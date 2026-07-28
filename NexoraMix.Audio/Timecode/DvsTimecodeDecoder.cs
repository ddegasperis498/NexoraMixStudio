namespace NexoraMix.Audio.Timecode;

public sealed class DvsTimecodeDecoder
{
    private readonly int _sampleRate;
    public DvsTimecodeDecoder(int sampleRate)
    {
        _sampleRate = Math.Max(1, sampleRate);
    }

    public DvsTimecodeFrame Decode(float[] stereoBuffer, int offset, int count)
    {
        if (stereoBuffer.Length == 0 || count < 4)
            return DvsTimecodeFrame.NoSignal;

        var samples = count / 2;
        double energy = 0;

        for (var frame = 0; frame < samples; frame++)
        {
            var left = stereoBuffer[offset + frame * 2];
            var right = stereoBuffer[offset + frame * 2 + 1];
            energy += left * left + right * right;
        }

        energy /= Math.Max(1, samples * 2);
        if (energy < 0.00001d)
        {
            return DvsTimecodeFrame.NoSignal;
        }

        var firstPhase = Math.Atan2(stereoBuffer[offset + 1], stereoBuffer[offset]);
        var lastOffset = offset + (samples - 1) * 2;
        var lastPhase = Math.Atan2(stereoBuffer[lastOffset + 1], stereoBuffer[lastOffset]);
        var delta = Unwrap(lastPhase - firstPhase);

        var blockSeconds = samples / (double)_sampleRate;
        var speed = blockSeconds > 0 ? delta / (Math.PI * 2d) / blockSeconds : 0d;
        var direction = Math.Abs(speed) < 0.01d
            ? DvsDirection.Stopped
            : speed > 0 ? DvsDirection.Forward : DvsDirection.Reverse;

        return new DvsTimecodeFrame(true, direction, speed, energy, lastPhase);
    }

    private static double Unwrap(double delta)
    {
        if (delta > Math.PI) delta -= Math.PI * 2d;
        if (delta < -Math.PI) delta += Math.PI * 2d;
        return delta;
    }
}

public enum DvsDirection
{
    Stopped,
    Forward,
    Reverse
}

public sealed record DvsTimecodeFrame(
    bool HasSignal,
    DvsDirection Direction,
    double RelativeSpeed,
    double SignalEnergy,
    double PhaseRadians)
{
    public static DvsTimecodeFrame NoSignal { get; } = new(false, DvsDirection.Stopped, 0d, 0d, 0d);
}
