namespace NexoraMix.App.Services;

public sealed record AudioOutputDeviceOption(int DeviceNumber, string Name)
{
    public override string ToString() => Name;
}
