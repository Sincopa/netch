<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { EditableModeKind, InheritedSwitch, ModeDetails, ModeWrite } from '../lib/types';

  let {
    details,
    selectedModeId,
    busy,
    canEdit,
    onClose,
    onSave,
    onDelete,
    onSelect
  }: {
    details: ModeDetails[];
    selectedModeId: string | null;
    busy: boolean;
    canEdit: boolean;
    onClose: () => void;
    onSave: (value: ModeWrite) => void;
    onDelete: (id: string) => void;
    onSelect: (id: string) => void;
  } = $props();

  let activeId = $state<string | null>(null);
  let draft = $state<ModeWrite | null>(null);
  let bypassText = $state('');
  let handleText = $state('');
  let creating = $state(false);
  let deleteArmed = $state(false);
  let initialized = $state(false);
  let active = $derived(details.find((mode) => mode.id === activeId) ?? null);
  let editable = $derived(Boolean(canEdit && (creating || active?.editable)));

  $effect(() => {
    if (initialized || details.length === 0) return;
    open(details.find((mode) => mode.id === selectedModeId) ?? details[0]);
    initialized = true;
  });

  function open(mode: ModeDetails) {
    activeId = mode.id;
    creating = false;
    deleteArmed = false;
    draft = mode.kind === 'ShareMode' ? null : {
      id: mode.id,
      name: mode.name,
      kind: mode.kind,
      filterIcmp: mode.filterIcmp,
      filterTcp: mode.filterTcp,
      filterUdp: mode.filterUdp,
      filterDns: mode.filterDns,
      includeChildProcesses: mode.includeChildProcesses,
      icmpDelayMs: mode.icmpDelayMs,
      proxyDns: mode.proxyDns,
      handleOnlyDns: mode.handleOnlyDns,
      dnsHost: mode.dnsHost,
      filterLoopback: mode.filterLoopback,
      filterIntranet: mode.filterIntranet,
      bypassRules: [...mode.bypassRules],
      handleRules: [...mode.handleRules]
    };
    bypassText = mode.bypassRules.join('\n');
    handleText = mode.handleRules.join('\n');
  }

  function create(kind: EditableModeKind) {
    activeId = null;
    creating = true;
    deleteArmed = false;
    draft = {
      name: '', kind,
      filterIcmp: null, filterTcp: null, filterUdp: null, filterDns: null,
      includeChildProcesses: null, icmpDelayMs: null, proxyDns: null, handleOnlyDns: null,
      dnsHost: null, filterLoopback: false, filterIntranet: true,
      bypassRules: [], handleRules: []
    };
    bypassText = '';
    handleText = '';
  }

  function lines(value: string) {
    return value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
  }

  function submit() {
    if (!draft || !editable || !draft.name.trim()) return;
    draft.bypassRules = lines(bypassText);
    draft.handleRules = lines(handleText);
    onSave($state.snapshot(draft) as ModeWrite);
  }

  function switchValue(value: string): InheritedSwitch {
    return value === 'inherit' ? null : value === 'on';
  }

  function switchText(value: InheritedSwitch) {
    return value === null ? 'inherit' : value ? 'on' : 'off';
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="modal modes-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="modes-title">
    <header class="modal-header">
      <div><span class="eyebrow">{$t("Routing definitions")}</span><h2 id="modes-title">{$t("Advanced modes")}</h2><p>{$t("Inspect built-in modes or maintain JSON modes under mode\\Custom.")}</p></div>
      <button class="icon-button" aria-label={$t("Close modes")} onclick={onClose}><Icon name="x" size={18} /></button>
    </header>

    <div class="modes-layout">
      <aside class="mode-catalog">
        <div class="mode-create-row">
          <button disabled={!canEdit || busy} onclick={() => create('ProcessMode')}><Icon name="plus" size={13} />{$t("Process")}</button>
          <button disabled={!canEdit || busy} onclick={() => create('TunMode')}><Icon name="plus" size={13} />TUN</button>
        </div>
        <div class="mode-catalog-list">
          {#each details as mode (mode.id)}
            <button class:active={!creating && activeId === mode.id} onclick={() => open(mode)}>
              <span class="mode-kind-icon"><Icon name={mode.kind === 'ProcessMode' ? 'boxes' : mode.kind === 'TunMode' ? 'globe' : 'share'} size={15} /></span>
              <span><strong>{mode.name || $t("Unnamed mode")}</strong><small>{mode.kind.replace('Mode', '')} · {mode.source}</small></span>
              {#if mode.id === selectedModeId}<i title={$t("Currently selected")}></i>{/if}
            </button>
          {/each}
        </div>
      </aside>

      <main class="mode-editor">
        {#if !canEdit}<div class="settings-lock"><Icon name="alert" size={15} /><span>{$t("Disconnect before changing modes.")}</span></div>{/if}
        {#if active?.kind === 'ShareMode' && !creating}
          <section class="mode-readonly">
            <span class="mode-readonly-glyph"><Icon name="share" size={24} /></span>
            <div><span class="eyebrow">{$t("Share mode · read only")}</span><h3>{active.name}</h3><p>{$t("Share arguments remain protected because they are passed directly to pcap2socks.")}</p></div>
            <label class="mode-field vertical"><span><strong>{$t("Source file")}</strong><small>{active.fileName}</small></span><input value={active.fileName} readonly /></label>
            <label class="mode-field vertical"><span><strong>pcap2socks arguments</strong><small>{$t("Visible for compatibility review only.")}</small></span><textarea rows="3" readonly value={active.shareArgument ?? ''}></textarea></label>
          </section>
        {:else if draft}
          <form onsubmit={(event) => { event.preventDefault(); submit(); }}>
            <div class="mode-editor-head">
              <div><span class="eyebrow">{creating ? $t("New custom definition") : active?.editable ? $t("Custom definition") : $t("Built-in definition")}</span><h3>{creating ? $t("Create {0} mode", draft.kind === 'ProcessMode' ? 'Process' : 'TUN') : draft.name}</h3></div>
              {#if !creating && active}<span class:custom={active.editable} class="source-badge">{active.source}</span>{/if}
            </div>

            <div class="mode-fields">
              <label class="mode-field"><span><strong>{$t("Mode name")}</strong><small>{$t("Stored as the localized mode remark.")}</small></span><input maxlength="80" bind:value={draft.name} disabled={!editable} /></label>
              {#if !creating && active}<label class="mode-field"><span><strong>{$t("Source file")}</strong><small>{$t("Path is controlled by Netch.")}</small></span><input value={active.fileName} readonly /></label>{/if}

              {#if draft.kind === 'ProcessMode'}
                <div class="mode-option-grid">
                  {#each [
                    ['filterTcp', 'TCP traffic'], ['filterUdp', 'UDP traffic'], ['filterIcmp', 'ICMP traffic'],
                    ['filterDns', 'DNS filtering'], ['includeChildProcesses', $t("Child processes")],
                    ['proxyDns', $t("Proxy DNS")], ['handleOnlyDns', $t("Handled DNS only")]
                  ] as option}
                    <label><span>{option[1]}</span><select value={switchText(draft[option[0] as keyof ModeWrite] as InheritedSwitch)} disabled={!editable} onchange={(event) => draft![option[0] as keyof ModeWrite] = switchValue(event.currentTarget.value) as never}><option value="inherit">{$t("Inherit global")}</option><option value="on">{$t("Enabled")}</option><option value="off">{$t("Disabled")}</option></select></label>
                  {/each}
                  <label><span>{$t("Loopback traffic")}</span><input type="checkbox" bind:checked={draft.filterLoopback} disabled={!editable} /></label>
                  <label><span>{$t("Intranet traffic")}</span><input type="checkbox" bind:checked={draft.filterIntranet} disabled={!editable} /></label>
                </div>
                <div class="mode-pair">
                  <label class="mode-field vertical"><span><strong>{$t("DNS endpoint")}</strong><small>{$t("Empty inherits the compatible default.")}</small></span><input bind:value={draft.dnsHost} placeholder="1.1.1.1:53" disabled={!editable} /></label>
                  <label class="mode-field vertical"><span><strong>{$t("ICMP delay")}</strong><small>{$t("0–60000 ms; empty inherits.")}</small></span><input type="number" min="0" max="60000" bind:value={draft.icmpDelayMs} disabled={!editable} /></label>
                </div>
              {/if}

              <div class="mode-rule-grid">
                <label class="mode-field vertical"><span><strong>{$t("Handle rules")}</strong><small>{$t("One legacy route or process rule per line.")}</small></span><textarea rows="8" bind:value={handleText} disabled={!editable} spellcheck="false"></textarea></label>
                <label class="mode-field vertical"><span><strong>{$t("Bypass rules")}</strong><small>{$t("One exclusion rule per line.")}</small></span><textarea rows="8" bind:value={bypassText} disabled={!editable} spellcheck="false"></textarea></label>
              </div>
            </div>

            <footer class="mode-editor-footer">
              {!creating && !active?.editable ? $t("Built-in text and JSON definitions are protected.") : $t("Changes preserve the original mode JSON format.")}
              <span></span>
              {#if !creating && active?.editable}
                <button type="button" class="button ghost danger-button" disabled={busy || !canEdit} onclick={() => deleteArmed ? onDelete(active!.id) : deleteArmed = true}>{deleteArmed ? $t("Confirm delete") : $t("Delete")}</button>
              {/if}
              <button class="button primary" disabled={busy || !editable || !draft.name.trim()}>{busy ? $t("Saving…") : creating ? $t("Create mode") : $t("Save mode")}</button>
            </footer>
          </form>
        {/if}
      </main>
    </div>

    <footer class="modal-footer modes-bottom-bar">
      <span>{details.length} {$t("definitions · selected changes apply on the next connection")}</span>
      {#if activeId && activeId !== selectedModeId}<button class="button secondary" disabled={busy || !canEdit} onclick={() => onSelect(activeId!)}>{$t("Use selected mode")}</button>{/if}
      <button class="button secondary" onclick={onClose}>{$t("Close")}</button>
    </footer>
  </div>
</div>
