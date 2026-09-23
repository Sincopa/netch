<script lang="ts">
  import { t } from '../lib/i18n';
  import Icon from './ui/Icon.svelte';
  let { onSubscriptions, onProfiles, onSettings, onDiagnostics, onUpdates, onHelp, updateAvailable, version }: {
    onSubscriptions: () => void; onProfiles: () => void; onSettings: () => void;
    onDiagnostics: () => void; onUpdates: () => void; onHelp: () => void;
    updateAvailable: boolean; version?: string;
  } = $props();
</script>

<nav class="navigation-rail" aria-label={$t('Main navigation')}>
  <div class="nav-group">
    <span class="nav-caption">{$t('Your space')}</span>
    <a class="nav-item active" href="#connection" aria-current="page" title={$t('Connection')}><Icon name="shield" size={21} /><span>{$t('Connection')}</span><i></i></a>
    <button class="nav-item" title={$t('Subscriptions')} onclick={onSubscriptions}><Icon name="download" size={21} /><span>{$t('Subscriptions')}</span></button>
    <button class="nav-item" title={$t('Saved profiles')} onclick={onProfiles}><Icon name="bookmark" size={21} /><span>{$t('Saved profiles')}</span></button>
    <button class="nav-item" title={$t('Settings')} onclick={onSettings}><Icon name="sliders" size={21} /><span>{$t('Settings')}</span></button>
  </div>
  <div class="nav-bottom">
    <button class="nav-help" title={$t('Getting started')} onclick={onHelp}><span class="help-symbol">?</span><strong>{$t('Let’s get you connected')}</strong><small>{$t('A simple guide, step by step')} <span>↗</span></small></button>
    <button class="nav-item subtle" title={$t('Open diagnostics')} aria-label={$t('Open diagnostics')} onclick={onDiagnostics}><Icon name="activity" size={19} /><span>{$t('Diagnostics')}</span></button>
    <button class="nav-item subtle" title={$t('Open updates')} aria-label={$t('Open updates')} onclick={onUpdates}><Icon name="refresh" size={19} /><span>{$t('Updates')}</span>{#if updateAvailable}<i class="update-dot"></i>{/if}</button>
    <div class="nav-version"><span>Netch {version ?? ''}</span><span>↗</span></div>
  </div>
</nav>
