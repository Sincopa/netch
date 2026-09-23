<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t, languageName } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import SettingToggle from './ui/SettingToggle.svelte';
  import type { SettingsData, SettingsDocument } from '../lib/types';

  let {
    document,
    saving,
    canSave,
    onClose,
    onSave,
    onManageSubscriptions
  }: {
    document: SettingsDocument;
    saving: boolean;
    canSave: boolean;
    onClose: () => void;
    onSave: (settings: SettingsData) => void;
    onManageSubscriptions: () => void;
  } = $props();

  const sections = [
    { id: 'general', label: 'General', icon: 'settings' },
    { id: 'connection', label: 'Connection', icon: 'plug' },
    { id: 'routing', label: 'Routing', icon: 'route' },
    { id: 'subscriptions', label: 'Subscriptions', icon: 'download' },
    { id: 'dns', label: 'DNS', icon: 'globe' },
    { id: 'startup', label: 'Startup', icon: 'power' },
    { id: 'updates', label: 'Updates', icon: 'refresh' },
    { id: 'advanced', label: 'Advanced', icon: 'sliders' }
  ] as const;

  type Section = typeof sections[number]['id'];
  let active = $state<Section>('general');
  let draft = $state<SettingsData | null>(null);
  let bypassText = $state('');

  $effect(() => {
    if (draft !== null) return;
    // The parent owns a deep Svelte state proxy; native structuredClone rejects it.
    draft = $state.snapshot(document.settings);
    draft.connection.latencyTestUrl ??= 'https://www.google.com/generate_204';
    bypassText = draft.dns.bypassIps.join('\n');
  });

  function submit() {
    if (!draft) return;
    draft.dns.bypassIps = bypassText.split(/\r?\n|,/).map((value) => value.trim()).filter(Boolean);
    onSave($state.snapshot(draft) as SettingsData);
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="modal settings-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="settings-title">
    <header class="modal-header">
      <div><span class="eyebrow">{$t("Application preferences")}</span><h2 id="settings-title">{$t("Settings")}</h2></div>
      <button class="icon-button" aria-label={$t("Close")} onclick={onClose}><Icon name="x" size={18} /></button>
    </header>

    <form class="settings-layout" onsubmit={(event) => { event.preventDefault(); submit(); }}>
      <nav class="settings-nav" aria-label={$t("Settings sections")}>
        {#each sections as section}
          <button type="button" class:active={active === section.id} aria-current={active === section.id ? "page" : undefined} onclick={() => active = section.id}>
            <Icon name={section.icon} size={15} /><span>{$t(section.label)}</span>
          </button>
        {/each}
      </nav>

      <div class="settings-content">
        {#if !canSave}
          <div class="settings-lock"><Icon name="alert" size={15} /><span>{$t("Disconnect to change networking settings.")}</span></div>
        {/if}

        {#if !draft}
          <div class="settings-loading"><Icon name="loader" class="spin" size={22} /><span>{$t("Preparing settings…")}</span></div>
        {:else if active === 'general'}
          <section class="settings-section">
            <div class="settings-section-head"><h3>{$t("General")}</h3><p>{$t("Make Netch work the way you prefer.")}</p></div>
            <SettingToggle bind:checked={draft.startup.runAtStartup} title={$t("Start with Windows")} description={$t("Open Netch when you sign in.")} />
            <SettingToggle bind:checked={draft.startup.closeToTray} title={$t("Keep running in the tray")} description={$t("Closing the window keeps your connection active.")} />
            <label class="setting-field"><span><strong>{$t("Interface language")}</strong><small>{$t("Applied completely after restarting Netch.")}</small></span><select bind:value={draft.general.language}>{#each document.languages as language}<option value={language}>{language === 'System' ? $t('System language') : languageName(language)}</option>{/each}</select></label>
            <details class="subscription-advanced"><summary>{$t("Saved profile layout")}</summary>
            <div class="setting-pair">
              <label class="setting-field"><span><strong>{$t("Saved profile slots")}</strong><small>{$t("Save server and routing combinations for later.")}</small></span><input type="number" min="0" max="100" bind:value={draft.general.profileCount} /></label>
              <label class="setting-field"><span><strong>{$t("Profile columns")}</strong><small>{$t("Layout preference for saved profiles.")}</small></span><input type="number" min="1" max="20" bind:value={draft.general.profileColumns} /></label>
            </div>
            </details>
          </section>
        {:else if active === 'connection'}
          <section class="settings-section">
            <div class="settings-section-head"><h3>{$t("Connection")}</h3><p>{$t("Local proxy endpoints, timeouts, and latency testing.")}</p></div>
            <div class="setting-pair">
              <label class="setting-field"><span><strong>{$t("SOCKS5 port")}</strong><small>{$t("Local SOCKS5 listener.")}</small></span><input type="number" min="1" max="65535" bind:value={draft.connection.socks5Port} /></label>
              <label class="setting-field"><span><strong>{$t("HTTP port")}</strong><small>{$t("Local HTTP proxy listener.")}</small></span><input type="number" min="1" max="65535" bind:value={draft.connection.httpPort} /></label>
            </div>
            <label class="setting-field"><span><strong>{$t("Local proxy address")}</strong><small>{$t("Use 127.0.0.1 for this PC only or 0.0.0.0 for LAN access.")}</small></span><input bind:value={draft.connection.localAddress} spellcheck="false" /></label>
            <div class="setting-pair">
              <label class="setting-field"><span><strong>{$t("Latency method")}</strong><small>{$t("HTTP tests the website through each server. TCP/ICMP only test the endpoint.")}</small></span><select bind:value={draft.connection.pingMethod}><option value="http">{$t('HTTP through VPN')}</option><option value="tcp">TCP</option><option value="icmp">ICMP</option></select></label>
              <label class="setting-field"><span><strong>{$t("Request timeout")}</strong><small>{$t("HTTP operations in milliseconds.")}</small></span><input type="number" min="1000" max="120000" step="500" bind:value={draft.connection.requestTimeoutMs} /></label>
            </div>
            <label class="setting-field"><span><strong>{$t('Latency test URL')}</strong><small>{$t('Used for HTTP latency and automatic server selection.')}</small></span><input type="url" bind:value={draft.connection.latencyTestUrl} placeholder="https://www.google.com/generate_204" /></label>
            <div class="setting-pair">
              <label class="setting-field"><span><strong>{$t("Latency interval")}</strong><small>{$t("Seconds between background tests; 0 disables.")}</small></span><input type="number" min="0" max="86400" bind:value={draft.connection.detectionIntervalSeconds} /></label>
              <label class="setting-field"><span><strong>{$t("Startup test delay")}</strong><small>{$t("Seconds after launch; -1 disables.")}</small></span><input type="number" min="-1" max="86400" bind:value={draft.connection.startupPingDelaySeconds} /></label>
            </div>
            <div class="setting-pair wide-first">
              <label class="setting-field"><span><strong>{$t("STUN host")}</strong><small>{$t("Used for NAT diagnostics.")}</small></span><input bind:value={draft.connection.stunHost} spellcheck="false" /></label>
              <label class="setting-field"><span><strong>{$t("STUN port")}</strong><small>{$t("Usually 3478.")}</small></span><input type="number" min="1" max="65535" bind:value={draft.connection.stunPort} /></label>
            </div>
          </section>
        {:else if active === 'routing'}
          <section class="settings-section">
            <div class="settings-section-head"><h3>{$t("Routing")}</h3><p>{$t("Choose how traffic from selected applications is handled.")}</p></div>
            <div class="toggle-grid">
              <SettingToggle bind:checked={draft.routing.filterTcp} title={$t("Route TCP")} description={$t("Capture TCP traffic from selected applications.")} />
              <SettingToggle bind:checked={draft.routing.filterUdp} title={$t("Route UDP")} description={$t("Capture UDP traffic from selected applications.")} />
              <SettingToggle bind:checked={draft.routing.filterDns} title={$t("Filter DNS")} description={$t("Handle DNS requests from selected processes.")} />
              <SettingToggle bind:checked={draft.routing.includeChildProcesses} title={$t("Include child processes")} description={$t("Apply routing to processes launched by selected apps.")} />
              <SettingToggle bind:checked={draft.routing.proxyDns} title={$t("Proxy DNS")} description={$t("Send captured DNS through the proxy path.")} />
              <SettingToggle bind:checked={draft.routing.handleOnlyDns} title={$t("DNS-only handling")} description={$t("Limit the DNS helper to handled processes.")} />
              <SettingToggle bind:checked={draft.routing.filterIcmp} title={$t("Filter ICMP")} description={$t("Enable ICMP handling for process mode.")} />
            </div>
            <div class="setting-pair">
              <label class="setting-field"><span><strong>{$t("Process DNS endpoint")}</strong><small>{$t("Resolver in host:port format.")}</small></span><input bind:value={draft.routing.dnsHost} spellcheck="false" /></label>
              <label class="setting-field"><span><strong>{$t("ICMP delay")}</strong><small>{$t("Artificial ICMP delay in milliseconds.")}</small></span><input type="number" min="0" max="60000" bind:value={draft.routing.icmpDelayMs} /></label>
            </div>
          </section>
        {:else if active === 'subscriptions'}
          <section class="settings-section">
            <div class="settings-section-head"><h3>{$t("Subscriptions")}</h3><p>{$t("Control automatic refresh and manage server sources.")}</p></div>
            <SettingToggle bind:checked={draft.subscriptions.updateOnLaunch} title={$t("Update subscriptions on launch")} description={$t("Refresh every enabled source when Netch starts.")} />
            <button type="button" class="settings-callout" onclick={onManageSubscriptions}>
              <span class="route-icon"><Icon name="download" size={18} /></span>
              <span><strong>{$t("Manage subscriptions")}</strong><small>{$t("Add, edit, delete, or refresh subscription URLs.")}</small></span>
              <em>{$t("Open manager")}</em>
            </button>
          </section>
        {:else if active === 'dns'}
          <section class="settings-section">
            <div class="settings-section-head"><h3>DNS</h3><p>{$t("Advanced network addresses. The defaults work for most connections.")}</p></div>
            <div class="setting-pair">
              <label class="setting-field"><span><strong>{$t("China DNS")}</strong><small>Example: tcp://223.5.5.5:53</small></span><input bind:value={draft.dns.chinaDns} spellcheck="false" /></label>
              <label class="setting-field"><span><strong>{$t("Other DNS")}</strong><small>Example: tcp://1.1.1.1:53</small></span><input bind:value={draft.dns.otherDns} spellcheck="false" /></label>
            </div>
            <div class="setting-pair three">
              <label class="setting-field"><span><strong>{$t("TUN address")}</strong><small>{$t("Adapter IP.")}</small></span><input bind:value={draft.dns.tunAddress} spellcheck="false" /></label>
              <label class="setting-field"><span><strong>{$t("Netmask")}</strong><small>{$t("Adapter mask.")}</small></span><input bind:value={draft.dns.tunNetmask} spellcheck="false" /></label>
              <label class="setting-field"><span><strong>{$t("Gateway")}</strong><small>{$t("Adapter gateway.")}</small></span><input bind:value={draft.dns.tunGateway} spellcheck="false" /></label>
            </div>
            <div class="toggle-grid">
              <SettingToggle bind:checked={draft.dns.useCustomDns} title={$t("Custom TUN DNS")} description={$t("Override AioDNS for the TUN adapter.")} />
              <SettingToggle bind:checked={draft.dns.proxyTunDns} title={$t("Proxy TUN DNS")} description={$t("Route TUN DNS queries through Netch.")} />
            </div>
            <label class="setting-field"><span><strong>{$t("TUN DNS address")}</strong><small>{$t("Used only when custom TUN DNS is enabled.")}</small></span><input bind:value={draft.dns.tunDns} disabled={!draft.dns.useCustomDns} spellcheck="false" /></label>
            <label class="setting-field vertical"><span><strong>{$t("Bypass networks")}</strong><small>{$t("One IP/CIDR entry per line, for example 192.168.0.0/16.")}</small></span><textarea rows="4" bind:value={bypassText} spellcheck="false"></textarea></label>
          </section>
        {:else if active === 'startup'}
          <section class="settings-section">
            <div class="settings-section-head"><h3>{$t("Startup")}</h3><p>{$t("Windows launch and window lifecycle behavior.")}</p></div>
            <div class="toggle-grid">
              <SettingToggle bind:checked={draft.startup.runAtStartup} title={$t("Start with Windows")} description={$t("Create the existing elevated Netch startup task.")} />
              <SettingToggle bind:checked={draft.startup.connectOnLaunch} title={$t("Connect automatically")} description={$t("Use the last selected server and mode after launch.")} />
              <SettingToggle bind:checked={draft.startup.minimizeOnLaunch} title={$t("Start minimized")} description={$t("Hide or minimize the main window after loading.")} />
              <SettingToggle bind:checked={draft.startup.closeToTray} title={$t("Close window to tray")} description={$t("Keep Netch running when the close button is pressed.")} />
              <SettingToggle bind:checked={draft.startup.stopConnectionOnExit} title={$t("Stop connection on exit")} description={$t("Cleanly stop routing and proxy cores before quitting.")} />
            </div>
          </section>
        {:else if active === 'updates'}
          <section class="settings-section">
            <div class="settings-section-head"><h3>{$t("Updates")}</h3><p>{$t("Control automatic update discovery.")}</p></div>
            <div class="toggle-grid">
              <SettingToggle bind:checked={draft.updates.checkOnLaunch} title={$t("Check on launch")} description={$t("Look for a newer Netch release after startup.")} />
              <SettingToggle bind:checked={draft.updates.includeBetaVersions} title={$t("Include beta versions")} description={$t("Show prerelease builds in update checks.")} />
            </div>
          </section>
        {:else}
          <section class="settings-section">
            <div class="settings-section-head"><h3>{$t("Advanced")}</h3><p>{$t("Compatibility and transport tuning. Change only when required.")}</p></div>
            <div class="advanced-warning"><Icon name="alert" size={15} /><span>{$t("These values affect proxy-core behavior and may reduce security or stability.")}</span></div>
            <div class="toggle-grid">
              <SettingToggle bind:checked={draft.advanced.xrayCone} title={$t("Xray cone NAT")} description={$t("Use the existing Xray cone behavior.")} />
              <SettingToggle bind:checked={draft.advanced.allowInsecureTls} title={$t("Allow insecure TLS")} description={$t("Skip certificate validation for compatible servers.")} danger />
              <SettingToggle bind:checked={draft.advanced.useMux} title={$t("Enable Mux")} description={$t("Multiplex supported proxy connections.")} />
              <SettingToggle bind:checked={draft.advanced.tcpFastOpen} title={$t("TCP Fast Open")} description={$t("Enable TFO when supported by Windows and the core.")} />
              <SettingToggle bind:checked={draft.advanced.hideUnsupportedEnvironmentWarning} title={$t("Hide support warning")} description={$t("Do not show compatibility warnings for this environment.")} />
              <SettingToggle bind:checked={draft.advanced.kcpCongestion} title={$t("KCP congestion control")} description={$t("Enable the configured KCP congestion mode.")} />
            </div>
            <div class="setting-pair three">
              <label class="setting-field"><span><strong>{$t("KCP MTU")}</strong><small>{$t("Packet size.")}</small></span><input type="number" min="576" max="9000" bind:value={draft.advanced.kcpMtu} /></label>
              <label class="setting-field"><span><strong>{$t("KCP TTI")}</strong><small>{$t("Update interval.")}</small></span><input type="number" min="1" max="10000" bind:value={draft.advanced.kcpTti} /></label>
              <label class="setting-field"><span><strong>{$t("Uplink")}</strong><small>{$t("Capacity.")}</small></span><input type="number" min="0" max="65535" bind:value={draft.advanced.kcpUplinkCapacity} /></label>
              <label class="setting-field"><span><strong>{$t("Downlink")}</strong><small>{$t("Capacity.")}</small></span><input type="number" min="0" max="65535" bind:value={draft.advanced.kcpDownlinkCapacity} /></label>
              <label class="setting-field"><span><strong>{$t("Read buffer")}</strong><small>{$t("Buffer size.")}</small></span><input type="number" min="0" max="65535" bind:value={draft.advanced.kcpReadBufferSize} /></label>
              <label class="setting-field"><span><strong>{$t("Write buffer")}</strong><small>{$t("Buffer size.")}</small></span><input type="number" min="0" max="65535" bind:value={draft.advanced.kcpWriteBufferSize} /></label>
            </div>
          </section>
        {/if}
      </div>

      <footer class="settings-footer">
        <span>{$t("Changes apply when you save.")}</span>
        <button type="button" class="button secondary" onclick={onClose}>{$t("Cancel")}</button>
        <button class="button primary" disabled={!draft || saving || !canSave}>{saving ? $t("Saving…") : $t("Save settings")}</button>
      </footer>
    </form>
  </div>
</div>
