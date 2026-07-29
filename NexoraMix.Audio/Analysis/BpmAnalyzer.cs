using NAudio.Dsp;
using NAudio.Wave;
using NexoraMix.Core.Models;
using NexoraMix.Core.Services;
using IOFile = System.IO.File;
using IOFileNotFoundException = System.IO.FileNotFoundException;

namespace NexoraMix.Audio.Analysis;

/// <summary>
/// Analizza un file locale e ricava waveform, BPM, prima battuta e numero stimato
/// di battute/misure. Spotify non espone campioni PCM: questo analizzatore lavora
/// esclusivamente su file audio accessibili localmente.
/// </summary>
public sealed class BpmAnalyzer
{
    private const int EnvelopeRate = 200;
    private const int WaveformPoints = 2200;
    private const int DefaultBeatsPerBar = 4;

    public Task<AudioAnalysisResult> AnalyzeAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => Analyze(path, cancellationToken), cancellationToken);

    public AudioAnalysisResult Analyze(string path, CancellationToken cancellationToken = default)
    {
        if (!IOFile.Exists(path)) throw new IOFileNotFoundException("File audio non trovato.", path);

        using var reader = new AudioFileReader(path);
        var durationSeconds = reader.TotalTime.TotalSeconds;
        var channels = reader.WaveFormat.Channels;
        var sampleRate = reader.WaveFormat.SampleRate;
        var samplesPerEnvelope = Math.Max(1, sampleRate / EnvelopeRate);
        var buffer = new float[Math.Max(8192, sampleRate / 2) * channels];
        var envelope = new List<float>((int)Math.Ceiling(durationSeconds * EnvelopeRate));
        var spectrum = new SpectralAccumulator(sampleRate);
        var truePeak = new TruePeakEstimator();

        double envelopeSum = 0;
        double squareSum = 0;
        double peak = 0;
        long totalMonoSamples = 0;
        var monoSamplesInEnvelope = 0;
        int read;

        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < read; i += channels)
            {
                double monoSigned = 0;
                for (var channel = 0; channel < channels && i + channel < read; channel++)
                    monoSigned += buffer[i + channel];

                monoSigned /= channels;
                spectrum.Add((float)monoSigned);
                truePeak.Add(monoSigned);
                var absolute = Math.Abs(monoSigned);
                envelopeSum += absolute;
                squareSum += monoSigned * monoSigned;
                peak = Math.Max(peak, absolute);
                monoSamplesInEnvelope++;
                totalMonoSamples++;

                if (monoSamplesInEnvelope < samplesPerEnvelope) continue;
                envelope.Add((float)(envelopeSum / monoSamplesInEnvelope));
                envelopeSum = 0;
                monoSamplesInEnvelope = 0;
            }
        }

        if (monoSamplesInEnvelope > 0)
            envelope.Add((float)(envelopeSum / monoSamplesInEnvelope));

        spectrum.Complete();

        var rms = totalMonoSamples > 0 ? Math.Sqrt(squareSum / totalMonoSamples) : 0;
        if (envelope.Count < EnvelopeRate * 4)
        {
            return new AudioAnalysisResult(
                durationSeconds,
                0,
                0,
                BuildWaveform(envelope),
                0,
                0,
                rms,
                peak,
                0,
                0,
                0)
            {
                Features = BuildFeatures(0d, 0d, 0d, rms, peak, truePeak.Peak, spectrum)
            };
        }

        var normalized = Normalize(envelope);
        var smoothed = MovingAverage(normalized, 7);
        var onset = BuildOnset(smoothed);
        var (bpm, lag, confidence) = EstimateBpm(onset);

        if (lag <= 0 || bpm <= 0)
        {
            return new AudioAnalysisResult(
                durationSeconds,
                0,
                0,
                BuildWaveform(normalized),
                confidence,
                0,
                rms,
                peak,
                0,
                0,
                0)
            {
                Features = BuildFeatures(0d, confidence, 0d, rms, peak, truePeak.Peak, spectrum)
            };
        }

        var phase = EstimateBeatPhase(onset, lag);
        var beatFrames = TrackBeatPositions(onset, lag, phase.Phase);
        var refinedLag = RefineLagFromTrackedBeats(beatFrames, lag);

        if (refinedLag > 0 && Math.Abs(refinedLag - lag) <= lag * 0.16d)
        {
            lag = refinedLag;
            bpm = NormalizeBpm(60d * EnvelopeRate / lag);
            phase = EstimateBeatPhase(onset, lag);
            beatFrames = TrackBeatPositions(onset, lag, phase.Phase);
        }

        var beatOffset = beatFrames.Count > 0
            ? beatFrames[0] / (double)EnvelopeRate
            : phase.Phase / (double)EnvelopeRate;
        beatOffset = Math.Clamp(beatOffset, 0d, Math.Max(0d, durationSeconds));

        // Il conteggio copre l'intera griglia regolare fino alla fine della traccia.
        // I transienti rilevati vengono usati per BPM e fase; la griglia permette di
        // contare anche battute meno marcate, intro e passaggi con batteria debole.
        var beatIntervalSeconds = bpm > 0 ? 60d / bpm : 0;
        var detectedBeatCount = beatIntervalSeconds > 0 && durationSeconds >= beatOffset
            ? 1 + (int)Math.Floor((durationSeconds - beatOffset) / beatIntervalSeconds)
            : 0;
        var estimatedBarCount = detectedBeatCount > 0
            ? (int)Math.Ceiling(detectedBeatCount / (double)DefaultBeatsPerBar)
            : 0;

        var trackingQuality = CalculateTrackingQuality(onset, beatFrames);
        var combinedPhaseConfidence = Math.Clamp(phase.Confidence * 0.70d + trackingQuality * 0.30d, 0d, 1d);

        return new AudioAnalysisResult(
            durationSeconds,
            Math.Round(bpm, 2),
            beatOffset,
            BuildWaveform(normalized),
            confidence,
            combinedPhaseConfidence,
            rms,
            peak,
            detectedBeatCount,
            estimatedBarCount,
            beatIntervalSeconds)
        {
            Features = BuildFeatures(bpm, confidence, combinedPhaseConfidence, rms, peak, truePeak.Peak, spectrum)
        };
    }

    private static TrackAudioFeatures BuildFeatures(
        double bpm,
        double bpmConfidence,
        double phaseConfidence,
        double rms,
        double samplePeak,
        double estimatedTruePeak,
        SpectralAccumulator spectrum)
    {
        var key = spectrum.EstimateKey();
        var rmsDb = ToDbFs(rms);
        double? loudness = rms > 0d ? -0.691d + 10d * Math.Log10(rms * rms) : null;
        var loudnessEnergy = rmsDb.HasValue ? Math.Clamp((rmsDb.Value + 45d) / 39d, 0d, 1d) : 0d;
        var crest = rms > 0d ? Math.Clamp(samplePeak / rms / 8d, 0d, 1d) : 0d;
        var energy = Math.Clamp(
            loudnessEnergy * 0.60d +
            spectrum.HighFrequencyRatio * 0.15d +
            crest * 0.10d +
            bpmConfidence * 0.15d,
            0d,
            1d);
        double? danceability = bpm > 0d
            ? Math.Clamp(bpmConfidence * 0.55d + phaseConfidence * 0.30d + spectrum.TransientRatio * 0.15d, 0d, 1d)
            : null;
        var availableConfidences = new[]
            {
                bpm > 0d ? bpmConfidence : double.NaN,
                key.Confidence ?? double.NaN,
                spectrum.FrameCount > 0 ? 0.65d : double.NaN
            }
            .Where(value => !double.IsNaN(value))
            .ToArray();

        return new TrackAudioFeatures
        {
            Status = AudioFeatureAnalysisStatus.Partial,
            AnalysisVersion = TrackAudioFeatures.CurrentAnalysisVersion,
            AnalyzedAtUtc = DateTimeOffset.UtcNow,
            OverallConfidence = availableConfidences.Length > 0 ? availableConfidences.Average() : null,
            Bpm = bpm > 0d ? Math.Round(bpm, 2) : null,
            BpmConfidence = bpm > 0d ? Math.Clamp(bpmConfidence, 0d, 1d) : null,
            MusicalKey = key.Key,
            MusicalMode = key.Mode,
            CamelotKey = key.Key is not null ? CamelotCompatibilityService.ToCamelot(key.Key, key.Mode) : null,
            KeyConfidence = key.Confidence,
            EstimatedIntegratedLufs = loudness.HasValue ? Math.Round(loudness.Value, 2) : null,
            SamplePeakDbFs = ToDbFs(samplePeak),
            EstimatedTruePeakDbFs = ToDbFs(estimatedTruePeak),
            Energy = energy,
            Danceability = danceability,
            SpectralCentroidHz = spectrum.CentroidHz,
            BassIntensity = spectrum.BassIntensity,
            // Una classificazione vocale affidabile richiede un modello dedicato.
            VocalPresence = VocalPresence.Unknown,
            VocalConfidence = null,
            Sections = Array.Empty<TrackAudioSection>()
        };
    }

    private static double? ToDbFs(double linear) =>
        linear > 0d ? Math.Round(20d * Math.Log10(linear), 2) : null;

    private static float[] Normalize(IReadOnlyList<float> values)
    {
        if (values.Count == 0) return Array.Empty<float>();
        var sorted = values.OrderBy(value => value).ToArray();
        var percentileIndex = Math.Clamp((int)(sorted.Length * 0.98), 0, sorted.Length - 1);
        var scale = Math.Max(0.000001f, sorted[percentileIndex]);
        var result = new float[values.Count];
        for (var i = 0; i < values.Count; i++) result[i] = Math.Clamp(values[i] / scale, 0, 1);
        return result;
    }

    private static float[] MovingAverage(IReadOnlyList<float> values, int radius)
    {
        var result = new float[values.Count];
        double running = 0;
        var queue = new Queue<float>();
        var size = radius * 2 + 1;

        for (var i = 0; i < values.Count; i++)
        {
            running += values[i];
            queue.Enqueue(values[i]);
            if (queue.Count > size) running -= queue.Dequeue();
            result[i] = (float)(running / queue.Count);
        }
        return result;
    }

    private static float[] BuildOnset(IReadOnlyList<float> values)
    {
        var result = new float[values.Count];
        var localMean = MovingAverage(values, 24);
        for (var i = 1; i < values.Count; i++)
        {
            var difference = values[i] - values[i - 1];
            var adaptive = values[i] - localMean[i] * 0.85f;
            result[i] = Math.Max(0, difference * 1.4f + adaptive);
        }
        return result;
    }

    private static (double Bpm, int Lag, double Confidence) EstimateBpm(IReadOnlyList<float> onset)
    {
        var minLag = (int)Math.Floor(EnvelopeRate * 60d / 190d);
        var maxLag = (int)Math.Ceiling(EnvelopeRate * 60d / 70d);
        var scores = new Dictionary<int, double>();
        double bestScore = 0;
        var bestLag = 0;

        for (var lag = minLag; lag <= maxLag; lag++)
        {
            double score = 0;
            for (var i = lag; i < onset.Count; i++) score += onset[i] * onset[i - lag];

            var half = lag / 2;
            if (half >= minLag)
            {
                double halfScore = 0;
                for (var i = half; i < onset.Count; i++) halfScore += onset[i] * onset[i - half];
                score += halfScore * 0.18d;
            }

            var twice = lag * 2;
            if (twice <= maxLag)
            {
                double doubleScore = 0;
                for (var i = twice; i < onset.Count; i++) doubleScore += onset[i] * onset[i - twice];
                score += doubleScore * 0.12d;
            }

            scores[lag] = score;
            if (score <= bestScore) continue;
            bestScore = score;
            bestLag = lag;
        }

        if (bestLag == 0 || bestScore <= 0) return (0, 0, 0);

        var bpm = NormalizeBpm(60d * EnvelopeRate / bestLag);
        var average = scores.Values.Average();
        var confidence = Math.Clamp((bestScore - average) / Math.Max(bestScore, 0.000001d), 0, 1);
        return (bpm, bestLag, confidence);
    }

    private static double NormalizeBpm(double bpm)
    {
        while (bpm < 80d) bpm *= 2d;
        while (bpm > 175d) bpm /= 2d;
        return bpm;
    }

    private static (int Phase, double Confidence) EstimateBeatPhase(IReadOnlyList<float> onset, int lag)
    {
        var scores = new double[lag];
        var bestPhase = 0;
        double bestScore = double.MinValue;

        for (var phase = 0; phase < lag; phase++)
        {
            double score = 0;
            for (var i = phase; i < onset.Count; i += lag) score += onset[i];
            scores[phase] = score;
            if (score <= bestScore) continue;
            bestScore = score;
            bestPhase = phase;
        }

        var average = scores.Length > 0 ? scores.Average() : 0;
        var confidence = bestScore > 0
            ? Math.Clamp((bestScore - average) / Math.Max(bestScore, 0.000001d), 0, 1)
            : 0;

        return (bestPhase, confidence);
    }

    private static List<int> TrackBeatPositions(IReadOnlyList<float> onset, int lag, int phase)
    {
        var result = new List<int>();
        if (lag <= 0 || onset.Count == 0) return result;

        var searchRadius = Math.Max(2, (int)Math.Round(lag * 0.15d));
        var predicted = Math.Clamp(phase, 0, onset.Count - 1);
        var previous = -1;

        while (predicted < onset.Count)
        {
            var start = Math.Max(previous + 1, predicted - searchRadius);
            var end = Math.Min(onset.Count - 1, predicted + searchRadius);
            var bestIndex = Math.Clamp(predicted, start, end);
            var bestValue = onset[bestIndex];

            for (var index = start; index <= end; index++)
            {
                if (onset[index] <= bestValue) continue;
                bestValue = onset[index];
                bestIndex = index;
            }

            result.Add(bestIndex);
            previous = bestIndex;
            predicted = bestIndex + lag;
        }

        return result;
    }

    private static int RefineLagFromTrackedBeats(IReadOnlyList<int> beatFrames, int fallbackLag)
    {
        if (beatFrames.Count < 4) return fallbackLag;
        var intervals = new List<int>(beatFrames.Count - 1);
        for (var index = 1; index < beatFrames.Count; index++)
        {
            var interval = beatFrames[index] - beatFrames[index - 1];
            if (interval > 0) intervals.Add(interval);
        }

        if (intervals.Count == 0) return fallbackLag;
        intervals.Sort();
        var median = intervals[intervals.Count / 2];
        var accepted = intervals.Where(value => Math.Abs(value - median) <= median * 0.18d).ToArray();
        if (accepted.Length == 0) return fallbackLag;
        return Math.Max(1, (int)Math.Round(accepted.Average()));
    }

    private static double CalculateTrackingQuality(IReadOnlyList<float> onset, IReadOnlyList<int> beats)
    {
        if (beats.Count == 0 || onset.Count == 0) return 0d;
        var globalAverage = onset.Average(value => (double)value);
        var beatAverage = beats
            .Where(index => index >= 0 && index < onset.Count)
            .Select(index => (double)onset[index])
            .DefaultIfEmpty(0d)
            .Average();
        if (beatAverage <= 0d) return 0d;
        return Math.Clamp((beatAverage - globalAverage) / beatAverage, 0d, 1d);
    }

    private static float[] BuildWaveform(IReadOnlyList<float> envelope)
    {
        if (envelope.Count == 0) return Array.Empty<float>();
        var points = Math.Min(WaveformPoints, envelope.Count);
        var result = new float[points];
        var block = envelope.Count / (double)points;

        for (var point = 0; point < points; point++)
        {
            var start = (int)Math.Floor(point * block);
            var end = Math.Min(envelope.Count, Math.Max(start + 1, (int)Math.Ceiling((point + 1) * block)));
            float maximum = 0;
            for (var i = start; i < end; i++) maximum = Math.Max(maximum, envelope[i]);
            result[point] = maximum;
        }
        return result;
    }

    private sealed class TruePeakEstimator
    {
        private readonly Queue<double> _samples = new(4);

        public double Peak { get; private set; }

        public void Add(double sample)
        {
            Peak = Math.Max(Peak, Math.Abs(sample));
            _samples.Enqueue(sample);
            if (_samples.Count < 4) return;

            var values = _samples.ToArray();
            for (var step = 1; step < 4; step++)
            {
                var t = step / 4d;
                var interpolated = 0.5d * ((2d * values[1]) +
                    (-values[0] + values[2]) * t +
                    (2d * values[0] - 5d * values[1] + 4d * values[2] - values[3]) * t * t +
                    (-values[0] + 3d * values[1] - 3d * values[2] + values[3]) * t * t * t);
                Peak = Math.Max(Peak, Math.Abs(interpolated));
            }
            _samples.Dequeue();
        }
    }

    private sealed class SpectralAccumulator
    {
        private const int FftSize = 2048;
        private const int FftExponent = 11;
        private const int HopSize = FftSize / 2;
        private static readonly double[] MajorProfile =
            { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
        private static readonly double[] MinorProfile =
            { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
        private static readonly string[] PitchNames =
            { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        private readonly int _sampleRate;
        private readonly float[] _samples = new float[FftSize];
        private readonly Complex[] _fft = new Complex[FftSize];
        private readonly double[] _chroma = new double[12];
        private int _sampleCount;
        private double _centroidWeightedSum;
        private double _spectralMagnitudeSum;
        private double _bassMagnitudeSum;
        private double _highMagnitudeSum;
        private double _previousFrameEnergy;
        private double _positiveEnergyChange;
        private double _totalEnergyChange;

        public SpectralAccumulator(int sampleRate) => _sampleRate = sampleRate;

        public int FrameCount { get; private set; }
        public double? CentroidHz => _spectralMagnitudeSum > 0d
            ? Math.Round(_centroidWeightedSum / _spectralMagnitudeSum, 2)
            : null;
        public double? BassIntensity => _spectralMagnitudeSum > 0d
            ? Math.Clamp(_bassMagnitudeSum / _spectralMagnitudeSum, 0d, 1d)
            : null;
        public double HighFrequencyRatio => _spectralMagnitudeSum > 0d
            ? Math.Clamp(_highMagnitudeSum / _spectralMagnitudeSum, 0d, 1d)
            : 0d;
        public double TransientRatio => _totalEnergyChange > 0d
            ? Math.Clamp(_positiveEnergyChange / _totalEnergyChange, 0d, 1d)
            : 0d;

        public void Add(float sample)
        {
            _samples[_sampleCount++] = sample;
            if (_sampleCount < FftSize) return;
            ProcessFrame();
            Array.Copy(_samples, HopSize, _samples, 0, HopSize);
            _sampleCount = HopSize;
        }

        public void Complete()
        {
            if (_sampleCount < 128) return;
            Array.Clear(_samples, _sampleCount, FftSize - _sampleCount);
            ProcessFrame();
            _sampleCount = 0;
        }

        public (string? Key, MusicalMode Mode, double? Confidence) EstimateKey()
        {
            var chromaTotal = _chroma.Sum();
            if (chromaTotal <= 0d) return (null, MusicalMode.Unknown, null);

            var bestScore = double.MinValue;
            var secondScore = double.MinValue;
            var bestRoot = 0;
            var bestMode = MusicalMode.Unknown;
            for (var root = 0; root < 12; root++)
            {
                EvaluateProfile(root, MajorProfile, MusicalMode.Major, ref bestScore, ref secondScore, ref bestRoot, ref bestMode);
                EvaluateProfile(root, MinorProfile, MusicalMode.Minor, ref bestScore, ref secondScore, ref bestRoot, ref bestMode);
            }

            if (bestScore <= 0d || bestMode == MusicalMode.Unknown) return (null, MusicalMode.Unknown, null);
            var confidence = Math.Clamp((bestScore - Math.Max(0d, secondScore)) / bestScore * 3d, 0d, 1d);
            // Un risultato quasi indistinguibile dall'alternativa resta sconosciuto.
            return confidence >= 0.05d
                ? (PitchNames[bestRoot], bestMode, confidence)
                : (null, MusicalMode.Unknown, confidence);
        }

        private void ProcessFrame()
        {
            for (var index = 0; index < FftSize; index++)
            {
                _fft[index].X = _samples[index] * (float)FastFourierTransform.HannWindow(index, FftSize);
                _fft[index].Y = 0f;
            }
            FastFourierTransform.FFT(true, FftExponent, _fft);

            double frameEnergy = 0d;
            for (var bin = 1; bin < FftSize / 2; bin++)
            {
                var frequency = bin * _sampleRate / (double)FftSize;
                var magnitude = Math.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y);
                if (magnitude <= 0d) continue;

                _centroidWeightedSum += frequency * magnitude;
                _spectralMagnitudeSum += magnitude;
                frameEnergy += magnitude * magnitude;
                if (frequency <= 250d) _bassMagnitudeSum += magnitude;
                if (frequency >= 2_000d) _highMagnitudeSum += magnitude;
                if (frequency is < 55d or > 5_000d) continue;

                var midi = 69d + 12d * Math.Log2(frequency / 440d);
                var pitchClass = ((int)Math.Round(midi) % 12 + 12) % 12;
                _chroma[pitchClass] += magnitude;
            }

            if (FrameCount > 0)
            {
                var change = frameEnergy - _previousFrameEnergy;
                _totalEnergyChange += Math.Abs(change);
                if (change > 0d) _positiveEnergyChange += change;
            }
            _previousFrameEnergy = frameEnergy;
            FrameCount++;
        }

        private void EvaluateProfile(
            int root,
            IReadOnlyList<double> profile,
            MusicalMode mode,
            ref double bestScore,
            ref double secondScore,
            ref int bestRoot,
            ref MusicalMode bestMode)
        {
            double score = 0d;
            double profileNorm = 0d;
            double chromaNorm = 0d;
            for (var index = 0; index < 12; index++)
            {
                var chroma = _chroma[(root + index) % 12];
                score += chroma * profile[index];
                profileNorm += profile[index] * profile[index];
                chromaNorm += chroma * chroma;
            }
            score /= Math.Sqrt(Math.Max(double.Epsilon, profileNorm * chromaNorm));
            if (score > bestScore)
            {
                secondScore = bestScore;
                bestScore = score;
                bestRoot = root;
                bestMode = mode;
            }
            else if (score > secondScore)
            {
                secondScore = score;
            }
        }
    }
}
