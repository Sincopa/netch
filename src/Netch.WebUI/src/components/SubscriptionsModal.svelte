<script lang="ts">
  import { dialogFocus } from '../lib/dialogFocus';
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { Subscription, SubscriptionInput } from '../lib/types';

  let {
    subscriptions,
    busy,
    mutationsDisabled,
    onClose,
    onSave,
    onDelete,
    onRefresh,
    onRefreshAll
  }: {
    subscriptions: Subscription[];
    busy: boolean;
    mutationsDisabled: boolean;
    onClose: () => void;
    onSave: (value: SubscriptionInput) => void;
    onDelete: (id: string) => void;
    onRefresh: (id: string) => void;
    onRefreshAll: () => void;
  } = $props();

  let editing = $state<string | null>(null);
  let remark = $state('');
  let url = $state('');
  let userAgent = $state('');
  let enabled = $state(true);
  let deleteTarget = $state<Subscription | null>(null);

  function edit(item?: Subscription) {
    editing = item?.id ?? null;
    remark = item?.remark ?? '';
    url = item?.url ?? '';
    userAgent = item?.userAgent ?? '';
    enabled = item?.enabled ?? true;
  }

  function submit() {
    onSave({ id: editing ?? undefined, remark, url, userAgent, enabled });
  }

  function urlHost(value: string) {
    try { return new URL(value).host; }
    catch { return $t("Invalid URL"); }
  }

  function updatedLabel(value: string | null) {
    if (!value) return $t("Not updated this session");
    return $t("Updated {0}", new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit' }).format(new Date(value)));
  }
</script>

<div class="modal-backdrop" role="presentation" onclick={(event) => event.target === event.currentTarget && onClose()}>
  <div class="modal subscriptions-modal" role="dialog" use:dialogFocus aria-modal="true" aria-labelledby="subscriptions-title">
    <header class="modal-header">
      <div><span class="eyebrow">{$t("Server sources")}</span><h2 id="subscriptions-title">{$t("Subscriptions")}</h2></div>
      <div class="modal-header-actions">
        <button class="toolbar-button" disabled={busy || mutationsDisabled || subscriptions.length === 0} onclick={onRefreshAll}><Icon name="refresh" class={busy ? 'spin' : ''} size={14} />{$t("Update all")}</button>
        <button class="icon-button" aria-label={$t("Close")} onclick={onClose}><Icon name="x" size={18} /></button>
      </div>
    </header>

    <div class="subscription-layout">
      <div class="subscription-list">
        <button class="new-subscription" disabled={busy || mutationsDisabled} onclick={() => edit()}><Icon name="plus" size={15} /> {$t("Add subscription")}</button>
        {#each subscriptions as item (item.id)}
          <div class:active={editing === item.id} class="subscription-row">
            <button class="subscription-main" onclick={() => edit(item)}>
              <span class="subscription-state"><Icon name="radio" size={15} /><i class:enabled={item.enabled}></i></span>
              <span class="subscription-copy">
                <span><strong>{item.remark}</strong><em class:success={item.refreshStatus === 'success'} class:error={item.refreshStatus === 'error'}>{item.serverCount} {$t("servers")}</em></span>
                <small>{urlHost(item.url)} · {updatedLabel(item.lastUpdatedAt)}</small>
                {#if item.lastError}<small class="subscription-error">{item.lastError}</small>{/if}
              </span>
            </button>
            <button class="row-icon" aria-label={$t("Refresh {0}", item.remark)} title={$t("Refresh")} disabled={busy || mutationsDisabled} onclick={() => onRefresh(item.id)}><Icon name={item.refreshStatus === 'refreshing' ? 'loader' : 'download'} class={item.refreshStatus === 'refreshing' ? 'spin' : ''} size={14} /></button>
          </div>
        {:else}
          <div class="empty compact"><Icon name="download" size={19} /><span>{$t("No subscriptions")}</span></div>
        {/each}
      </div>

      <form class="subscription-form" onsubmit={(event) => { event.preventDefault(); submit(); }}>
        <div class="form-heading"><h3>{editing ? $t("Edit source") : $t('Add your subscription')}</h3></div>
        <p class="subscription-explainer">{$t('Paste the link from your VPN provider. Netch will download the available servers for you.')}</p>
        {#if mutationsDisabled}<div class="settings-lock"><Icon name="alert" size={15} /><span>{$t("Disconnect to change or refresh subscriptions.")}</span></div>{/if}
        <label><span>{$t("Name")}</span><input bind:value={remark} required maxlength="64" placeholder={$t("My subscription")} disabled={mutationsDisabled} /></label>
        <label><span>{$t("Subscription URL")}</span><input bind:value={url} required type="url" placeholder="https://example.com/subscription" disabled={mutationsDisabled} /></label>
        <details class="subscription-advanced"><summary>{$t('Advanced options')} · {$t('optional')}</summary>
        <label><span>{$t("User agent")} <small>{$t("optional")}</small></span><input bind:value={userAgent} list="subscription-user-agents" placeholder="v2ray" disabled={mutationsDisabled} /></label>
        <datalist id="subscription-user-agents">{#each ['v2ray', 'v2rayNG', 'v2rayN', 'Happ', 'Netch'] as agent}<option value={agent}></option>{/each}</datalist>
        <p class="subscription-agent-help">{$t('Default: v2ray. Examples: v2ray, v2rayNG, v2rayN, Happ, Netch. You can enter any value; accepted agents depend on your provider. Netch reads Xray JSON and share links. Clash/mihomo YAML is not supported.')}</p>
        </details>
        <label class="toggle-row">
          <span><strong>{$t("Enabled")}</strong><small>{$t("Include this source during refresh")}</small></span>
          <input type="checkbox" bind:checked={enabled} disabled={mutationsDisabled} />
        </label>
        <div class="form-actions">
          {#if editing}<button type="button" class="button danger-ghost" disabled={busy || mutationsDisabled} onclick={() => deleteTarget = subscriptions.find((item) => item.id === editing) ?? null}><Icon name="trash" size={15} />{$t("Delete")}</button>{/if}
          <span></span>
          <button type="button" class="button secondary" onclick={onClose}>{$t("Cancel")}</button>
          <button class="button primary" disabled={busy || mutationsDisabled || !remark.trim() || !url.trim()}><Icon name={busy ? "loader" : "check"} class={busy ? "spin" : ""} size={15} />{busy ? $t("Saving…") : $t("Save")}</button>
        </div>
      </form>
    </div>
    {#if deleteTarget}
      <div class="confirm-layer">
        <div class="confirm-card" role="alertdialog" tabindex="-1" use:dialogFocus aria-modal="true" aria-label={$t("Delete subscription")} onkeydown={(event) => { if (event.key === "Escape") { event.stopPropagation(); deleteTarget = null; } }}><span class="confirm-icon"><Icon name="trash" size={18} /></span><h3>{$t("Delete")} {deleteTarget.remark}?</h3><p>{$t("The subscription and all servers currently managed by it will be removed.")}</p><div><button class="button secondary" disabled={busy} onclick={() => deleteTarget = null}>{$t("Cancel")}</button><button class="button danger" disabled={busy} onclick={() => { onDelete(deleteTarget!.id); deleteTarget = null; }}>{$t("Delete subscription")}</button></div></div>
      </div>
    {/if}
  </div>
</div>
