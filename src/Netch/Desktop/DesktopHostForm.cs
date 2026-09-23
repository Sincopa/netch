using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Netch.Application;
using Netch.Properties;
using Netch.Utils;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32;

namespace Netch.Desktop;

[Fody.ConfigureAwait(true)]
public sealed class DesktopHostForm : Form, INativeWindowActions
{
    private const string AppOrigin = "https://app.netch.local";
    private readonly WebView2 _webView;
    private readonly NotifyIcon _trayIcon;
    private readonly CatalogService _catalog;
    private readonly ConnectionService _connection;
    private readonly AppFacade _app;
    private WebViewRpcBridge? _bridge;
    private ToolStripMenuItem? _trayConnectionItem;
    private ToolStripMenuItem? _trayServersItem;
    private ToolStripMenuItem? _traySubscriptionsItem;
    private ToolStripMenuItem? _trayCloseToTrayItem;
    private System.Windows.Forms.Timer? _restartTimer;
    private bool _exitRequested;
    private bool _shutdownStarted;
    private bool _shutdownComplete;
    private bool _resumeConnection;

    public DesktopHostForm(LogBufferSink logSink)
    {
        Text = "Netch";
        Icon = Resources.icon;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(920, 640);
        var workArea = Screen.FromControl(this).WorkingArea;
        Size = new Size(Math.Min(1440, workArea.Width), Math.Min(860, workArea.Height));
        BackColor = Color.FromArgb(16, 17, 19);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = true;
        MinimizeBox = true;
        KeyPreview = true;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(16, 17, 19)
        };
        Controls.Add(_webView);

        _catalog = new CatalogService();
        _catalog.LoadModes();
        _connection = new ConnectionService(_catalog);
        var diagnostics = new DiagnosticsService();
        _app = new AppFacade(
            _catalog,
            _connection,
            new ServerService(_catalog, _connection),
            new ProfileService(_catalog, _connection),
            new SubscriptionService(_connection),
            new ProcessService(_connection),
            new ModeManagementService(_catalog, _connection),
            new SettingsService(_connection),
            diagnostics,
            new DriverService(_connection, diagnostics),
            new UpdateService(_connection),
            new ServerCountryService(),
            logSink);

        _trayIcon = new NotifyIcon
        {
            Icon = Resources.icon,
            Text = "Netch",
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
        AppEvents.Published += OnAppEvent;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Program.SingleInstance.StartListenServer();
        try
        {
            await InitializeWebViewAsync();

            if (Global.Settings.UpdateServersWhenOpened)
                _ = RefreshSubscriptionsOnStartupAsync();

            if (Global.Settings.CheckUpdateWhenOpened)
                _ = CheckUpdatesOnStartupAsync();

            if (Global.Settings.StartWhenOpened)
                _ = ConnectSelectedAsync();

            if (Global.Settings.MinimizeWhenStarted)
            {
                if (Global.Settings.ExitWhenClosed)
                    WindowState = FormWindowState.Minimized;
                else
                    Hide();
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "WebView2 initialization failed");
            ShowWebViewFailure(exception.Message);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        var userDataFolder = Path.Combine(Global.NetchDir, "data", "webview2");
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _webView.EnsureCoreWebView2Async(environment);

        var core = _webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.NavigationCompleted += (_, args) =>
        {
            if (args.IsSuccess)
                Log.Information("WebUI loaded");
            else
                Log.Error("WebUI navigation failed: {Status}", args.WebErrorStatus);
        };

        var devUrl = Environment.GetEnvironmentVariable("NETCH_WEBUI_DEV_URL");
        var allowedOrigins = new List<string> { AppOrigin };
        if (!string.IsNullOrWhiteSpace(devUrl) && Uri.TryCreate(devUrl, UriKind.Absolute, out var devUri) && devUri.IsLoopback)
            allowedOrigins.Add(devUri.GetLeftPart(UriPartial.Authority));

        core.NavigationStarting += (_, args) =>
        {
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) ||
                !allowedOrigins.Contains(target.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase))
                args.Cancel = true;
        };

        _bridge = new WebViewRpcBridge(_webView, _app, this, allowedOrigins);
        _ = _app.Countries.WarmAsync();

        if (allowedOrigins.Count > 1)
        {
            _webView.Source = new Uri(devUrl!);
            return;
        }

        var webRoot = Path.Combine(AppContext.BaseDirectory, "webui");
        if (!Directory.Exists(webRoot))
            throw new DirectoryNotFoundException($"WebUI assets were not found at '{webRoot}'. Run npm build before starting Netch.");

