namespace Netch.Application;

public sealed record AppEventMessage(string Name, object Payload);

public static class AppEvents
{
    public static event EventHandler<AppEventMessage>? Published;

    public static void Publish(string name, object? payload = null)
    {
        Published?.Invoke(null, new AppEventMessage(name, payload ?? new { }));
    }
}
