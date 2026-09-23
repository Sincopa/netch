<script lang="ts">
  import { bridge } from '../lib/bridge';
  let { onScreen, onReset }: { onScreen: (screen: string) => void; onReset: () => Promise<void> } = $props();
  let scenario = $state('disconnected');
  async function reset() { await bridge.invoke('demo.reset'); scenario = 'disconnected'; await onReset(); }
</script>

<div class="dev-toolbar">
  <strong>DEV · Демо</strong><span>Все действия имитируются</span>
  <select aria-label="Демо-состояние" bind:value={scenario} onchange={() => bridge.invoke('demo.scenario', { scenario })}>
    <option value="disconnected">Не подключено</option><option value="connecting">Подключение</option>
    <option value="connected">Подключено</option><option value="disconnecting">Отключение</option>
    <option value="error">Ошибка подключения</option><option value="driver-error">Нет драйвера</option><option value="empty">Нет серверов</option>
  </select>
  <select aria-label="Открыть демо-экран" value="" onchange={event => { onScreen(event.currentTarget.value); event.currentTarget.value = ''; }}>
    <option value="">Открыть окно…</option>
    <option value="settings">Настройки</option><option value="subscriptions">Подписки</option>
    <option value="processes">Приложения</option><option value="import">Импорт</option>
    <option value="server">Редактор сервера</option><option value="profiles">Быстрые профили</option>
    <option value="modes">Расширенные режимы</option><option value="diagnostics">Диагностика</option>
    <option value="updates">Обновления</option><option value="logs">Журнал</option>
  </select>
  <button class="text-button" onclick={reset}>Сбросить демо</button>
</div>
