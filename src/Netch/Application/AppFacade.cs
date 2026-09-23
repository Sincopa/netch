namespace Netch.Application;

public sealed class AppFacade
{
    public AppFacade(
        CatalogService catalog,
        ConnectionService connection,
        ServerService servers,
        ProfileService profiles,
        SubscriptionService subscriptions,
        ProcessService processes,
        ModeManagementService modes,
        SettingsService settings,
        DiagnosticsService diagnostics,
        DriverService drivers,
        UpdateService updates,
        ServerCountryService countries,
        LogBufferSink logs)
    {
        Catalog = catalog;
        Connection = connection;
        Servers = servers;
        Profiles = profiles;
        Subscriptions = subscriptions;
        Processes = processes;
        Modes = modes;
        Settings = settings;
        Diagnostics = diagnostics;
        Drivers = drivers;
        Updates = updates;
        Countries = countries;
        Logs = logs;
    }

    public CatalogService Catalog { get; }

    public ConnectionService Connection { get; }

    public ServerService Servers { get; }

    public ProfileService Profiles { get; }

    public SubscriptionService Subscriptions { get; }

    public ProcessService Processes { get; }

    public ModeManagementService Modes { get; }

    public SettingsService Settings { get; }

    public DiagnosticsService Diagnostics { get; }

    public DriverService Drivers { get; }

    public UpdateService Updates { get; }

    public ServerCountryService Countries { get; }

    public LogBufferSink Logs { get; }

    public BootstrapDto GetBootstrap()
    {
        return new BootstrapDto(
            Catalog.GetServers(),
            Catalog.GetModes(),
            Profiles.GetAll(),
            Subscriptions.GetAll(),
            Connection.GetState(),
            Processes.GetRouting(),
            Logs.GetEntries(),
            Catalog.GetSelectedServerId(),
            Catalog.GetSelectedModeId(),
            !Global.Settings.ExitWhenClosed,
            Global.Settings.Language);
    }
}
