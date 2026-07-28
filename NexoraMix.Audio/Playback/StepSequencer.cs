namespace NexoraMix.Audio.Playback;

public sealed class StepSequencer
{
    private readonly object _gate = new();
    private readonly AudioClock _clock;
    private readonly int _sampleRate;
    private readonly Func<int, double, bool> _trigger;
    private readonly List<SequencerStep> _steps = new();
    private long _nextStepFrame = -1;
    private int _stepIndex;
    private bool _enabled;
    private double _bpm = 120d;

    public StepSequencer(AudioClock clock, int sampleRate, Func<int, double, bool> trigger)
    {
        _clock = clock;
        _sampleRate = sampleRate;
        _trigger = trigger;
    }

    public bool Enabled { get { lock (_gate) return _enabled; } }
    public int StepCount { get; private set; } = 16;
    public int CurrentStep { get { lock (_gate) return _stepIndex; } }

    public void Configure(double bpm, int stepCount = 16)
    {
        lock (_gate)
        {
            _bpm = Math.Clamp(bpm, 40d, 240d);
            StepCount = Math.Clamp(stepCount, 1, 64);
            _stepIndex = 0;
            _nextStepFrame = _clock.FramePosition;
        }
    }

    public void SetStep(int step, int slotIndex, double velocity = 1d)
    {
        lock (_gate)
        {
            var normalizedStep = Math.Clamp(step, 0, StepCount - 1);
            _steps.RemoveAll(item => item.Step == normalizedStep && item.SlotIndex == slotIndex);
            _steps.Add(new SequencerStep(normalizedStep, slotIndex, Math.Clamp(velocity, 0d, 1d)));
        }
    }

    public void ClearSlot(int slotIndex)
    {
        lock (_gate) _steps.RemoveAll(step => step.SlotIndex == slotIndex);
    }

    public void Clear()
    {
        lock (_gate) _steps.Clear();
    }

    public void Start()
    {
        lock (_gate)
        {
            _enabled = true;
            _stepIndex = 0;
            _nextStepFrame = _clock.FramePosition;
        }
    }

    public void Stop()
    {
        lock (_gate) _enabled = false;
    }

    public void Tick()
    {
        List<SequencerStep> due = new();
        lock (_gate)
        {
            if (!_enabled) return;
            var current = _clock.FramePosition;
            if (_nextStepFrame < 0) _nextStepFrame = current;

            var framesPerStep = Math.Max(1, (long)Math.Round(_sampleRate * 60d / _bpm / 4d));
            var guard = 0;
            while (current >= _nextStepFrame && guard++ < StepCount)
            {
                due.AddRange(_steps.Where(step => step.Step == _stepIndex));
                _stepIndex = (_stepIndex + 1) % StepCount;
                _nextStepFrame += framesPerStep;
            }
        }

        foreach (var step in due) _trigger(step.SlotIndex, step.Velocity);
    }

    private sealed record SequencerStep(int Step, int SlotIndex, double Velocity);
}
