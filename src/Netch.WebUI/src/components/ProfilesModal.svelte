<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { QuickProfile } from '../lib/types';

  let {
    profiles,
    selectedServerId,
    selectedModeId,
    busy,
    initialSlot = null,
    onClose,
    onSave,
    onDelete,
    onActivate
  }: {
    profiles: QuickProfile[];
    selectedServerId: string | null;
    selectedModeId: string | null;
    busy: boolean;
    initialSlot?: number | null;
    onClose: () => void;
    onSave: (slot: number, name: string) => void;
    onDelete: (slot: number) => void;
    onActivate: (slot: number) => void;
  } = $props();

  let editSlot = $state<number | null>(null);
  let profileName = $state('');
  let deleteSlot = $state<number | null>(null);
  let initialized = $state(false);
  let currentSelectionReady = $derived(Boolean(selectedServerId && selectedModeId));

  $effect(() => {
    if (initialized) return;
    editSlot = initialSlot;
    if (initialSlot !== null) {
      const profile = profiles.find((value) => value.slot === initialSlot);
      profileName = profile?.name ?? $t("Profile {0}", initialSlot + 1);
    }
    initialized = true;
  });

  function edit(profile: QuickProfile) {
    editSlot = profile.slot;
    profileName = profile.name ?? $t("Profile {0}", profile.slot + 1);
  }

  function submit() {
    if (editSlot === null || !profileName.trim() || !currentSelectionReady) return;
    onSave(editSlot, profileName.trim());
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="modal profiles-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="profiles-title">
    <header class="modal-header">
      <div>
        <span class="eyebrow">{$t("Selection presets")}</span>
        <h2 id="profiles-title">{$t("Quick profiles")}</h2>
        <p>{$t("Save a server and connection mode to switch between them in one click.")}</p>
      </div>
      <button class="icon-button" aria-label={$t("Close profiles")} onclick={onClose}><Icon name="x" size={17} /></button>
    </header>

    {#if editSlot !== null}
      <div class="profile-editor">
        <span class="profile-editor-index">{String(editSlot + 1).padStart(2, '0')}</span>
        <label>
          <span>{$t("Profile name")}</span>
          <input maxlength="64" bind:value={profileName} placeholder={$t("Gaming")} onkeydown={(event) => event.key === 'Enter' && submit()} />
        </label>
        <div class="profile-editor-context">
          <small>{$t("Current selection")}</small>
          <strong>{currentSelectionReady ? $t("Server + mode ready") : $t("Select a server and mode first")}</strong>
        </div>
        <button class="button primary compact-button" disabled={busy || !profileName.trim() || !currentSelectionReady} onclick={submit}>
          {busy ? $t("Saving…") : $t("Save current")}
        </button>
        <button class="button ghost compact-button" disabled={busy} onclick={() => editSlot = null}>{$t("Cancel")}</button>
      </div>
    {/if}

    <div class="profile-slot-list">
      {#each profiles as profile (profile.slot)}
        <article class:missing={profile.status === 'missing'} class:empty={profile.status === 'empty'} class="profile-slot-row">
          <span class="profile-slot-number">{String(profile.slot + 1).padStart(2, '0')}</span>
          <span class="profile-slot-copy">
            <strong>{profile.name ?? $t("Empty slot")}</strong>
            <small>{profile.status === 'empty'
              ? $t("Available for the current selection")
              : `${profile.serverName ?? $t("Missing server")} · ${profile.modeName ?? $t("Missing mode")}`}</small>
          </span>
          <span class="profile-state {profile.status}">{profile.status === 'ready' ? $t("Ready") : profile.status === 'missing' ? 'Unavailable' : 'Empty'}</span>
          <div class="profile-slot-actions">
            {#if profile.status === 'ready'}
              <button class="button ghost compact-button" disabled={busy} onclick={() => onActivate(profile.slot)}>{$t("Activate")}</button>
            {/if}
            <button class="button secondary compact-button" disabled={busy || !currentSelectionReady} onclick={() => edit(profile)}>
              {profile.status === 'empty' ? $t("Save") : $t('Overwrite')}
            </button>
            {#if profile.status !== 'empty'}
              <button class="icon-button danger-button" disabled={busy} aria-label={$t("Delete {0}", profile.name)} onclick={() => deleteSlot = profile.slot}>
                <Icon name="trash" size={15} />
              </button>
            {/if}
          </div>
        </article>
      {:else}
        <div class="empty profile-empty-state">
          <Icon name="bookmark" size={22} />
          <strong>{$t("No quick-profile slots")}</strong>
          <span>{$t("Set Profile count above zero in General settings.")}</span>
        </div>
      {/each}
    </div>

    <footer class="modal-footer profile-modal-footer">
      <span>{$t("Activating a profile changes selection only. It does not connect automatically.")}</span>
      <button class="button secondary" onclick={onClose}>{$t("Done")}</button>
    </footer>

    {#if deleteSlot !== null}
      <div class="confirm-layer">
        <div class="confirm-card" role="alertdialog" tabindex="-1" use:dialogFocus aria-modal="true" aria-labelledby="delete-profile-title" onkeydown={(event) => { if (event.key === 'Escape') { event.stopPropagation(); deleteSlot = null; } }}>
          <span class="confirm-icon"><Icon name="trash" size={18} /></span>
          <h3 id="delete-profile-title">{$t("Delete this profile?")}</h3>
          <p>{$t("The slot becomes empty. Servers and modes are not affected.")}</p>
          <div>
            <button class="button ghost" disabled={busy} onclick={() => deleteSlot = null}>{$t("Cancel")}</button>
            <button class="button danger" disabled={busy} onclick={() => { onDelete(deleteSlot!); deleteSlot = null; }}>{$t("Delete")}</button>
          </div>
        </div>
      </div>
    {/if}
  </div>
</div>
