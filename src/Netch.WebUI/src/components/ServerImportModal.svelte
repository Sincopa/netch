<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';

  let { groups, busy, onClose, onImport }: {
    groups: string[];
    busy: boolean;
    onClose: () => void;
    onImport: (text: string, group: string) => void;
  } = $props();

  let text = $state('');
  let group = $state('');

  function submit() {
    if (!text.trim() || busy) return;
    onImport(text, group);
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="modal server-import-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="server-import-title">
    <header class="modal-header">
      <div><span class="eyebrow">{$t("Connect with a link")}</span><h2 id="server-import-title">{$t("Import servers")}</h2></div>
      <button class="icon-button" aria-label={$t("Close import")} onclick={onClose}><Icon name="x" size={17} /></button>
    </header>
    <div class="server-import-body">
      <div class="import-note"><Icon name="shield" size={16} /><span><strong>{$t("Parsed locally")}</strong><small>{$t("Paste share links, Netch links, Shadowsocks JSON, or a Base64 subscription body.")}</small></span></div>
      <label class="server-form-field">
        <span><strong>{$t("Destination group")}</strong><small>{$t("Leave blank for Local, or enter a local group.")}</small></span>
        <input list="server-local-groups" maxlength="64" bind:value={group} placeholder={$t("Local")} />
        <datalist id="server-local-groups">{#each groups as value}<option value={value}></option>{/each}</datalist>
      </label>
      <label class="server-form-field vertical">
        <span><strong>{$t("Share-link data")}</strong><small>{$t("One or several links, plain text or Base64 encoded.")}</small></span>
        <textarea spellcheck="false" bind:value={text} placeholder="vless://…&#10;ss://…&#10;Netch://…"></textarea>
      </label>
    </div>
    <footer class="modal-footer server-form-footer">
      <span>{$t("Unsupported lines are ignored by the existing Netch parser.")}</span>
      <button class="button ghost" disabled={busy} onclick={onClose}>{$t("Cancel")}</button>
      <button class="button primary" disabled={busy || !text.trim()} onclick={submit}>{busy ? 'Importing…' : 'Import'}</button>
    </footer>
  </div>
</div>
