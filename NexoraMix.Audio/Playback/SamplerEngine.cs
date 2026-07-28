using System.IO;
using NAudio.Wave;

namespace NexoraMix.Audio.Playback;

public sealed class SamplerEngine : ISampleProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<int, SamplerSlot> _slots = new();
    private readonly List<SamplerVoice> _voices = new();
    private readonly StepSequencer _sequencer;
    private readonly float[] _scratch = new float[4096];

    public SamplerEngine(WaveFormat waveFormat, AudioClock clock)
    {
        WaveFormat = waveFormat;
        _sequencer = new StepSequencer(clock, waveFormat.SampleRate, Trigger);
    }

    public WaveFormat WaveFormat { get; }
    public StepSequencer Sequencer => _sequencer;
    public int ActiveVoiceCount { get { lock (_gate) return _voices.Count; } }
    public IReadOnlyCollection<int> LoadedSlots { get { lock (_gate) return _slots.Keys.ToArray(); } }

    public void LoadSlot(int slotIndex, string path, double gain = 1d, bool choke = false)
    {
        if (slotIndex is < 0 or > 31) throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (!File.Exists(path)) throw new FileNotFoundException("Sample non trovato.", path);

        var samples = ReadStereoSamples(path, WaveFormat.SampleRate);
        lock (_gate)
        {
            _slots[slotIndex] = new SamplerSlot(slotIndex, path, samples, Math.Clamp(gain, 0d, 2d), choke);
        }
    }

    public void UnloadSlot(int slotIndex)
    {
        lock (_gate)
        {
            _slots.Remove(slotIndex);
            _voices.RemoveAll(voice => voice.SlotIndex == slotIndex);
            _sequencer.ClearSlot(slotIndex);
        }
    }

    public bool Trigger(int slotIndex, double velocity = 1d)
    {
        lock (_gate)
        {
            if (!_slots.TryGetValue(slotIndex, out var slot)) return false;
            if (slot.Choke) _voices.RemoveAll(voice => voice.SlotIndex == slotIndex);
            _voices.Add(new SamplerVoice(slotIndex, slot.Samples, slot.Gain * Math.Clamp(velocity, 0d, 1d)));
            return true;
        }
    }

    public void StopAll()
    {
        lock (_gate) _voices.Clear();
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > buffer.Length) return 0;
        var availableBuffer = Math.Min(count, buffer.Length - offset);
        if (availableBuffer <= 0) return 0;

        Array.Clear(buffer, offset, availableBuffer);
        _sequencer.Tick();

        lock (_gate)
        {
            for (var voiceIndex = _voices.Count - 1; voiceIndex >= 0; voiceIndex--)
            {
                var voice = _voices[voiceIndex];
                var copied = voice.MixInto(buffer, offset, availableBuffer);
                if (copied < availableBuffer) _voices.RemoveAt(voiceIndex);
            }
        }

        return availableBuffer;
    }

    private float[] ReadStereoSamples(string path, int sampleRate)
    {
        using var reader = new AudioFileReader(path);
        var stereo = StereoSampleProviderFactory.Create(reader, sampleRate);
        var result = new List<float>((int)Math.Min(int.MaxValue, reader.TotalTime.TotalSeconds * sampleRate * 2d));
        int read;
        while ((read = stereo.Read(_scratch, 0, _scratch.Length)) > 0)
        {
            for (var i = 0; i < read; i++) result.Add(_scratch[i]);
        }
        return result.ToArray();
    }

    private sealed record SamplerSlot(int Index, string Path, float[] Samples, double Gain, bool Choke);

    private sealed class SamplerVoice
    {
        private readonly float[] _samples;
        private readonly double _gain;
        private int _position;

        public SamplerVoice(int slotIndex, float[] samples, double gain)
        {
            SlotIndex = slotIndex;
            _samples = samples;
            _gain = gain;
        }

        public int SlotIndex { get; }

        public int MixInto(float[] buffer, int offset, int count)
        {
            var available = Math.Min(count, _samples.Length - _position);
            for (var i = 0; i < available; i++)
                buffer[offset + i] += (float)(_samples[_position + i] * _gain);
            _position += available;
            return available;
        }
    }
}