        core.SetVirtualHostNameToFolderMapping(
            "app.netch.local",
            webRoot,
            CoreWebView2HostResourceAccessKind.DenyCors);
        _webView.Source = new Uri(AppOrigin + "/index.html");
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(i18N.Translate("Open Netch"), null, (_, _) => ShowWindow());
        _trayConnectionItem = new ToolStripMenuItem(i18N.Translate("Connect"), null, (_, _) => ToggleConnectionAsync().Forget());
        menu.Items.Add(_trayConnectionItem);
        _trayServersItem = new ToolStripMenuItem(i18N.Translate("Servers"));
        menu.Items.Add(_trayServersItem);
        _traySubscriptionsItem = new ToolStripMenuItem(i18N.Translate("Update subscriptions"), null, (_, _) => RefreshSubscriptionsOnStartupAsync().Forget());
        menu.Items.Add(_traySubscriptionsItem);
        _trayCloseToTrayItem = new ToolStripMenuItem(i18N.Translate("Close window to tray"), null, (_, _) => ToggleCloseToTrayAsync().Forget())
        {
            Checked = !Global.Settings.ExitWhenClosed,
            CheckOnClick = false
        };
        menu.Items.Add(_trayCloseToTrayItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(i18N.Translate("Exit"), null, (_, _) =>
        {
            _exitRequested = true;
            Close();
        });
        menu.Opening += (_, _) => RefreshTrayServersMenu();
        return menu;
    }

    private void RefreshTrayServersMenu()
    {
        if (_trayServersItem is null)
            return;

        _trayServersItem.DropDownItems.Clear();
        var servers = _catalog.GetServers();
        var selectedId = _catalog.GetSelectedServerId();
        var selectable = _connection.GetState().Status is "disconnected" or "error";
        var selected = servers.FirstOrDefault(server => server.Id == selectedId);
        var favorites = servers.Where(server => server.IsFavorite).ToArray();

        if (selected is not null && !selected.IsFavorite)
        {
            _trayServersItem.DropDownItems.Add(CreateTrayServerItem(selected, selectedId, selectable));
            if (favorites.Length > 0)
                _trayServersItem.DropDownItems.Add(new ToolStripSeparator());
        }

        foreach (var server in favorites)
            _trayServersItem.DropDownItems.Add(CreateTrayServerItem(server, selectedId, selectable));

        if (_trayServersItem.DropDownItems.Count == 0)
            _trayServersItem.DropDownItems.Add(new ToolStripMenuItem(i18N.Translate("No favorite servers")) { Enabled = false });
    }

    private ToolStripMenuItem CreateTrayServerItem(ServerDto server, string? selectedId, bool selectable)
    {
        var item = new ToolStripMenuItem(server.Name)
        {
            Checked = server.Id == selectedId,
            Enabled = selectable
        };
        item.Click += (_, _) => SelectTrayServerAsync(server.Id).Forget();
        return item;
    }

    private async Task SelectTrayServerAsync(string id)
    {
        try
        {
            await _catalog.SelectServerAsync(id);
        }
        catch (AppException exception)
        {
            ShowTrayNotification(exception.Message, ToolTipIcon.Error);
        }
    }

    private async Task ToggleConnectionAsync()
    {
        try
        {
            var status = _connection.GetState().Status;
            if (status == "connected")
                await _connection.DisconnectAsync();
            else if (status is "connecting" or "reconnecting")
                await _connection.CancelConnectAsync();
            else if (status == "disconnecting")
                return;
            else
                await ConnectSelectedAsync();
        }
        catch (AppException exception)
        {
            ShowTrayNotification(exception.Message, ToolTipIcon.Error);
        }
    }

    private async Task ConnectSelectedAsync()
    {
        var serverId = _catalog.GetSelectedServerId();
        var modeId = _catalog.GetSelectedModeId();
        if (serverId is null || modeId is null)
            throw new AppException("SELECTION_REQUIRED", "Select a server and a mode before connecting.");

        await _connection.ConnectAsync(serverId, modeId);
    }

    private async Task RefreshSubscriptionsOnStartupAsync()
    {
        try
        {
            await _app.Subscriptions.RefreshAllAsync();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Subscription refresh failed");
        }
    }

    private async Task CheckUpdatesOnStartupAsync()
    {
        try
        {
            await _app.Updates.CheckAsync(Global.Settings.CheckBetaUpdate);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Startup update check failed");
        }
    }

    private async Task ToggleCloseToTrayAsync()
    {
        try
        {
            await _app.Settings.SetCloseToTrayAsync(Global.Settings.ExitWhenClosed);
        }
        catch (AppException exception)
        {
            ShowTrayNotification(exception.Message, ToolTipIcon.Error);
        }
    }

