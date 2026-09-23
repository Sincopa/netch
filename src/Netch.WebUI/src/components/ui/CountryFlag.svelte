<script lang="ts">
  import { t } from '../../lib/i18n';
  import Icon from './Icon.svelte';
  const flags = import.meta.glob('/node_modules/flag-icons/flags/4x3/*.svg', { eager: true, query: '?url', import: 'default' }) as Record<string, string>;
  let { code, automatic = false }: { code: string | null | undefined; automatic?: boolean } = $props();
  let source = $derived(code && /^[a-z]{2}$/i.test(code) ? flags[`/node_modules/flag-icons/flags/4x3/${code.toLowerCase()}.svg`] : undefined);
</script>

{#if automatic}
  <span title={$t('Automatic server selection')} aria-label={$t('Automatic server selection')}><Icon name="refresh" size={20} /></span>
{:else if source}
  <img src={source} alt={code ?? ''} width="28" height="21" style="object-fit: contain" />
{:else}
  <span title={$t('Country unknown')} aria-label={$t('Country unknown')}><Icon name="globe" size={20} /></span>
{/if}
