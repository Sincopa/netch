<script lang="ts">
  import { t } from "../../lib/i18n";
  import type { Snippet } from 'svelte';

  let { label, children }: { label: string; children: Snippet } = $props();
</script>

<span class="tooltip-host">
  {@render children()}
  <span class="tooltip-bubble" role="tooltip">{$t(label)}</span>
</span>
