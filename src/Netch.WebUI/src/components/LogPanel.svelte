<script lang="ts">
  import { t } from "../lib/i18n";
  import Icon from './ui/Icon.svelte';
  import type { LogEntry } from '../lib/types';

  let { logs, onClear, onCopy }: { logs: LogEntry[]; onClear: () => void; onCopy: () => void } = $props();
  let logBody: HTMLDivElement;

  $effect(() => {
    logs.length;
    requestAnimationFrame(() => {
      if (logBody) logBody.scrollTop = logBody.scrollHeight;
    });
  });
</script>

<section class="panel log-panel">
  <div class="section-title log-heading">
    <span><Icon name="terminal" size={15} /> {$t("Live log")} <b>{logs.length}</b></span>
    <div>
      <button class="text-button" onclick={onCopy} disabled={logs.length === 0}><Icon name="clipboard" size={13} />{$t("Copy")}</button>
      <button class="text-button danger" onclick={onClear} disabled={logs.length === 0}><Icon name="trash" size={13} />{$t("Clear")}</button>
    </div>
  </div>
  <div class="log-body" bind:this={logBody} aria-live="polite">
    {#each logs as entry (entry.id)}
      <div class="log-entry">
        <time>{new Date(entry.timestamp).toLocaleTimeString([], { hour12: false })}</time>
        <span class="log-level {entry.level.toLowerCase()}">{entry.level}</span>
        <p>{$t(entry.message)}</p>
      </div>
    {:else}
      <div class="empty log-empty"><Icon name="terminal" size={21} /><span>{$t("Logs will appear here")}</span></div>
    {/each}
  </div>
</section>
