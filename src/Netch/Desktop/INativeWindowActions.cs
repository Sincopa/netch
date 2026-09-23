namespace Netch.Desktop;

public interface INativeWindowActions
{
    void BeginDrag();

    void Minimize();

    void ToggleMaximize();

    void CloseWindow();

    void ShowWindow();

    Task<string?> PickExecutableAsync(CancellationToken cancellationToken);

    Task<bool> ConfirmDangerousActionAsync(string title, string message, CancellationToken cancellationToken);

    void RequestRestart();
}
