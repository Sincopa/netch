<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';

  let { title, message, confirmLabel = $t("Delete"), busy, onClose, onConfirm }: {
    title: string;
    message: string;
    confirmLabel?: string;
    busy: boolean;
    onClose: () => void;
    onConfirm: () => void;
  } = $props();
</script>

<div class="modal-backdrop confirm-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="confirm-card" role="alertdialog" use:dialogFocus aria-modal="true" aria-labelledby="confirm-title">
    <span class="confirm-icon"><Icon name="trash" size={18} /></span>
    <h3 id="confirm-title">{$t(title)}</h3>
    <p>{$t(message)}</p>
    <div>
      <button class="button ghost" disabled={busy} onclick={onClose}>{$t("Cancel")}</button>
      <button class="button danger" disabled={busy} onclick={onConfirm}>{busy ? $t('Working…') : confirmLabel}</button>
    </div>
  </div>
</div>
