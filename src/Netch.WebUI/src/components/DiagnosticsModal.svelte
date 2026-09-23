<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { DiagnosticsSnapshot } from '../lib/types';

  let { snapshot, loading, busyDriver, onClose, onRefresh, onRepair }: {
    snapshot: DiagnosticsSnapshot | null;
    loading: boolean;
    busyDriver: string | null;
    onClose: () => void;
    onRefresh: () => void;
    onRepair: (driverId: string) => void;
  } = $props();

  function statusLabel(value: string) {
    return ({ ready: 'Ready', missing: 'Missing', 'missing-source': 'Source missing', outdated: 'Repair needed', error: 'Error' } as Record<string, string>)[value] ?? value;
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="modal diagnostics-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="diagnostics-title">
    <header class="modal-header">
      <div><span class="eyebrow">{$t("System readiness")}</span><h2 id="diagnostics-title">{$t("Diagnostics")}</h2></div>
      <div class="modal-header-actions">
        <button class="toolbar-button" disabled={loading || busyDriver !== null} onclick={onRefresh}><Icon name="refresh" class={loading ? 'spin' : ''} size={14} />{$t("Refresh")}</button>
        <button class="icon-button" aria-label={$t("Close")} onclick={onClose}><Icon name="x" size={18} /></button>
      </div>
    </header>

    {#if loading && !snapshot}
      <div class="diagnostic-skeleton" aria-label={$t("Loading diagnostics")}>
        {#each Array(5) as _}<div><i></i><span></span><b></b></div>{/each}
      </div>
    {:else if snapshot}
      <div class="diagnostic-environment">
        <div><small>Netch</small><strong>{snapshot.appVersion}</strong></div>
        <div><small>{$t("Runtime")}</small><strong>{snapshot.runtimeVersion}</strong></div>
        <div><small>{$t("Architecture")}</small><strong>{snapshot.architecture}</strong></div>
        <div><small>{$t("Privileges")}</small><strong class:warning={!snapshot.isAdministrator}>{snapshot.isAdministrator ? $t("Administrator") : $t("Standard user")}</strong></div>
      </div>
      <div class="diagnostic-os">{snapshot.operatingSystem}<span>WebView2 {snapshot.webView2Version ?? $t('not detected')}</span></div>
      <div class="diagnostic-list">
        {#each snapshot.components as component (component.id)}
          <article class="diagnostic-row">
            <span class="diagnostic-symbol {component.status}"><Icon name={component.status === 'ready' ? 'check' : 'alert'} size={15} /></span>
            <span><strong>{component.name}</strong><small>{component.summary}</small></span>
            {#if component.version}<code>{component.version}</code>{/if}
            <em class={component.status}>{$t(statusLabel(component.status))}</em>
            {#if component.canRepair}
              <button class="button secondary compact-button" disabled={busyDriver !== null || component.status === 'ready'} onclick={() => onRepair(component.id)}>
                {#if busyDriver === component.id}<Icon name="loader" class="spin" size={13} />{/if}
                {component.status === 'outdated' ? $t("Repair") : $t('Install')}
              </button>
            {/if}
          </article>
        {/each}
      </div>
    {/if}

    <footer class="modal-footer">
      <p>{$t("Checks are local. Driver actions require confirmation and administrator access.")}</p>
      <button class="button secondary" onclick={onClose}>{$t("Close")}</button>
    </footer>
  </div>
</div>
