namespace NexoraMix.Audio.Playback;

public sealed class AudioClock
{
    private long _framePosition;

    public long FramePosition => Interlocked.Read(ref _framePosition);

    public void Advance(int frames)
    {
        if (frames > 0) Interlocked.Add(ref _framePosition, frames);
    }

    public void Reset() => Interlocked.Exchange(ref _framePosition, 0);
}
