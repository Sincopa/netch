<script lang="ts">
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import CountryFlag from './ui/CountryFlag.svelte';
  import { serverLabel } from '../lib/serverLabel';
  import QuickProfiles from './QuickProfiles.svelte';
  import type { ConnectionState, DiagnosticsSnapshot, Mode, QuickProfile, Routing, Server, TrafficSnapshot } from '../lib/types';

  let {
    canToggle, connectionBusy, connectionCancelling, onToggle, onSubscriptions, onHelp,
    connection,
    server,
    modes,
    selectedModeId,
    applicationsPending,
    routing,
    traffic,
    profiles,
    diagnostics,
    disabled,
    routingLocked,
    onModeSelect,
    onProcesses,
    onModesManage,
    onDiagnostics,
    onReconnect,
    onProfileActivate,
    onProfilesManage
  }: {
    canToggle: boolean; connectionBusy: boolean; connectionCancelling: boolean; onToggle: () => void; onSubscriptions: () => void; onHelp: () => void;
    connection: ConnectionState;
    server: Server | undefined;
    modes: Mode[];
    selectedModeId: string | null;
    applicationsPending: boolean;
    routing: Routing;
    traffic: TrafficSnapshot;
    profiles: QuickProfile[];
    diagnostics: DiagnosticsSnapshot | null;
    disabled: boolean;
    routingLocked: boolean;
    onModeSelect: (id: string | null) => void;
    onProcesses: () => void;
    onModesManage: () => void;
    onDiagnostics: () => void;
    onReconnect: () => void;
    onProfileActivate: (slot: number) => void;
    onProfilesManage: (slot?: number) => void;
  } = $props();

  let connected = $derived(connection.status === 'connected');
  let attempting = $derived(connection.status === 'connecting' || connection.status === 'reconnecting');
  let heroTitle = $derived(connected ? 'You’re connected.' : attempting ? 'Making the connection.' : connection.status === 'disconnecting' ? 'Disconnecting safely.' : connection.status === 'error' ? 'Let’s try that again.' : server ? 'Your internet. A little freer.' : 'Your first connection.');
  let heroDescription = $derived(connected ? 'Your selected traffic is now using this connection.' : attempting ? 'This usually takes a few seconds. You can cancel at any time.' : connection.status === 'disconnecting' ? 'Please wait while Netch restores your regular connection.' : connection.status === 'error' ? 'The connection didn’t go through. Try again or choose another server.' : server ? 'Your server is selected. One click and you’re on your way.' : 'Add a link from your VPN provider. We’ll take care of the server list.');
  let selectedMode = $derived(modes.find((mode) => mode.id === selectedModeId));
  let selectedKind = $derived(applicationsPending ? 'selected-applications' : selectedMode?.selectionRole);
  const kinds = [
    { value: 'whole-computer', title: 'Whole computer', subtitle: 'Browsers, games and all other apps', icon: 'globe', component: 'wintun' },
    { value: 'selected-applications', title: 'Choose apps', subtitle: 'Only the apps you select', icon: 'boxes', component: 'netfilter2' }
  ];
  let driverIssue = $derived(diagnostics?.components.find((component) =>
    (component.id === 'wintun' || component.id === 'netfilter2') && component.status !== 'ready') ?? null);

  function kindReady(component: string | null) {
    if (!component || !diagnostics) return true;
    return diagnostics.components.find((item) => item.id === component)?.status === 'ready';
  }

  function chooseKind(kind: string) {
    const mode = modes.find((item) => item.selectionRole === kind);
    if (mode) onModeSelect(mode.id);
    else if (kind === 'selected-applications') onModeSelect(null);
  }

  function duration(seconds: number) {
    const hours = Math.floor(seconds / 3600).toString().padStart(2, '0');
    const minutes = Math.floor((seconds % 3600) / 60).toString().padStart(2, '0');
    const remainder = Math.floor(seconds % 60).toString().padStart(2, '0');
    return `${hours}:${minutes}:${remainder}`;
  }

  function statusLabel(status: ConnectionState['status']) {
    return ({
      disconnected: 'Disconnected',
      connecting: 'Connecting',
      connected: 'Connected',
      disconnecting: 'Disconnecting',
      reconnecting: 'Reconnecting',
      error: 'Connection error'
    })[status];
  }
</script>