    private void OnAppEvent(object? sender, AppEventMessage message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnAppEvent(sender, message));
            return;
        }

        if (message.Name == "connection.stateChanged" && message.Payload is ConnectionStateDto state)
        {
            if (_trayConnectionItem is not null)
            {
                _trayConnectionItem.Text = state.Status switch
                {
                    "connected" => i18N.Translate("Disconnect"),
                    "connecting" or "reconnecting" => i18N.Translate("Cancel connection"),
                    "disconnecting" => i18N.Translate("Disconnecting..."),
                    _ => i18N.Translate("Connect")
                };
                _trayConnectionItem.Enabled = state.Status != "disconnecting";
            }
            if (_traySubscriptionsItem is not null)
                _traySubscriptionsItem.Enabled = state.Status is "disconnected" or "error";
            _trayIcon.Text = state.Status == "connected" ? "Netch — connected" : "Netch";
            RefreshTrayServersMenu();
        }

        else if (message.Name == "settings.changed" && message.Payload is SettingsDocumentDto document)
        {
            if (_trayCloseToTrayItem is not null)
                _trayCloseToTrayItem.Checked = document.Settings.Startup.CloseToTray;
        }

        else if (message.Name is "servers.changed" or "selection.changed")
        {
            RefreshTrayServersMenu();
            if (message.Name == "servers.changed")
                _ = _app.Countries.WarmAsync();
        }

        if (message.Name != "notification")
            return;

        var json = System.Text.Json.JsonSerializer.SerializeToElement(message.Payload);
        var level = json.TryGetProperty("level", out var levelElement) ? levelElement.GetString() : "info";
        var text = json.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
        if (!string.IsNullOrWhiteSpace(text))
            ShowTrayNotification(text, level == "error" ? ToolTipIcon.Error : ToolTipIcon.Info);
    }

    private void ShowTrayNotification(string text, ToolTipIcon icon)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowTrayNotification(text, icon));
            return;
        }

        _trayIcon.ShowBalloonTip(3500, "Netch", text, icon);
    }

    private void ShowWebViewFailure(string details)
    {
        Controls.Clear();
        Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            BackColor = BackColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(48),
            Text = $"Netch WebUI could not start.\n\n{details}"
        });
    }

    public void BeginDrag()
    {
        if (WindowState == FormWindowState.Maximized)
            return;

        ReleaseCapture();
        SendMessage(Handle, WmNcLeftButtonDown, HtCaption, 0);
    }

    public void Minimize()
    {
        WindowState = FormWindowState.Minimized;
    }

    public void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    public void CloseWindow()
    {
        BeginInvoke(Close);
    }

    public void ShowWindow()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ShowWindow);
            return;
        }

        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    public Task<string?> PickExecutableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "Select application",
            Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true,
            CheckPathExists = true,
            DereferenceLinks = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        return Task.FromResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null);
    }

    public Task<bool> ConfirmDangerousActionAsync(string title, string message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = MessageBox.Show(this, message, title, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        return Task.FromResult(result == DialogResult.OK);
    }

    public void RequestRestart()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RequestRestart);
            return;
        }

        _restartTimer ??= new System.Windows.Forms.Timer { Interval = 350 };
        _restartTimer.Tick -= RestartTimerOnTick;
        _restartTimer.Tick += RestartTimerOnTick;
        _restartTimer.Start();
    }

    private void RestartTimerOnTick(object? sender, EventArgs eventArgs)
    {
        _restartTimer?.Stop();
        try
        {
            _exitRequested = true;
            Program.SingleInstance.Dispose();
            Process.Start(new ProcessStartInfo(Global.NetchExecutable) { UseShellExecute = true });
            Close();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Could not restart Netch after update");
            ShowTrayNotification("Netch was updated but could not restart automatically.", ToolTipIcon.Error);
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (_shutdownComplete)
        {
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        if (e.CloseReason is CloseReason.WindowsShutDown or CloseReason.ApplicationExitCall or CloseReason.TaskManagerClosing)
            _exitRequested = true;
        if (!_exitRequested && !Global.Settings.ExitWhenClosed)
        {
            Hide();
            return;
        }

        if (_shutdownStarted)
            return;

        _shutdownStarted = true;
        try
        {
            if (Global.Settings.StopWhenExited || _connection.IsConnected)
                await _connection.DisconnectAsync();
            await Configuration.SaveAsync();
        }
        finally
        {
            AppEvents.Published -= OnAppEvent;
            _bridge?.Dispose();
            await _app.Updates.DisposeAsync();
            _app.Countries.Dispose();
            _connection.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _shutdownComplete = true;
            BeginInvoke(Close);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == HtClient)
            {
                var point = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xffff), (short)((m.LParam.ToInt64() >> 16) & 0xffff)));
                const int grip = 7;
                var left = point.X <= grip;
                var right = point.X >= ClientSize.Width - grip;
                var top = point.Y <= grip;
                var bottom = point.Y >= ClientSize.Height - grip;
                m.Result = (nint)(top && left ? HtTopLeft : top && right ? HtTopRight : bottom && left ? HtBottomLeft :
                    bottom && right ? HtBottomRight : left ? HtLeft : right ? HtRight : top ? HtTop : bottom ? HtBottom : HtClient);
            }
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppEvents.Published -= OnAppEvent;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _bridge?.Dispose();
            _webView.Dispose();
            _restartTimer?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        HandlePowerModeChangedAsync(e.Mode).Forget();
    }

    private async Task HandlePowerModeChangedAsync(PowerModes mode)
    {
        try
        {
            if (mode == PowerModes.Suspend && _connection.IsConnected)
            {
                _resumeConnection = true;
                await _connection.DisconnectAsync();
            }
            else if (mode == PowerModes.Resume && _resumeConnection)
            {
                _resumeConnection = false;
                await ConnectSelectedAsync();
            }
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Power transition connection handling failed");
        }
    }

    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, int wordParameter, int longParameter);
}
