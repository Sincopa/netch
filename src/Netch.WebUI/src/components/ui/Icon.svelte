<script lang="ts">
  import icons from '../../lib/materialIcons.json';
  let { name, size = 16, strokeWidth = 1.8, class: className = '' }: {
    name: string; size?: number; strokeWidth?: number; class?: string;
  } = $props();
  let symbol = $derived((icons as Record<string, string>)[name] ?? 'help');
</script>

{#if name === 'clock' || name === 'arrow-right'}
  <svg class={className} width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width={strokeWidth} stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    {#if name === 'clock'}<circle cx="12" cy="12" r="9" /><path d="M12 7v5l3 2" />{:else}<path d="M5 12h14m-6-6 6 6-6 6" />{/if}
  </svg>
{:else}
  <span class={`material-symbols ${className}`} style:font-size={`${size}px`} style:width={`${size}px`} style:height={`${size}px`} aria-hidden="true">{symbol}</span>
{/if}
