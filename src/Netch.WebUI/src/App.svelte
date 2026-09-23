<script lang="ts">
  import { t, language as uiLanguage, locale as uiLocale } from "./lib/i18n";
  import { onMount } from 'svelte';
  import Icon from './components/ui/Icon.svelte';
  import CountryFlag from './components/ui/CountryFlag.svelte';
  import { serverLabel } from './lib/serverLabel';
  import { bridge, BridgeError, netch } from './lib/bridge';
  import type { ConnectionState, DiagnosticsSnapshot, LogEntry, Mode, ModeDetails, ModeWrite, ProcessInfo, QuickProfile, Routing, Server, ServerDetailsWrite, ServerEditor, ServerEditorDocument, SettingsData, SettingsDocument, Subscription, SubscriptionInput, TrafficSnapshot, UpdateState } from './lib/types';
  import NavigationRail from './components/NavigationRail.svelte';
  import HelpModal from './components/HelpModal.svelte';
  import { dialogFocus } from './lib/dialogFocus';
  import TitleBar from './components/TitleBar.svelte';
  import ServerSidebar from './components/ServerSidebar.svelte';
  import ConnectionPanel from './components/ConnectionPanel.svelte';
  import LogPanel from './components/LogPanel.svelte';
  import SubscriptionsModal from './components/SubscriptionsModal.svelte';
  import ProcessModal from './components/ProcessModal.svelte';
  import SettingsModal from './components/SettingsModal.svelte';
  import ProfilesModal from './components/ProfilesModal.svelte';
  import ServerImportModal from './components/ServerImportModal.svelte';
  import ProtocolServerModal from './components/ProtocolServerModal.svelte';
  import ConfirmDialog from './components/ConfirmDialog.svelte';
  import DiagnosticsModal from './components/DiagnosticsModal.svelte';
  import UpdateModal from './components/UpdateModal.svelte';
  import AdvancedModesModal from './components/AdvancedModesModal.svelte';
  // Vite removes the toolbar and demo bridge from production builds.
  const devToolbar = import.meta.env.DEV ? import('./components/DevToolbar.svelte') : null;

  let helpOpen = $state(false);
  let loading = $state(true);
  let busy = $state(false);
  let connectionBusy = $state(false);
  let connectionCancelling = $state(false);
  let error = $state<string | null>(null);
  let servers = $state<Server[]>([]);
  let modes = $state<Mode[]>([]);
  let profiles = $state<QuickProfile[]>([]);
  let subscriptions = $state<Subscription[]>([]);
  let logs = $state<LogEntry[]>([]);
  let connection = $state<ConnectionState>({ status: 'disconnected', message: 'Ready', serverId: null, modeId: null, connectedAt: null });
  let routing = $state<Routing>({ mode: 'all', processes: [] });
  let selectedServerId = $state<string | null>(null);
  let selectedModeId = $state<string | null>(null);
  let applicationsRequested = $state(false);
  let applicationsPending = $derived(applicationsRequested && selectedModeId === null);
  let traffic = $state<TrafficSnapshot>({ receivedBytes: 0, sentBytes: 0, received: '0 B', sent: '0 B', durationSeconds: 0, connectedAt: null });
  let subscriptionsOpen = $state(false);
  let processesOpen = $state(false);
  let settingsOpen = $state(false);
  let profilesOpen = $state(false);
  let profileInitialSlot = $state<number | null>(null);
  let profilesBusy = $state(false);
  let serverImportOpen = $state(false);
  let serverEditorOpen = $state(false);
  let serverEditorTarget = $state<ServerEditor | null>(null);
  let serverEditorDocument = $state<ServerEditorDocument | null>(null);
  let serverDeleteTarget = $state<Server | null>(null);
  let serverBusy = $state(false);
  let pingingIds = $state<string[]>([]);
  let pingAllBusy = false;
  let settingsLoading = $state(false);
  let settingsError = $state<string | null>(null);
  let logsOpen = $state(false);
  let settingsSaving = $state(false);
  let settingsDocument = $state<SettingsDocument | null>(null);
  let processes = $state<ProcessInfo[]>([]);
  let processesLoading = $state(false);
  let routingSaving = $state(false);
  let diagnosticsOpen = $state(false);
  let diagnosticsLoading = $state(false);
  let diagnostics = $state<DiagnosticsSnapshot | null>(null);
  let busyDriver = $state<string | null>(null);
  let updatesOpen = $state(false);
  let updateBusy = $state(false);
  let updateState = $state<UpdateState>({ status: 'idle', currentVersion: '', latestVersion: null, releaseNotes: null, releaseUrl: null, progress: 0, error: null, canDownload: false, canApply: false });
  let modesOpen = $state(false);
  let modeDetails = $state<ModeDetails[]>([]);
  let modeBusy = $state(false);
  let toast = $state<{ tone: string; message: string } | null>(null);
  let toastTimer: ReturnType<typeof setTimeout> | undefined;

  let modalOpen = $derived(helpOpen || subscriptionsOpen || processesOpen || settingsOpen || profilesOpen || serverImportOpen || serverEditorOpen || Boolean(serverDeleteTarget) || diagnosticsOpen || updatesOpen || modesOpen);

  let selectedServer = $derived(servers.find((server) => server.id === selectedServerId));
  let connectionAttempting = $derived(connection.status === 'connecting' || connection.status === 'reconnecting');
  let inTransition = $derived(connectionAttempting || connection.status === 'disconnecting');
  let canConnect = $derived(Boolean(selectedServerId && selectedModeId && !loading && !inTransition && !connectionBusy));
  let canToggle = $derived(connectionAttempting || connection.status === 'connected' || canConnect);
  let updateAvailable = $derived(updateState.status === 'available' || updateState.status === 'ready');
  let canModifyServers = $derived(connection.status === 'disconnected' || connection.status === 'error');
  let localServerGroups = $derived(Array.from(new Set(servers.filter((server) => !server.managedBySubscription).map((server) => server.group))).sort());

  $effect(() => { document.documentElement.lang = $uiLocale; });

  onMount(() => {
    const unsubscribe = [
      bridge.on<ConnectionState>('connection.stateChanged', (value) => connection = value),
      bridge.on<{ message: string }>('connection.statusChanged', (value) => connection = { ...connection, message: value.message }),
      bridge.on<LogEntry>('logs.added', (entry) => logs = [...logs.slice(-399), entry]),
      bridge.on('logs.cleared', () => logs = []),
      bridge.on('servers.changed', () => { void refreshServers(); void refreshProfiles(); }),
      bridge.on<{ serverId: string; latency: number | null; latencyMethod?: Server['latencyMethod'] }>('servers.latencyChanged', (value) => {
        pingingIds = pingingIds.filter(id => id !== value.serverId);
        servers = servers.map((server) => server.id === value.serverId ? { ...server, latency: value.latency ?? null, latencyMethod: value.latencyMethod } : server);
      }),
      bridge.on<{ hostname: string; countryCode: string }>('servers.countryChanged', (value) => {
        servers = servers.map((server) => server.hostname.toLowerCase() === value.hostname.toLowerCase() ? { ...server, countryCode: value.countryCode } : server);
      }),
      bridge.on<Subscription[]>('subscriptions.changed', (value) => subscriptions = value),
      bridge.on('modes.changed', () => { void refreshModes(); void refreshProfiles(); }),
      bridge.on<Routing>('routing.changed', (value) => routing = value),
      bridge.on<QuickProfile[]>('profiles.changed', (value) => profiles = value),
      bridge.on<{ serverId: string | null; modeId: string | null }>('selection.changed', (value) => {
        selectedServerId = value.serverId;
        selectedModeId = value.modeId;
      }),
      bridge.on<SettingsDocument>('settings.changed', (value) => { settingsDocument = value; uiLanguage.set(value.settings.general.language); void refreshProfiles(); }),
      bridge.on<TrafficSnapshot>('traffic.updated', (value) => traffic = value),
      bridge.on<DiagnosticsSnapshot>('diagnostics.changed', (value) => diagnostics = value),
      bridge.on<UpdateState>('updates.changed', (value) => updateState = value),
      bridge.on<{ level: string; message: string }>('notification', (value) => showToast(value.message, value.level))
    ];
    void initialize();
    return () => { unsubscribe.forEach((stop) => stop()); clearTimeout(toastTimer); };
  });

  async function initialize() {
    loading = true;
    error = null;
    try {
      const data = await netch.bootstrap();
      uiLanguage.set(data.language ?? 'System');
      servers = data.servers;
      modes = data.modes;
      profiles = data.profiles;
      subscriptions = data.subscriptions;
      connection = data.connection;
      routing = data.routing;
      logs = data.logs;
      selectedServerId = data.selectedServerId;
      selectedModeId = data.selectedModeId;
      if (servers.length) void pingAll(true);
      void netch.diagnostics.get().then((value) => diagnostics = value).catch(() => undefined);
    } catch (cause) {
      error = messageOf(cause);
    } finally {
      loading = false;
    }
  }

  async function selectServer(id: string) {
    const previous = selectedServerId;
    selectedServerId = id;
    try { await netch.servers.select(id); } catch (cause) { selectedServerId = previous; showToast(messageOf(cause), 'error'); }
  }

  async function selectMode(id: string | null) {
    if (id === null) {
      // No saved app profile yet: show its controls until the user saves a list.
      applicationsRequested = true;
      selectedModeId = null;
      return;
    }
    const previous = selectedModeId;
    selectedModeId = id;
    try { await netch.modes.select(id); } catch (cause) { selectedModeId = previous; showToast(messageOf(cause), 'error'); }
  }

  async function toggleConnection() {
    if (!canToggle) return;
    if (connectionAttempting) {
      connectionCancelling = true;
      try { connection = await netch.connection.cancel(); }
      catch (cause) { showToast(messageOf(cause), 'error'); }
      finally { connectionCancelling = false; }
      return;
    }

    connectionBusy = true;
    try {
      connection = connection.status === 'connected'
        ? await netch.connection.disconnect()
        : await netch.connection.connect(selectedServerId!, selectedModeId!);
    } catch (cause) {
      showToast(messageOf(cause), 'error');
    } finally {
      connectionBusy = false;
    }
  }

  async function reconnect() {
    if (applicationsPending) return;
    connectionBusy = true;
    try { connection = await netch.connection.reconnect(); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { connectionBusy = false; }
  }

  async function refreshServers() {
    const known = new Set(servers.map(server => `${server.id}:${server.hostname}:${server.port}`));
    servers = await netch.servers.list();
    if (servers.some(server => !known.has(`${server.id}:${server.hostname}:${server.port}`))) void pingAll(true);
  }

  async function refreshModes() {
    modes = await netch.modes.list();
  }

  async function openModes() {
    modesOpen = true;
    try {
      const catalog = await netch.modes.details();
      modes = catalog.modes;
      modeDetails = catalog.details;
      selectedModeId = catalog.selectedModeId;
    } catch (cause) { modesOpen = false; showToast(messageOf(cause), 'error'); }
  }

  async function saveMode(value: ModeWrite) {
    modeBusy = true;
    try {
      const catalog = await netch.modes.save(value);
      modes = catalog.modes;
      modeDetails = catalog.details;
      selectedModeId = catalog.selectedModeId;
      await refreshProfiles();
      showToast(value.id ? $t("Mode saved") : $t("Mode created"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { modeBusy = false; }
  }

  async function deleteMode(id: string) {
    modeBusy = true;
    try {
      const catalog = await netch.modes.delete(id);
      modes = catalog.modes;
      modeDetails = catalog.details;
      selectedModeId = catalog.selectedModeId;
      await refreshProfiles();
      showToast($t("Custom mode deleted"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { modeBusy = false; }
  }

  async function selectManagedMode(id: string) {
    try { await selectMode(id); selectedModeId = id; }
    catch { return; }
  }

  async function refreshProfiles() {
    try { profiles = await netch.profiles.list(); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
  }

  function openProfiles(slot?: number) {
    profileInitialSlot = slot ?? null;
    profilesOpen = true;
  }

  async function activateProfile(slot: number) {
    profilesBusy = true;
    try {
      const selection = await netch.profiles.activate(slot);
      selectedServerId = selection.serverId;
      selectedModeId = selection.modeId;
      showToast($t("Profile {0} activated", slot + 1), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { profilesBusy = false; }
  }

  async function saveProfile(slot: number, name: string) {
    if (!selectedServerId || !selectedModeId) return;
    profilesBusy = true;
    try {
      profiles = await netch.profiles.save(slot, name, selectedServerId, selectedModeId);
      showToast($t("Quick profile saved"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { profilesBusy = false; }
  }

  async function deleteProfile(slot: number) {
    profilesBusy = true;
    try {
      profiles = await netch.profiles.delete(slot);
      showToast($t("Quick profile removed"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { profilesBusy = false; }
  }

  async function pingAll(silent = false) {
    if (pingAllBusy || pingingIds.length) return;
    pingAllBusy = true;
    pingingIds = servers.map(server => server.id);
    if (!silent) showToast($t("Testing server latency…"), 'info');
    try { servers = await netch.servers.ping(); } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { pingingIds = []; pingAllBusy = false; }
  }

  async function pingServer(id: string) {
    if (pingingIds.includes(id)) return;
    pingingIds = [...pingingIds, id];
    try { servers = await netch.servers.ping([id]); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { pingingIds = pingingIds.filter(value => value !== id); }
  }

  async function importServers(text: string, group: string) {
    serverBusy = true;
    try {
      const catalog = await netch.servers.import(text, group);
      servers = catalog.servers;
      selectedServerId = catalog.selectedServerId;
      serverImportOpen = false;
      await refreshProfiles();
      showToast($t("{0} server{1} imported", catalog.importedCount, catalog.importedCount === 1 ? '' : 's'), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { serverBusy = false; }
  }

  async function openServerEditor(id?: string) {
    serverBusy = true;
    try {
      serverEditorDocument = await netch.servers.details();
      serverEditorTarget = id ? serverEditorDocument.servers.find((server) => server.id === id) ?? null : null;
      if (id && !serverEditorTarget) throw new Error($t("The selected server no longer exists."));
      serverEditorOpen = true;
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { serverBusy = false; }
  }

  async function saveServerDetails(value: ServerDetailsWrite) {
    serverBusy = true;
    try {
      const catalog = await netch.servers.saveDetails(value);
      servers = catalog.servers;
      selectedServerId = catalog.selectedServerId;
      serverEditorOpen = false;
      serverEditorTarget = null;
      await refreshProfiles();
      showToast(value.id ? $t("Server details saved") : $t("Server added"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { serverBusy = false; }
  }

  async function deleteServer() {
    if (!serverDeleteTarget) return;
    serverBusy = true;
    try {
      const catalog = await netch.servers.delete(serverDeleteTarget.id);
      servers = catalog.servers;
      selectedServerId = catalog.selectedServerId;
      serverDeleteTarget = null;
      await refreshProfiles();
      showToast($t("Server deleted"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { serverBusy = false; }
  }

  async function setServerFavorite(server: Server) {
    serverBusy = true;
    try {
      const catalog = await netch.servers.setFavorite(server.id, !server.isFavorite);
      servers = catalog.servers;
      selectedServerId = catalog.selectedServerId;
      showToast(server.isFavorite ? $t("Removed from favorites") : $t("Added to favorites"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { serverBusy = false; }
  }

  async function refreshAllSubscriptions() {
    busy = true;
    try {
      const summary = await netch.subscriptions.refreshAll();
      await refreshServers();
      subscriptions = await netch.subscriptions.list();
      showToast(refreshSummaryMessage(summary.serverCount, summary.failedSubscriptions), summary.failedSubscriptions ? 'error' : 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { busy = false; }
  }

  async function saveSubscription(value: SubscriptionInput) {
    busy = true;
    try {
      subscriptions = await netch.subscriptions.save(value);
      if (!value.id && value.enabled) {
        const created = subscriptions.find(item => item.remark === value.remark.trim());
        if (created) {
          showToast($t('Subscription saved; downloading servers…'));
          const count = await netch.subscriptions.refresh(created.id);
          subscriptions = await netch.subscriptions.list();
          await refreshServers();
          showToast($t('{0} servers updated', count), 'success');
        }
      } else showToast($t("Subscription saved"), 'success');
      subscriptionsOpen = false;
    }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { busy = false; }
  }

  async function deleteSubscription(id: string) {
    busy = true;
    try { subscriptions = await netch.subscriptions.delete(id); await refreshServers(); showToast($t("Subscription removed"), 'success'); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { busy = false; }
  }

  async function refreshSubscription(id: string) {
    busy = true;
    try { const count = await netch.subscriptions.refresh(id); await refreshServers(); showToast($t("{0} servers updated", count), 'success'); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { busy = false; }
  }

  async function openProcesses() {
    processesOpen = true;
    await refreshProcesses();
  }

  async function refreshProcesses() {
    processesLoading = true;
    try { processes = await netch.processes.list(); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { processesLoading = false; }
  }

  async function pickExecutable() {
    try { return await netch.processes.pickExecutable(); }
    catch (cause) { showToast(messageOf(cause), 'error'); return null; }
  }

  async function saveProcesses(values: string[]) {
    routingSaving = true;
    try {
      const result = await netch.routing.setProcesses(values);
      routing = result.routing;
      selectedModeId = result.modeId;
      await refreshModes();
      processesOpen = false;
      showToast($t("Process routing updated"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { routingSaving = false; }
  }

  async function openSettings() {
    if (settingsLoading) return;
    settingsOpen = true;
    settingsLoading = true;
    settingsError = null;
    try { settingsDocument = await netch.settings.get(); }
    catch (cause) { settingsError = messageOf(cause); }
    finally { settingsLoading = false; }
  }

  async function saveSettings(settings: SettingsData) {
    settingsSaving = true;
    try {
      const previousLanguage = settingsDocument?.settings.general.language;
      settingsDocument = await netch.settings.update(settings);
      uiLanguage.set(settingsDocument.settings.general.language);
      settingsOpen = false;
      showToast(previousLanguage !== settings.general.language ? $t("Settings saved. Restart Netch to apply the language.") : $t("Settings saved"), 'success');
    } catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { settingsSaving = false; }
  }

  function manageSubscriptionsFromSettings() {
    settingsOpen = false;
    subscriptionsOpen = true;
  }

  async function openDiagnostics() {
    diagnosticsOpen = true;
    await refreshDiagnostics();
  }

  async function refreshDiagnostics() {
    diagnosticsLoading = true;
    try { diagnostics = await netch.diagnostics.get(); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { diagnosticsLoading = false; }
  }

  async function installDriver(driverId: string) {
    busyDriver = driverId;
    try { diagnostics = await netch.diagnostics.installDriver(driverId); }
    catch (cause) {
      if (!(cause instanceof BridgeError) || cause.rpc.code !== 'OPERATION_CANCELLED') showToast(messageOf(cause), 'error');
    } finally { busyDriver = null; }
  }

  async function openUpdates() {
    updatesOpen = true;
    try { updateState = await netch.updates.getState(); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
  }

  async function checkUpdates(includePrerelease: boolean) {
    updateBusy = true;
    try { updateState = await netch.updates.check(includePrerelease); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { updateBusy = false; }
  }

  async function downloadUpdate() {
    updateBusy = true;
    try { updateState = await netch.updates.download(); }
    catch (cause) { showToast(messageOf(cause), 'error'); }
    finally { updateBusy = false; }
  }

  async function applyUpdate() {
    updateBusy = true;
    try { updateState = await netch.updates.apply(); }
    catch (cause) {
      if (!(cause instanceof BridgeError) || cause.rpc.code !== 'OPERATION_CANCELLED') showToast(messageOf(cause), 'error');
      updateBusy = false;
    }
  }

  async function clearLogs() {
    if (await netch.logs.clear()) logs = [];
  }

  async function copyLogs() {
    const value = logs.map((entry) => `${entry.timestamp} ${entry.level.padEnd(7)} ${entry.message}`).join('\n');
    await navigator.clipboard.writeText(value);
    showToast($t("Logs copied"), 'success');
  }

  function showToast(message: string, tone = 'info') {
    toast = { message, tone };
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast = null, 4200);
  }

  function messageOf(cause: unknown) {
    return cause instanceof BridgeError ? cause.rpc.message : cause instanceof Error ? cause.message : $t("Unexpected error");
  }

  function openDemoScreen(screen: string) {
    helpOpen = false;
  subscriptionsOpen = processesOpen = settingsOpen = profilesOpen = serverImportOpen = diagnosticsOpen = updatesOpen = modesOpen = false;
    serverEditorOpen = false;
    switch (screen) {
      case 'settings': void openSettings(); break;
      case 'subscriptions': subscriptionsOpen = true; break;
      case 'processes': void openProcesses(); break;
      case 'import': serverImportOpen = true; break;
      case 'server': void openServerEditor(); break;
      case 'profiles': openProfiles(); break;
      case 'modes': void openModes(); break;
      case 'diagnostics': void openDiagnostics(); break;
      case 'updates': void openUpdates(); break;
      case 'logs': logsOpen = true; break;
    }
  }

  function refreshSummaryMessage(serverCount: number, failures: number) {
    return failures
      ? $t("{0} servers updated; {1} subscription{2} failed", serverCount, failures, failures === 1 ? '' : 's')
      : $t("{0} servers updated", serverCount);
  }
</script>

<svelte:window onkeydown={(event) => {
  if (event.key !== 'Escape') return;
  helpOpen = false;
  subscriptionsOpen = processesOpen = settingsOpen = profilesOpen = serverImportOpen = diagnosticsOpen = updatesOpen = modesOpen = false;
  serverEditorOpen = false;
  serverEditorTarget = null;
  serverDeleteTarget = null;
}} />

<div class="app-shell" inert={modalOpen} aria-hidden={modalOpen} class:demo-shell={Boolean(devToolbar && bridge.demo)}>
  {#if devToolbar && bridge.demo}
    {#await devToolbar then module}<module.default onScreen={openDemoScreen} onReset={initialize} />{/await}
  {/if}
  <TitleBar status={connection.status} />
  <main class="workspace">
    <NavigationRail onSubscriptions={() => subscriptionsOpen = true} onProfiles={() => openProfiles()} onSettings={openSettings} onDiagnostics={openDiagnostics} onUpdates={openUpdates} onHelp={() => helpOpen = true} {updateAvailable} version={diagnostics?.appVersion} />
    <ServerSidebar onSubscriptions={() => subscriptionsOpen = true} {servers} {pingingIds} selectedId={selectedServerId} {loading} disabled={!canModifyServers || inTransition || serverBusy} mutationDisabled={!canModifyServers || serverBusy || busy} favoriteDisabled={serverBusy || busy} onSelect={selectServer} onPingAll={pingAll} onPingOne={pingServer} onFavorite={setServerFavorite} onCreate={() => openServerEditor()} onImport={() => serverImportOpen = true} onEdit={(server) => openServerEditor(server.id)} onDelete={(server) => serverDeleteTarget = server} />
    <div class="main-pane" id="connection">
      {#if loading}
        <div class="loading-screen"><Icon name="loader" class="spin" size={24} /><span>{$t("Loading Netch configuration")}</span></div>
      {:else if error}
        <div class="fatal-state"><Icon name="alert" size={24} /><h2>{$t("Desktop service unavailable")}</h2><p>{error}</p><button class="button secondary" onclick={initialize}>{$t("Retry")}</button></div>
      {:else}
        <div class="content-grid">
          <div class="page-heading"><div><span class="eyebrow">{$t('Make yourself at home')}</span><h2>{$t('Your connection')}</h2></div><button class="page-help" title={$t('Getting started')} aria-label={$t('Getting started')} onclick={() => helpOpen = true}>?</button></div>
          <ConnectionPanel {canToggle} {connectionBusy} {connectionCancelling} onToggle={toggleConnection} onSubscriptions={() => subscriptionsOpen = true} onHelp={() => helpOpen = true} {connection} server={selectedServer} {modes} {selectedModeId} {applicationsPending} {routing} {traffic} {profiles} {diagnostics} disabled={!canModifyServers || inTransition || profilesBusy} routingLocked={!canModifyServers} onModeSelect={selectMode} onProcesses={openProcesses} onModesManage={openModes} onDiagnostics={openDiagnostics} onReconnect={reconnect} onProfileActivate={activateProfile} onProfilesManage={openProfiles} />
          <details class="logs-disclosure" bind:open={logsOpen}>
            <summary><Icon name="terminal" size={18} /> {$t("Connection log")} <span>{logs.length} {$t("entries")}</span><Icon name="chevron-down" size={16} /></summary>
            {#if logsOpen}<LogPanel {logs} onClear={clearLogs} onCopy={copyLogs} />{/if}
          </details>
        </div>
        <footer class="connect-dock">
          <div class="connect-context">
            {#if selectedServer}<span class="context-mark"><CountryFlag code={selectedServer.countryCode} automatic={selectedServer.isAutomatic} /></span><span><small>{$t("Selected endpoint")}</small><strong>{serverLabel(selectedServer)}</strong></span>
            {:else}<span class="context-mark muted"><Icon name="server-off" size={16} /></span><span><small>{$t("Endpoint required")}</small><strong>{$t("Select a server")}</strong></span>{/if}
          </div>
<span class="dock-note"><Icon name="shield" size={16} />{$t(connection.status === 'connected' ? 'Connection active' : 'Ready when you are')}</span>
        </footer>
      {/if}
    </div>
  </main>
</div>

{#if helpOpen}<HelpModal onClose={() => helpOpen = false} onSubscriptions={() => { helpOpen = false; subscriptionsOpen = true; }} />{/if}
{#if subscriptionsOpen}
  <SubscriptionsModal {subscriptions} {busy} mutationsDisabled={!canModifyServers} onClose={() => subscriptionsOpen = false} onSave={saveSubscription} onDelete={deleteSubscription} onRefresh={refreshSubscription} onRefreshAll={refreshAllSubscriptions} />
{/if}
{#if processesOpen}
  <ProcessModal {processes} selected={routing.processes} loading={processesLoading} saving={routingSaving} onClose={() => processesOpen = false} onSave={saveProcesses} onRefresh={refreshProcesses} onPick={pickExecutable} />
{/if}
{#if settingsOpen}
  {#if settingsLoading || settingsError || !settingsDocument}
    <div class="modal-backdrop">
      <div use:dialogFocus class="modal settings-status" role="dialog" aria-modal="true" aria-labelledby="settings-status-title">
        <header class="modal-header"><h2 id="settings-status-title">{$t("Settings")}</h2><button class="icon-button" aria-label={$t("Close settings")} onclick={() => settingsOpen = false}><Icon name="x" size={18} /></button></header>
        <div class="settings-loading" role="status">
          {#if settingsError}<Icon name="alert" size={24} /><p>{settingsError}</p><button class="button secondary" onclick={openSettings}>{$t("Try again")}</button>
          {:else}<Icon name="loader" class="spin" size={24} /><span>{$t("Loading settings…")}</span>{/if}
        </div>
      </div>
    </div>
  {:else}
    <SettingsModal document={settingsDocument} saving={settingsSaving} canSave={connection.status === 'disconnected' || connection.status === 'error'} onClose={() => settingsOpen = false} onSave={saveSettings} onManageSubscriptions={manageSubscriptionsFromSettings} />
  {/if}
{/if}
{#if profilesOpen}
  <ProfilesModal {profiles} {selectedServerId} {selectedModeId} busy={profilesBusy} initialSlot={profileInitialSlot} onClose={() => profilesOpen = false} onSave={saveProfile} onDelete={deleteProfile} onActivate={activateProfile} />
{/if}
{#if serverImportOpen}
  <ServerImportModal groups={localServerGroups} busy={serverBusy} onClose={() => serverImportOpen = false} onImport={importServers} />
{/if}
{#if serverEditorOpen && serverEditorDocument}
  <ProtocolServerModal server={serverEditorTarget} protocols={serverEditorDocument.protocols} options={serverEditorDocument.options} groups={localServerGroups} busy={serverBusy} onClose={() => serverEditorOpen = false} onSave={saveServerDetails} />
{/if}
{#if serverDeleteTarget}
  <ConfirmDialog title={$t("Delete {0}?", serverDeleteTarget.name)} message="The server is removed from this device. Quick profiles that reference it will be marked unavailable." busy={serverBusy} onClose={() => serverDeleteTarget = null} onConfirm={deleteServer} />
{/if}
{#if diagnosticsOpen}
  <DiagnosticsModal snapshot={diagnostics} loading={diagnosticsLoading} {busyDriver} onClose={() => diagnosticsOpen = false} onRefresh={refreshDiagnostics} onRepair={installDriver} />
{/if}
{#if updatesOpen}
  <UpdateModal update={updateState} busy={updateBusy} onClose={() => updatesOpen = false} onCheck={checkUpdates} onDownload={downloadUpdate} onApply={applyUpdate} />
{/if}
{#if modesOpen}
  <AdvancedModesModal details={modeDetails} {selectedModeId} busy={modeBusy} canEdit={connection.status === 'disconnected' || connection.status === 'error'} onClose={() => modesOpen = false} onSave={saveMode} onDelete={deleteMode} onSelect={selectManagedMode} />
{/if}
{#if toast}
  <div class="toast {toast.tone}" role="status"><span>{$t(toast.message)}</span><button aria-label={$t("Dismiss")} onclick={() => toast = null}><Icon name="x" size={14} /></button></div>
{/if}
