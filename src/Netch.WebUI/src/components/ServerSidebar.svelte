<script lang="ts">
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import CountryFlag from './ui/CountryFlag.svelte';
  import { serverLabel } from '../lib/serverLabel';
  import type { Server } from '../lib/types';

  let { onSubscriptions, servers, selectedId, loading, disabled, mutationDisabled, favoriteDisabled, pingingIds = [], onSelect, onPingAll, onPingOne, onFavorite, onCreate, onImport, onEdit, onDelete }: {
    onSubscriptions: () => void;
    servers: Server[];
    selectedId: string | null;
    loading: boolean;
    disabled: boolean;
    mutationDisabled: boolean;
    favoriteDisabled: boolean;
    pingingIds?: string[];
    onSelect: (id: string) => void;
    onPingAll: () => void;
    onPingOne: (id: string) => void;
    onFavorite: (server: Server) => void;
    onCreate: () => void;
    onImport: () => void;
    onEdit: (server: Server) => void;
    onDelete: (server: Server) => void;
  } = $props();

  let query = $state('');
  let group = $state('');
  let sort = $state<'original' | 'favorites' | 'latency' | 'name'>('original');
  let favoritesOnly = $state(false);
  let menuId = $state<string | null>(null);
  let groups = $derived(Array.from(new Set(servers.map((server) => server.group))).sort());
  let filtered = $derived.by(() => {
    const order = new Map(servers.map((server, index) => [server.id, index]));
    const values = servers.filter((server) =>
      (group === '' || server.group === group) &&
      (!favoritesOnly || server.isFavorite) &&
      `${server.name} ${server.group} ${server.hostname} ${server.protocol}`.toLowerCase().includes(query.trim().toLowerCase())
    );
    return [...values].sort((left, right) => {
      if (sort === 'favorites' && left.isFavorite !== right.isFavorite) return left.isFavorite ? -1 : 1;
      if (sort === 'latency') {
        const result = (left.latency ?? Number.MAX_SAFE_INTEGER) - (right.latency ?? Number.MAX_SAFE_INTEGER);
        if (result) return result;
      }
      if (sort === 'name') {
        const result = left.name.localeCompare(right.name, undefined, { sensitivity: 'base' });
        if (result) return result;
      }
      return (order.get(left.id) ?? 0) - (order.get(right.id) ?? 0);
    });
  });

  $effect(() => {
    if (group !== '' && !groups.includes(group)) group = '';
  });

  function latencyTone(latency: number | null) {
    if (latency == null || !Number.isFinite(latency) || latency < 0) return 'unknown';
    if (latency < 80) return 'good';
    if (latency < 180) return 'fair';
    return 'poor';
  }



  function displayGroup(value: string) {
    return value === 'NONE' ? $t("Local") : value;
  }



  function run(action: () => void) {
    menuId = null;
    action();
  }

  function navigate(event: KeyboardEvent) {
    if (disabled || !['ArrowDown', 'ArrowUp', 'Home', 'End'].includes(event.key) || filtered.length === 0) return;
    event.preventDefault();
    const current = filtered.findIndex((server) => server.id === selectedId);
    const index = event.key === 'Home' ? 0 : event.key === 'End' ? filtered.length - 1
      : event.key === 'ArrowDown' ? Math.min(current + 1, filtered.length - 1)
      : Math.max(current < 0 ? 0 : current - 1, 0);
    onSelect(filtered[index].id);
  }
</script>

<svelte:window onpointerdown={(event) => {
  if (!(event.target instanceof Element) || !event.target.closest('.server-action-host')) menuId = null;
}} />

