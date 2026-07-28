namespace NexoraMix.Audio.Playback;

/// <summary>
/// Contratto sostituibile per il controllo del tempo. L'implementazione inclusa
/// usa resampling interpolato e non fornisce key-lock; un adapter professionale
/// può implementare lo stesso contratto senza modificare i deck o il mixer.
/// </summary>
public interface ITimeStretchProcessor
{
    double TempoRatio { get; set; }
    bool KeyLockEnabled { get; }
    void Reset();
}
