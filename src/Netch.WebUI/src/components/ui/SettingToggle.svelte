<script lang="ts">
  import { t } from "../../lib/i18n";
  let {
    checked = $bindable(),
    title,
    description,
    danger = false,
    disabled = false
  }: {
    checked: boolean;
    title: string;
    description: string;
    danger?: boolean;
    disabled?: boolean;
  } = $props();
</script>

<label class:danger class="setting-toggle">
  <span><strong>{$t(title)}</strong><small>{$t(description)}</small></span>
  <input type="checkbox" bind:checked {disabled} />
</label>
