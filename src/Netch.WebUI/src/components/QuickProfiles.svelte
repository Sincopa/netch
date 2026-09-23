<script lang="ts">
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { QuickProfile } from '../lib/types';

  let {
    profiles,
    disabled,
    onActivate,
    onManage
  }: {
    profiles: QuickProfile[];
    disabled: boolean;
    onActivate: (slot: number) => void;
    onManage: (slot?: number) => void;
  } = $props();

  function select(profile: QuickProfile) {
    if (profile.status === 'ready') onActivate(profile.slot);
    else onManage(profile.slot);
  }
</script>

<div class="quick-profile-block">
  <div class="section-title quick-profile-title">
    <span><Icon name="bookmark" size={15} /> {$t("Quick profiles")}</span>
    <button class="text-button" onclick={() => onManage()}>{$t("Manage")}</button>
  </div>

  {#if profiles.length}
    <div class="quick-profile-strip" aria-label={$t("Quick profiles")}>
      {#each profiles as profile (profile.slot)}
        <button
          class:empty={profile.status === 'empty'}
          class:missing={profile.status === 'missing'}
          class="quick-profile-chip"
          disabled={disabled}
          title={profile.status === 'ready'
            ? `${profile.serverName} · ${profile.modeName}`
            : profile.status === 'missing' ? $t("Saved server or mode is unavailable") : $t("Save current selection")}
          onclick={() => select(profile)}
        >
          <span class="profile-slot">{profile.slot + 1}</span>
          <span>{profile.name ?? $t("Empty slot")}</span>
          {#if profile.status === 'missing'}<Icon name="alert" size={13} />
          {:else if profile.status === 'empty'}<Icon name="plus" size={13} />{/if}
        </button>
      {/each}
    </div>
  {:else}
    <button class="quick-profile-empty" onclick={() => onManage()}>
      {$t("Enable quick-profile slots in Settings")}
    </button>
  {/if}
</div>
