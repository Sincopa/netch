<script lang="ts">
  import { t } from '../lib/i18n';
  import Icon from './ui/Icon.svelte';
  import { dialogFocus } from '../lib/dialogFocus';
  let { onClose, onSubscriptions }: { onClose: () => void; onSubscriptions: () => void } = $props();
</script>

<div class="modal-backdrop" role="presentation" onclick={event => event.target === event.currentTarget && onClose()}>
  <div class="modal help-modal" role="dialog" aria-modal="true" aria-labelledby="help-title" use:dialogFocus>
    <header class="modal-header"><div><span class="eyebrow">{$t('Getting started')}</span><h2 id="help-title">{$t('Three steps. You’re online.')}</h2></div><button class="icon-button" aria-label={$t('Close')} onclick={onClose}><Icon name="x" size={20} /></button></header>
    <div class="help-body">
      <p class="help-intro">{$t('Netch connects you through your VPN provider. You only need their subscription link or a server link.')}</p>
      <ol class="help-steps">
        <li><span>01</span><div><h3>{$t('Add your subscription')}</h3><p>{$t('Copy the subscription link from your provider, open Subscriptions and paste it. Your servers will appear automatically.')}</p></div></li>
        <li><span>02</span><div><h3>{$t('Choose a server and apps')}</h3><p>{$t('Select a server from the list. Choose Whole computer for the simplest setup, or select just the apps you need.')}</p></div></li>
        <li><span>03</span><div><h3>{$t('Press Connect')}</h3><p>{$t('Wait until you see Connected. To return to your regular connection, press Disconnect.')}</p></div></li>
      </ol>
      <details class="help-faq"><summary>{$t('Something isn’t working?')}</summary><p>{$t('Try another server, or update your subscription. If Netch asks for a component, open Diagnostics and follow the instructions. The connection log can help explain an error.')}</p></details>
    </div>
    <footer class="modal-footer"><button class="button secondary" onclick={onClose}>{$t('Got it')}</button><button class="button primary" onclick={onSubscriptions}>{$t('Open subscriptions')}<Icon name="arrow-right" size={17} /></button></footer>
  </div>
</div>
