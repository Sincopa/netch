<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { UpdateState } from '../lib/types';

  let { update, busy, onClose, onCheck, onDownload, onApply }: {
    update: UpdateState;
    busy: boolean;
    onClose: () => void;
    onCheck: (includePrerelease: boolean) => void;
    onDownload: () => void;
    onApply: () => void;
  } = $props();

  let includePrerelease = $state(false);
  let working = $derived(update.status === 'checking' || update.status === 'downloading' || update.status === 'applying' || busy);

  function heading() {
    if (update.status === 'up-to-date') return $t("You are up to date");
    if (update.status === 'available') return $t("Netch {0} is available", update.latestVersion);
    if (update.status === 'ready') return $t("Update verified and ready");
    if (update.status === 'applied') return $t("Restarting Netch");
    if (update.status === 'error') return $t("Update operation failed");
    if (update.status === 'checking') return $t("Checking releases");
    if (update.status === 'downloading') return $t("Downloading {0}%", update.progress);
    if (update.status === 'applying') return $t("Applying update");
    return $t("Check for updates");
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && !working && onClose()}>
  <div class="modal update-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="update-title">
    <header class="modal-header">
      <div><span class="eyebrow">{$t("SHA-256 verified release")}</span><h2 id="update-title">{$t("Updates")}</h2></div>
      <button class="icon-button" disabled={working} aria-label={$t("Close")} onclick={onClose}><Icon name="x" size={18} /></button>
    </header>

    <div class="update-body">
      <div class="update-hero">
        <span class:active={update.status === 'available' || update.status === 'ready'} class="update-glyph"><Icon name="download" size={22} /></span>
        <span><small>{$t("Installed")} {update.currentVersion || 'unknown'}</small><h3>{heading()}</h3></span>
      </div>

      {#if update.status === 'downloading' || update.status === 'applying' || update.status === 'ready'}
        <div class="update-progress"><i style={`width: ${update.progress}%`}></i></div>
      {/if}
      {#if update.error}<div class="update-error"><Icon name="alert" size={15} />{update.error}</div>{/if}

      {#if update.releaseNotes}
        <section class="release-notes"><span class="eyebrow">{$t("Release notes")}</span><pre>{update.releaseNotes}</pre></section>
      {:else}
        <section class="update-empty"><Icon name="shield" size={21} /><span>{$t("Release metadata is fetched directly from the configured Netch GitHub repository.")}</span></section>
      {/if}
    </div>

    <footer class="modal-footer update-footer">
      <label class="prerelease-toggle"><input type="checkbox" bind:checked={includePrerelease} disabled={working} /><span>{$t("Include prerelease builds")}</span></label>
      <button class="button secondary" disabled={working} onclick={() => onCheck(includePrerelease)}><Icon name="refresh" class={update.status === 'checking' ? 'spin' : ''} size={13} />{$t("Check now")}</button>
      {#if update.canDownload}<button class="button primary" disabled={working} onclick={onDownload}>{$t("Download update")}</button>{/if}
      {#if update.canApply}<button class="button primary" disabled={working} onclick={onApply}>{$t("Install & restart")}</button>{/if}
    </footer>
  </div>
</div>
