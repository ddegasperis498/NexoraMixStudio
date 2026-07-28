using NAudio.Wave;
using NexoraMix.Core.Models;

namespace NexoraMix.Audio.Playback;

internal sealed class CueMixSampleProvider : ISampleProvider
{
    private readonly IReadOnlyDictionary<DeckId, DeckChannel> _decks;
    private readonly AudioTapSampleProvider _masterTap;
    private readonly Dictionary<DeckId, bool> _deckCueEnabled = new();
    private float[] _scratch = Array.Empty<float>();
    private double _volume = 0.75d;
    private volatile bool _masterCueEnabled;

    public CueMixSampleProvider(
        WaveFormat waveFormat,
        IReadOnlyDictionary<DeckId, DeckChannel> decks,
        AudioTapSampleProvider masterTap)
    {
        WaveFormat = waveFormat;
        _decks = decks;
        foreach (var id in decks.Keys) _deckCueEnabled[id] = false;
        _masterTap = masterTap;
    }

    public WaveFormat WaveFormat { get; }
    public double Volume { get => Volatile.Read(ref _volume); set => Volatile.Write(ref _volume, Math.Clamp(value, 0d, 1.5d)); }
    public bool MasterCueEnabled { get => _masterCueEnabled; set => _masterCueEnabled = value; }

    public void SetDeckCue(DeckId id, bool enabled)
    {
        lock (_deckCueEnabled) _deckCueEnabled[id] = enabled;
    }

    public bool GetDeckCue(DeckId id)
    {
        lock (_deckCueEnabled) return _deckCueEnabled.TryGetValue(id, out var enabled) && enabled;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);
        EnsureScratch(count);
        var activeSources = 0;

        List<DeckChannel> activeDecks;
        lock (_deckCueEnabled)
        {
            activeDecks = _deckCueEnabled
                .Where(pair => pair.Value && _decks.ContainsKey(pair.Key))
                .Select(pair => _decks[pair.Key])
                .ToList();
        }

        foreach (var deck in activeDecks)
        {
            Array.Clear(_scratch, 0, count);
            var read = deck.ReadCue(_scratch, 0, count);
            if (read <= 0) continue;
            activeSources++;
            for (var i = 0; i < read; i++)
                buffer[offset + i] += _scratch[i];
        }

        if (MasterCueEnabled)
        {
            Array.Clear(_scratch, 0, count);
            var read = _masterTap.ReadTap(_scratch, 0, count);
            if (read > 0)
            {
                activeSources++;
                for (var i = 0; i < read; i++)
                    buffer[offset + i] += _scratch[i];
            }
        }

        if (activeSources > 0)
        {
            var gain = (float)(Volume / Math.Sqrt(activeSources));
            for (var i = 0; i < count; i++)
                buffer[offset + i] = Math.Clamp(buffer[offset + i] * gain, -1f, 1f);
        }

        return count;
    }

    private void EnsureScratch(int count)
    {
        if (_scratch.Length < count) _scratch = new float[count];
    }
}