<section class="connection-stack">
  <div class="connection-hero" class:is-connected={connected} class:is-working={attempting} class:has-error={connection.status === 'error'}>
    <div class="hero-copy">
      <span class="hero-status" role="status"><i></i>{$t(statusLabel(connection.status))}</span>
      <h2>{$t(heroTitle)}</h2>
      <p>{$t(heroDescription)}</p>
      {#if server}
        <button class="connect-button" class:disconnect={connected} class:cancel={attempting} disabled={!canToggle || connectionCancelling || (connectionBusy && !attempting)} onclick={onToggle}>
          <Icon name={attempting || connection.status === 'disconnecting' || connectionCancelling ? 'loader' : 'power'} class={attempting || connection.status === 'disconnecting' || connectionCancelling ? 'spin' : ''} size={21} />
          {connected ? $t('Disconnect') : attempting ? connectionCancelling ? $t('Cancelling…') : $t('Cancel connection') : connection.status === 'disconnecting' ? $t('Disconnecting…') : connection.status === 'error' ? $t('Retry connection') : $t('Connect')}
        </button>
        {#if applicationsPending}<span class="hero-hint">{$t('Choose and save your apps below to continue.')}</span>{:else if !selectedModeId}<span class="hero-hint">{$t('Choose a connection mode below to continue.')}</span>{/if}
      {:else}
        <button class="connect-button" onclick={onSubscriptions}><Icon name="plus" size={20} />{$t('Add your subscription')}</button>
        <button class="hero-help" onclick={onHelp}>{$t('Where do I get a link?')} ↗</button>
      {/if}
    </div>
    <div class="connection-art" aria-hidden="true"><div class="orbit orbit-outer"></div><div class="orbit orbit-middle"></div><div class="orbit orbit-inner"></div><div class="orbit-core"><img src="/logo.svg" alt="" /></div><span class="orbit-satellite"><Icon name={connected ? 'check' : 'shield'} size={21} /></span><span class="orbit-spark"></span><span class="art-caption">NETCH / {$t('CONNECTION')}</span></div>
  </div>
  <div class="connection-overview">
    <span class="overview-icon">{#if server}<CountryFlag code={server.countryCode} automatic={server.isAutomatic} />{:else}<Icon name="globe" size={25} />{/if}</span>
    <div><small>{$t('Selected server')}</small><h1>{server ? serverLabel(server) : $t('Your connection starts here')}</h1><p>{server ? server.isAutomatic ? $t('Netch will choose a server for you') : server.group === 'NONE' ? $t('Local') : server.group : $t('Add a subscription or a server, then choose what to connect.')}</p></div>
    {#if server}<span class="overview-latency" title={$t('Lower latency means a quicker response')}><Icon name="activity" size={17} />{server.latency == null ? '—' : $t('{0} ms', server.latency)}</span>{/if}
  </div>
  <div class="routing-section-title"><span class="section-number">01</span><h2 class="routing-heading">{$t('What should use this connection?')}</h2></div>
  <div class="mode-grid">
    {#each kinds as kind}
      {@const available = (kind.value === 'selected-applications' || modes.some((mode) => mode.selectionRole === kind.value)) && kindReady(kind.component)}
      <button
        class:active={selectedKind === kind.value}
        class="mode-tile"
        aria-pressed={selectedKind === kind.value}
        title={!available ? $t("Open Diagnostics to check the required driver and mode.") : $t(kind.subtitle)}
        disabled={!available || disabled}
        onclick={() => chooseKind(kind.value)}
      >
        <Icon name={kind.icon} size={18} strokeWidth={1.7} />
        <span><strong>{$t(kind.title)}</strong><small>{$t(kind.subtitle)}</small></span>
        {#if selectedKind === kind.value}<span class="selected-mark"><Icon name="check" size={12} /></span>{/if}
      </button>
    {/each}
  </div>
  {#if driverIssue}
    <button class="driver-callout" onclick={onDiagnostics}><Icon name="alert" size={15} /><span><strong>{$t('A component needs your attention')}</strong><small>{driverIssue.summary}</small></span><em>{driverIssue.canRepair ? $t("Repair") : $t("Details")}</em></button>
  {/if}

  <div class="panel profile-panel">
    {#if selectedKind === 'selected-applications'}
      <button class="route-row" onclick={onProcesses} disabled={disabled || routingLocked}>
        <span class="route-icon"><Icon name="route" size={20} /></span>
        <span><strong>{$t("Applications")}</strong><small>{routingLocked ? $t("Disconnect to change apps") : $t("{0} apps selected", routing.processes.length)}</small></span>
        <span class="route-action">{$t("Choose apps")}</span>
      </button>
      <p class="routing-description">{$t('Your app selection is saved for this subscription, including when you change servers.')}</p>
    {:else}
      <p class="routing-description">{selectedMode?.selectionRole === 'whole-computer' ? $t("Use this mode to connect your whole computer. Local network devices remain accessible.") : $t("Advanced mode: {0}", selectedMode?.name ?? 'Select a routing profile below')}</p>
    {/if}
    <details class="advanced-routing">
      <summary><Icon name="sliders" size={16} /> {$t("Advanced routing & saved profiles")} <Icon name="chevron-down" size={16} /></summary>
      <div class="advanced-routing-body">
    <div class="section-title">
      <span><Icon name="sliders" size={15} /> {$t("Connection profile")}</span>
      <button class="text-button" onclick={onModesManage}><Icon name="edit" size={12} />{$t("Manage modes")}</button>
    </div>
    <label class="select-field">
      <select aria-label={$t("Connection profile")} value={selectedModeId ?? ''} onchange={(event) => onModeSelect(event.currentTarget.value)} disabled={disabled}>
        {#each modes as mode}<option value={mode.id}>{mode.name}</option>{/each}
      </select>
      <Icon name="chevron-down" size={15} />
    </label>

      <div class="profile-panel-divider"></div>
      <QuickProfiles {profiles} {disabled} onActivate={onProfileActivate} onManage={onProfilesManage} />
      </div>
    </details>
  </div>

  <div class="panel telemetry-panel">
    <div class="section-title"><span><span class="section-number">02</span>{$t("Session")}</span><small class:live={connection.status === 'connected'} class:error={connection.status === 'error'}>{$t(statusLabel(connection.status))}</small></div>
    <div class="telemetry-grid">
      
      <div><small><Icon name="clock" size={15} />{$t("Duration")}</small><strong>{duration(traffic.durationSeconds)}</strong></div>
      <div><small><Icon name="download" size={15} />{$t("Download")}</small><strong>↓ {traffic.received}</strong></div>
      <div><small><Icon name="upload" size={15} />{$t("Upload")}</small><strong>↑ {traffic.sent}</strong></div>
    </div>
    <div class="status-line">
      <i class:connected={connection.status === 'connected'} class:error={connection.status === 'error'}></i>
      <span>{$t(connection.message)}</span>
      {#if connection.status === 'connected' || connection.status === 'error'}
        <button class="text-button" onclick={onReconnect} disabled={applicationsPending}><Icon name="refresh" size={12} />{$t("Reconnect")}</button>
      {/if}
    </div>
  </div>
</section>
