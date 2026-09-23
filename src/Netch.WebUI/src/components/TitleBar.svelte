<script lang="ts">
  import { t } from '../lib/i18n';
  import Icon from './ui/Icon.svelte';
  import { netch } from '../lib/bridge';
  let { status }: { status: string } = $props();
  function beginDrag(event: PointerEvent) {
    if (event.button !== 0 || (event.target as HTMLElement).closest('button')) return;
    void netch.window.beginDrag();
  }
</script>
<header class="titlebar" role="toolbar" aria-label={$t('Window controls')} tabindex="-1" onpointerdown={beginDrag} ondblclick={(event) => { if (!(event.target as HTMLElement).closest('button')) void netch.window.toggleMaximize(); }}>
  <div class="brand" aria-label="Netch"><img src="/logo.svg" alt="" width="28" height="28" /><span>netch<span class="brand-period">.</span></span></div>
  <span class="titlebar-caption">{$t('A little more freedom online')}</span>
  <div class="window-actions">
    <span class="connection-pill" class:online={status === 'connected'}><i></i>{$t(status === 'connected' ? 'Connected' : status === 'disconnected' ? 'Disconnected' : status === 'error' ? 'Connection error' : status === 'connecting' ? 'Connecting' : status === 'reconnecting' ? 'Reconnecting' : 'Disconnecting')}</span>
    <span class="action-divider"></span>
    <button class="window-button" title={$t('Minimize')} aria-label={$t('Minimize')} onclick={() => netch.window.minimize()}><Icon name="minus" /></button>
    <button class="window-button" title={$t('Maximize')} aria-label={$t('Maximize')} onclick={() => netch.window.toggleMaximize()}><Icon name="square" size={13} /></button>
    <button class="window-button close" title={$t('Close')} aria-label={$t('Close')} onclick={() => netch.window.close()}><Icon name="x" size={18} /></button>
  </div>
</header>
