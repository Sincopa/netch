<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import { onMount } from 'svelte';
  import Icon from './ui/Icon.svelte';
  import type { ProcessInfo } from '../lib/types';

  let {
    processes,
    selected,
    loading,
    saving,
    onClose,
    onSave,
    onRefresh,
    onPick
  }: {
    processes: ProcessInfo[];
    selected: string[];
    loading: boolean;
    saving: boolean;
    onClose: () => void;
    onSave: (values: string[]) => void;
    onRefresh: () => Promise<void>;
    onPick: () => Promise<ProcessInfo | null>;
  } = $props();

  let query = $state('');
  let draft = $state(new Set<string>());
  let selectedOnly = $state(false);
  let extras = $state<ProcessInfo[]>([]);
  let picking = $state(false);

  let entries = $derived.by(() => {
    const values = [...processes];
    const known = new Set(values.map((process) => process.executable.toLowerCase()));
    for (const extra of extras) {
      if (!known.has(extra.executable.toLowerCase())) values.push(extra);
    }
    for (const executable of selected) {
      if (!values.some((process) => process.executable.toLowerCase() === executable.toLowerCase())) {
        values.push({ processIds: [], name: executable.replace(/\.exe$/i, ''), executable, path: null, iconDataUrl: null });
      }
    }
    return values;
  });

  let filtered = $derived(entries.filter((process) => {
    const selectedMatch = !selectedOnly || draft.has(process.executable.toLowerCase());
    const queryMatch = `${process.name} ${process.executable} ${process.path ?? ''}`.toLowerCase().includes(query.toLowerCase());
    return selectedMatch && queryMatch;
  }));

  function toggle(executable: string) {
    const next = new Set(draft);
    const key = executable.toLowerCase();
    if (next.has(key)) next.delete(key);
    else next.add(key);
    draft = next;
  }

  function selectVisible() {
    const next = new Set(draft);
    filtered.forEach((process) => next.add(process.executable.toLowerCase()));
    draft = next;
  }

  async function pickExecutable() {
    picking = true;
    try {
      const process = await onPick();
      if (!process) return;
      extras = [...extras.filter((item) => item.executable.toLowerCase() !== process.executable.toLowerCase()), process];
      const next = new Set(draft);
      next.add(process.executable.toLowerCase());
      draft = next;
    } finally {
      picking = false;
    }
  }

  function instanceLabel(process: ProcessInfo) {
    if (process.processIds.length === 0) return $t("Not running");
    if (process.processIds.length === 1) return `PID ${process.processIds[0]}`;
    return $t("{0} instances", process.processIds.length);
  }

  onMount(() => draft = new Set(selected.map((value) => value.toLowerCase())));
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="modal process-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="process-title">
    <header class="modal-header">
      <div><span class="eyebrow">{$t("Only the apps you choose")}</span><h2 id="process-title">{$t("Selected applications")}</h2></div>
      <button class="icon-button" aria-label={$t("Close")} onclick={onClose}><Icon name="x" size={18} /></button>
    </header>

    <div class="process-toolbar">
      <label class="search-field"><Icon name="search" size={15} /><input bind:value={query} placeholder={$t("Search applications")} aria-label={$t("Search applications")} /></label>
      <button class:active={selectedOnly} class="toolbar-button" aria-pressed={selectedOnly} onclick={() => selectedOnly = !selectedOnly}><Icon name="check" size={14} />{$t("Selected")}</button>
      <button class="toolbar-button" disabled={loading} onclick={onRefresh}><Icon name="refresh" class={loading ? 'spin' : ''} size={14} />{$t("Refresh")}</button>
      <button class="toolbar-button" disabled={picking} onclick={pickExecutable}><Icon name="plus" size={14} />{$t("Add .exe")}</button>
    </div>

    <div class="selection-strip">
      <span><strong>{draft.size}</strong> {$t("Selected")}</span>
      <button onclick={selectVisible} disabled={filtered.length === 0}>{$t("Select visible")}</button>
      <button onclick={() => draft = new Set()} disabled={draft.size === 0}>{$t("Clear all")}</button>
    </div>

    <div class="process-list">
      {#if loading && processes.length === 0}
        <div class="empty"><Icon name="loader" class="spin" size={22} /><span>{$t("Reading Windows processes…")}</span></div>
      {:else}
        {#each filtered as process (process.executable.toLowerCase())}
          <button class:active={draft.has(process.executable.toLowerCase())} class="process-row" aria-pressed={draft.has(process.executable.toLowerCase())} onclick={() => toggle(process.executable)}>
            <span class="process-icon">
              {#if process.iconDataUrl}<img src={process.iconDataUrl} alt="" />{:else}{process.name.slice(0, 1).toUpperCase()}{/if}
            </span>
            <span class="process-copy">
              <span><strong>{process.name}</strong><em>{instanceLabel(process)}</em></span>
              <small>{process.path ?? process.executable}</small>
            </span>
            <span class="check-box">{#if draft.has(process.executable.toLowerCase())}<Icon name="check" size={13} />{/if}</span>
          </button>
        {:else}
          <div class="empty compact"><span>{selectedOnly ? $t("No selected applications match") : $t("No matching applications")}</span></div>
        {/each}
      {/if}
    </div>
    <footer class="modal-footer">
      <p>{$t("Choose at least one app. Your selection is kept when you change servers.")}</p>
      <button class="button secondary" onclick={onClose}>{$t("Cancel")}</button>
      <button class="button primary" disabled={loading || saving || draft.size === 0} onclick={() => onSave([...draft])}>{saving ? $t("Saving…") : $t("Apply routing")}</button>
    </footer>
  </div>
</div>
