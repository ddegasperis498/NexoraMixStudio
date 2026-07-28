using NAudio.Wave;
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
                0);
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
                0);
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
            beatIntervalSeconds);
    }

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
}
