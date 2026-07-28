using NAudio.Wave;
using IOPath = System.IO.Path;
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;

namespace NexoraMix.Audio.Playback;

/// <summary>
/// Creates two royalty-free synthetic demo tracks when the packaged WAV files are unavailable.
/// This makes the DEMO button reliable from Visual Studio, published builds and copied folders.
/// </summary>
public static class DemoAudioFactory
{
    public static IReadOnlyList<string> EnsureDemoFiles()
    {
        var packaged = TryFindPackagedDemoFiles();
        if (packaged.Count >= 2) return packaged;

        var folder = IOPath.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexoraMix",
            "DemoAudio");

        IODirectory.CreateDirectory(folder);

        var first = IOPath.Combine(folder, "Nexora Demo - Aurora 118 BPM.wav");
        var second = IOPath.Combine(folder, "Nexora Demo - Pulse 124 BPM.wav");

        EnsureTrack(first, 118, 46.0, 52.0);
        EnsureTrack(second, 124, 52.0, 65.0);

        return new[] { first, second };
    }

    private static List<string> TryFindPackagedDemoFiles()
    {
        var candidates = new List<string>();
        var roots = new[]
        {
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
            IOPath.GetFullPath(IOPath.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."))
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var folder = IOPath.Combine(root, "DemoAudio");
            if (!IODirectory.Exists(folder)) continue;

            candidates.AddRange(IODirectory.GetFiles(folder, "*.wav"));
            if (candidates.Count >= 2) break;
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToList();
    }

    private static void EnsureTrack(string path, double bpm, double bassFrequency, double leadFrequency)
    {
        if (IOFile.Exists(path)) return;

        const int sampleRate = 44_100;
        const int channels = 2;
        const double durationSeconds = 32.0;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        var beatLength = 60.0 / bpm;
        var totalFrames = (int)(sampleRate * durationSeconds);

        using var writer = new WaveFileWriter(path, format);

        for (var frame = 0; frame < totalFrames; frame++)
        {
            var time = frame / (double)sampleRate;
            var beatPhase = time % beatLength;
            var beatIndex = (int)Math.Floor(time / beatLength);

            var kickEnvelope = Math.Exp(-beatPhase * 22.0);
            var kick = Math.Sin(2.0 * Math.PI * (72.0 - beatPhase * 34.0) * time) * kickEnvelope * 0.62;

            var clapPhase = (time + beatLength * 0.5) % beatLength;
            var clapEnvelope = Math.Exp(-clapPhase * 35.0);
            var clap = (PseudoNoise(frame) * 2.0 - 1.0) * clapEnvelope * (beatIndex % 2 == 1 ? 0.18 : 0.06);

            var bass = Math.Sin(2.0 * Math.PI * bassFrequency * time) * 0.11;
            var lead = Math.Sin(2.0 * Math.PI * leadFrequency * time + Math.Sin(time * 0.7) * 0.35) * 0.055;
            var shimmer = Math.Sin(2.0 * Math.PI * leadFrequency * 2.0 * time) * 0.025;

            var fadeIn = Math.Clamp(time / 0.8, 0.0, 1.0);
            var fadeOut = Math.Clamp((durationSeconds - time) / 1.2, 0.0, 1.0);
            var master = fadeIn * fadeOut;

            var mono = Math.Clamp((kick + clap + bass + lead + shimmer) * master, -0.92, 0.92);
            var pan = Math.Sin(time * 0.42) * 0.08;
            writer.WriteSample((float)(mono * (1.0 - pan)));
            writer.WriteSample((float)(mono * (1.0 + pan)));
        }
    }

    private static double PseudoNoise(int value)
    {
        unchecked
        {
            var x = (uint)value;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x / (double)uint.MaxValue;
        }
    }
}
