using NAudio.Midi;
using NexoraMix.Core.Controllers;

namespace NexoraMix.Audio.Controllers;

public sealed class GenericMidiControllerService : IDisposable
{
    private readonly object _gate = new();
    private MidiIn? _midiIn;
    private MidiOut? _midiOut;
    private bool _disposed;

    public event EventHandler<ControllerInputEvent>? InputReceived;
    public event EventHandler<string>? ConnectionChanged;

    public int? InputDeviceNumber { get; private set; }
    public int? OutputDeviceNumber { get; private set; }
    public bool IsInputConnected => _midiIn is not null;
    public bool IsOutputConnected => _midiOut is not null;

    public IReadOnlyList<ControllerDeviceInfo> EnumerateDevices()
    {
        var inputs = Enumerable.Range(0, MidiIn.NumberOfDevices)
            .Select(index => (Index: index, Name: MidiIn.DeviceInfo(index).ProductName))
            .ToList();
        var outputs = Enumerable.Range(0, MidiOut.NumberOfDevices)
            .Select(index => (Index: index, Name: MidiOut.DeviceInfo(index).ProductName))
            .ToList();

        var names = inputs.Select(item => item.Name)
            .Concat(outputs.Select(item => item.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToList();

        return names.Select(name =>
        {
            var input = inputs.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            var output = outputs.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            int? inputNumber = inputs.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) ? input.Index : null;
            int? outputNumber = outputs.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)) ? output.Index : null;
            return new ControllerDeviceInfo(inputNumber, outputNumber, name, $"midi:{name}");
        }).ToArray();
    }

    public void ConnectInput(int deviceNumber)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            DisconnectInputUnsafe();
            _midiIn = new MidiIn(deviceNumber);
            _midiIn.MessageReceived += OnMessageReceived;
            _midiIn.ErrorReceived += OnErrorReceived;
            _midiIn.Start();
            InputDeviceNumber = deviceNumber;
            ConnectionChanged?.Invoke(this, $"MIDI IN collegato: {MidiIn.DeviceInfo(deviceNumber).ProductName}");
        }
    }

    public void ConnectOutput(int deviceNumber)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            DisconnectOutputUnsafe();
            _midiOut = new MidiOut(deviceNumber);
            OutputDeviceNumber = deviceNumber;
            ConnectionChanged?.Invoke(this, $"MIDI OUT collegato: {MidiOut.DeviceInfo(deviceNumber).ProductName}");
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            DisconnectInputUnsafe();
            DisconnectOutputUnsafe();
            ConnectionChanged?.Invoke(this, "Controller MIDI scollegato");
        }
    }

    public void SendControlChange(int channel, int controller, int value)
    {
        lock (_gate)
        {
            if (_midiOut is null) return;
            var status = 0xB0 | (Math.Clamp(channel, 1, 16) - 1);
            var message = status | (Math.Clamp(controller, 0, 127) << 8) | (Math.Clamp(value, 0, 127) << 16);
            _midiOut.Send(message);
        }
    }

    public void SendNote(int channel, int note, int velocity, bool on)
    {
        lock (_gate)
        {
            if (_midiOut is null) return;
            var status = (on ? 0x90 : 0x80) | (Math.Clamp(channel, 1, 16) - 1);
            var message = status | (Math.Clamp(note, 0, 127) << 8) | (Math.Clamp(velocity, 0, 127) << 16);
            _midiOut.Send(message);
        }
    }

    private void OnMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        var raw = e.RawMessage;
        var status = raw & 0xFF;
        var command = status & 0xF0;
        var channel = (status & 0x0F) + 1;
        var data1 = (raw >> 8) & 0xFF;
        var data2 = (raw >> 16) & 0xFF;
        var kind = command switch
        {
            0x80 or 0x90 => ControllerMessageKind.Note,
            0xB0 => ControllerMessageKind.ControlChange,
            0xE0 => ControllerMessageKind.PitchBend,
            0xC0 => ControllerMessageKind.ProgramChange,
            0xA0 or 0xD0 => ControllerMessageKind.Aftertouch,
            _ => ControllerMessageKind.SystemExclusive
        };

        InputReceived?.Invoke(this, new ControllerInputEvent(
            DateTimeOffset.UtcNow,
            kind,
            channel,
            data1,
            data2,
            $"0x{raw:X8}"));
    }

    private void OnErrorReceived(object? sender, MidiInMessageEventArgs e) =>
        ConnectionChanged?.Invoke(this, $"Errore MIDI: 0x{e.RawMessage:X8}");

    private void DisconnectInputUnsafe()
    {
        if (_midiIn is null) return;
        try { _midiIn.Stop(); }
        catch (Exception ex) { ConnectionChanged?.Invoke(this, $"Arresto MIDI non confermato: {ex.Message}"); }
        _midiIn.MessageReceived -= OnMessageReceived;
        _midiIn.ErrorReceived -= OnErrorReceived;
        _midiIn.Dispose();
        _midiIn = null;
        InputDeviceNumber = null;
    }

    private void DisconnectOutputUnsafe()
    {
        _midiOut?.Dispose();
        _midiOut = null;
        OutputDeviceNumber = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GenericMidiControllerService));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            DisconnectInputUnsafe();
            DisconnectOutputUnsafe();
        }
        GC.SuppressFinalize(this);
    }
}