<aside class="server-rail">
  <div class="rail-heading">
    <div><h2>{$t("Servers")} <b>{servers.length}</b></h2></div>
    <div class="rail-heading-actions">
      <button class="icon-button bordered" title={$t("Add server")} aria-label={$t("Add server")} onclick={onCreate} disabled={loading || mutationDisabled}><Icon name="plus" size={15} /></button>
      <button class="icon-button bordered" title={$t("Test all servers")} aria-label={$t("Test all servers")} onclick={onPingAll} disabled={loading || disabled || pingingIds.length > 0 || servers.length === 0}><Icon name={pingingIds.length ? 'loader' : 'activity'} class={pingingIds.length ? 'spin' : ''} size={18} /></button>
    </div>
  </div>

  <p class="rail-intro">{$t('Pick a location. Lower latency means a quicker response.')}</p>
  <div class="server-tools">
    <label class="search-field">
      <Icon name="search" size={15} />
      <input bind:value={query} placeholder={$t("Search servers")} aria-label={$t("Search servers")} />
      {#if query}<button aria-label={$t("Clear search")} onclick={() => query = ''}>×</button>{/if}
    </label>
    <div class="server-filter-row">
      <label class="group-filter" title={$t("Filter server groups")}>
        <Icon name="filter" size={13} />
        <select bind:value={group} aria-label={$t("Filter server groups")}><option value="">{$t("All")}</option>{#each groups as value}<option value={value}>{displayGroup(value)}</option>{/each}</select>
      </label>
      <label class="group-filter sort-filter" title={$t("Sort servers")}>
        <Icon name="sort" size={13} />
        <select bind:value={sort} aria-label={$t("Sort servers")}>
          <option value="original">{$t("Source order")}</option>
          <option value="favorites">{$t("Favorites first")}</option>
          <option value="latency">{$t("Lowest latency")}</option>
          <option value="name">{$t("Name")}</option>
        </select>
      </label>
      <button class:active={favoritesOnly} class="favorite-filter" title={$t("Show favorite servers only")} aria-label={$t("Show favorite servers only")} aria-pressed={favoritesOnly} onclick={() => favoritesOnly = !favoritesOnly}><Icon name="bookmark" size={14} /></button>
    </div>
  </div>

  <div class="server-list" role="listbox" aria-label={$t("Servers")} tabindex="0" onkeydown={navigate}>
    {#if loading}
      {#each Array(8) as _}
        <div class="server-row server-skeleton" aria-hidden="true"><span></span><span><i></i><i></i></span></div>
      {/each}
    {:else}{#each filtered as server (server.id)}
      {@const validLatency = typeof server.latency === 'number' && Number.isFinite(server.latency) && server.latency >= 0}
      <div class:active={selectedId === server.id} class="server-row" role="option" aria-selected={selectedId === server.id} tabindex="-1">
        <button class="server-select" onclick={() => onSelect(server.id)} disabled={disabled}>
          <span class="server-avatar"><CountryFlag code={server.countryCode} automatic={server.isAutomatic ?? false} /></span>
          <span class="server-copy"><strong title={serverLabel(server)}>{serverLabel(server)}</strong><small>{displayGroup(server.group)} · {server.isAutomatic ? $t('Auto') : server.protocol}</small></span>
          <span title={server.latencyMethod === 'http' ? $t('HTTP through this server') : $t('Direct TCP/ICMP; not VPN latency')} class="latency {latencyTone(server.latency)}">{#if pingingIds.includes(server.id)}<span role="status" aria-label={$t('Testing latency')}><Icon name="loader" class="spin" size={18} /></span>{:else}{#if server.isFavorite}<Icon name="bookmark" size={10} />{/if}<i></i>{validLatency ? $t("{0} ms", server.latency) : '—'}{/if}</span>
        </button>
        <div class="server-action-host">
          <button class="row-menu-button" class:open={menuId === server.id} aria-label={$t("Actions for {0}", server.name)} aria-expanded={menuId === server.id} onclick={() => menuId = menuId === server.id ? null : server.id}><Icon name="more" size={15} /></button>
          {#if menuId === server.id}
            <div class="server-action-menu" role="menu">
              <button role="menuitem" disabled={favoriteDisabled} onclick={() => run(() => onFavorite(server))}><Icon name="bookmark" size={14} /> {server.isFavorite ? $t("Remove favorite") : $t("Add to favorites")}</button>
              <button role="menuitem" disabled={disabled || pingingIds.includes(server.id)} onclick={() => run(() => onPingOne(server.id))}><Icon name="activity" size={14} /> {$t("Test latency")}</button>
              <button role="menuitem" disabled={mutationDisabled || server.managedBySubscription} onclick={() => run(() => onEdit(server))}><Icon name="edit" size={14} /> {$t("Edit details")}</button>
              <button role="menuitem" class="danger" disabled={mutationDisabled || server.managedBySubscription} onclick={() => run(() => onDelete(server))}><Icon name="trash" size={14} /> {$t("Delete server")}</button>
              {#if server.managedBySubscription}<span><Icon name="lock" size={12} /> {$t("Managed by subscription")}</span>{/if}
            </div>
          {/if}
        </div>
      </div>
    {:else}
      <div class="empty compact">
        <Icon name="server" size={20} />
        <span>{servers.length ? $t("No matching servers") : $t("No servers configured")}</span>
        {#if !servers.length}<span>{$t("Add a subscription or import a server link below.")}</span>{:else}<button class="button secondary" onclick={() => { query = ''; group = ''; favoritesOnly = false; }}>{$t('Reset filters')}</button>{/if}
      </div>
    {/each}{/if}
  </div>

  <div class="rail-bottom"><button class="button secondary subscription-entry" onclick={onSubscriptions} disabled={loading}><Icon name="plus" size={18} /> {$t("Add subscription")}</button><div class="rail-foot"><span>{filtered.length} {$t("servers")}</span><button class="text-button" onclick={onImport} disabled={loading || mutationDisabled}>{$t("Import links")}</button></div></div>
</aside>
