import type { Bootstrap, ConnectionStatus, ModeDetails, ModeWrite, ServerDetailsWrite, SettingsData, SubscriptionInput } from '../types';
import { demoBootstrap, demoDiagnostics, demoModeDetails, demoProcesses, demoSettings, demoUpdate } from './fixtures';

const storageKey = 'netch-browser-demo-v1';
const pause = (ms: number) => new Promise<void>(resolve => setTimeout(resolve, ms));
type Emit = (event: string, payload: unknown) => void;

export function createMockBridge(publish: Emit) {
  const emit: Emit = (event, payload) => publish(event, structuredClone(payload));
  let data = demoBootstrap();
  let settings = demoSettings();
  let details = demoModeDetails();
  let diagnostics = demoDiagnostics();
  let update = demoUpdate();
  let attempt = 0;
  let trafficTimer: ReturnType<typeof setInterval> | undefined;
  const selections: Record<string, { modeId: string | null; processes: string[] }> = {};
  try {
    const saved = JSON.parse(localStorage.getItem(storageKey) ?? 'null');
    if (saved?.version === 1) {
      data = saved.data; settings = saved.settings; details = saved.details;
      Object.assign(selections, saved.selections);
      data.connection = demoBootstrap().connection;
    }
  } catch { /* A fresh preview also works with storage disabled. */ }
  const persist = () => {
    try { localStorage.setItem(storageKey, JSON.stringify({ version: 1, data, settings, details, selections })); } catch { /* Optional persistence. */ }
  };
  const group = () => {
    const name = data.servers.find(s => s.id === data.selectedServerId)?.group;
    return data.subscriptions.find(s => s.remark === name)?.id ?? 'NONE';
  };
  const remember = () => selections[group()] = { modeId: data.selectedModeId, processes: [...data.routing.processes] };
  const selection = () => {
    data.routing.mode = data.selectedModeId === 'apps' ? 'selected' : 'all';
    emit('selection.changed', { serverId: data.selectedServerId, modeId: data.selectedModeId });
    emit('routing.changed', data.routing);
  };
  const log = (message: string, level: 'INFO' | 'ERROR' = 'INFO') => {
    const entry = { id: Date.now(), timestamp: new Date().toISOString(), level, message };
    data.logs = [...data.logs.slice(-399), entry]; emit('logs.added', entry);
  };
  const setConnection = (status: ConnectionStatus) => {
    clearInterval(trafficTimer);
    data.connection = { status, message: status === 'error' ? 'Демо: сервер не отвечает. Выберите другой или попробуйте снова.' : status === 'connected' ? 'Демо: подключение установлено' : status === 'disconnected' ? 'Ready' : 'Демо: подключение…',
      serverId: data.selectedServerId, modeId: data.selectedModeId, connectedAt: status === 'connected' ? new Date().toISOString() : null };
    emit('connection.stateChanged', data.connection);
    let seconds = 0;
    const traffic = () => emit('traffic.updated', { receivedBytes: seconds * 153600, sentBytes: seconds * 15360,
      received: `${(seconds * .15).toFixed(2)} MiB`, sent: `${seconds * 15} KiB`, durationSeconds: seconds, connectedAt: data.connection.connectedAt });
    traffic();
    if (status === 'connected') trafficTimer = setInterval(() => { seconds++; traffic(); }, 1000);
  };
  const catalog = (importedCount = 0) => ({ servers: data.servers, selectedServerId: data.selectedServerId, importedCount });
  const modes = () => ({ modes: data.modes, details, selectedModeId: data.selectedModeId });
  const serversChanged = () => {
    if (!data.servers.some(s => s.id === data.selectedServerId)) data.selectedServerId = data.servers[0]?.id ?? null;
    emit('servers.changed', true); selection(); persist();
  };

  async function invoke(method: string, params: object = {}): Promise<unknown> {
    // Each branch mirrors the desktop RPC shape; all side effects stay in this preview.
    const p = params as Record<string, any>;
    switch (method) {
      case 'app.bootstrap': return { ...data, language: settings.settings.general.language };
      case 'connection.getState': return data.connection;
      case 'connection.connect':
      case 'connection.reconnect': {
        const current = ++attempt;
        if (p.serverId) data.selectedServerId = p.serverId;
        if (p.modeId) data.selectedModeId = p.modeId;
        setConnection(method.endsWith('reconnect') ? 'reconnecting' : 'connecting');
        await pause(1200);
        if (current === attempt) { setConnection('connected'); log('Демо: VPN подключён'); }
        return data.connection;
      }
      case 'connection.cancel': ++attempt; setConnection('disconnected'); return data.connection;
      case 'connection.disconnect': ++attempt; setConnection('disconnecting'); await pause(450); setConnection('disconnected'); log('Демо: VPN отключён'); return data.connection;
      case 'servers.list': return data.servers;
      case 'servers.select':
        remember(); data.selectedServerId = p.id;
        data.selectedModeId = selections[group()]?.modeId ?? 'whole';
        data.routing.processes = selections[group()]?.processes ?? [];
        selection(); persist(); return data.servers;
      case 'servers.ping':
        for (const [index, server] of data.servers.filter(s => !p.serverIds || p.serverIds.includes(s.id)).entries()) {
          await pause(100); server.latency = index === 5 ? null : 28 + index * 23; server.latencyMethod = 'http';
          emit('servers.latencyChanged', { serverId: server.id, latency: server.latency, latencyMethod: 'http' });
        }
        return data.servers;
      case 'servers.setFavorite': { const server = data.servers.find(s => s.id === p.id); if (server) server.isFavorite = p.isFavorite; persist(); return catalog(); }
      case 'servers.delete':
        data.servers = data.servers.filter(s => s.id !== p.id);
        if (data.selectedServerId === p.id) data.selectedServerId = data.servers[0]?.id ?? null;
        serversChanged(); return catalog();
      case 'servers.details': return {
        servers: data.servers.map(s => ({ ...s, values: { transferProtocol: 'tcp', tlsSecureType: 'tls', encryptMethod: 'none' }, secretConfigured: { userId: true } })),
        protocols: ['VLESS', 'VMess', 'Trojan', 'SS', 'SSR', 'SOCKS', 'SSH', 'WireGuard'],
        options: {
          'ss.encryptMethod': ['aes-128-gcm', 'aes-256-gcm', 'chacha20-ietf-poly1305'],
          'ssr.encryptMethod': ['aes-256-cfb', 'aes-128-cfb', 'chacha20-ietf'],
          'ssr.protocol': ['origin', 'auth_sha1_v4', 'auth_aes128_md5'], 'ssr.obfs': ['plain', 'http_simple', 'tls1.2_ticket_auth'],
          'socks.version': ['5', '4'], 'vmess.encryptMethod': ['auto', 'aes-128-gcm', 'chacha20-poly1305', 'none'],
          'v2.transferProtocol': ['tcp', 'ws', 'grpc', 'http', 'xhttp', 'kcp', 'quic'],
          'v2.packetEncoding': ['xudp', 'packet', 'none'], 'v2.fakeType': ['none', 'http', 'srtp', 'utp', 'wechat-video'],
          'v2.quicSecure': ['none', 'aes-128-gcm', 'chacha20-poly1305'], 'boolean.inherit': ['', 'true', 'false'],
          'vless.tls': ['none', 'tls', 'reality'], 'vmess.tls': ['none', 'tls'],
          'vless.fingerprint': ['', 'chrome', 'firefox', 'safari', 'randomized']
        }
      };
      case 'servers.saveDetails': {
        const value = p as unknown as ServerDetailsWrite;
        const server = { id: value.id ?? `local-${Date.now()}`, name: value.name, group: value.group || 'NONE', protocol: value.protocol,
          hostname: value.hostname, port: value.port, latency: null, managedBySubscription: false, isFavorite: false, countryCode: null };
        data.servers = [...data.servers.filter(s => s.id !== server.id), server]; data.selectedServerId = server.id;
        serversChanged(); return catalog(value.id ? 0 : 1);
      }
      case 'servers.import': {
        const lines = String(p.text).split(/\s+/).filter(Boolean);
        lines.forEach((_, i) => data.servers.push({ id: `import-${Date.now()}-${i}`, name: `Демо импорт ${i + 1}`, group: p.group || 'NONE', protocol: 'VLESS', hostname: 'import.example.invalid', port: 443, latency: null, managedBySubscription: false, isFavorite: false, countryCode: 'PL' }));
        data.selectedServerId ??= data.servers[0]?.id ?? null; serversChanged(); return catalog(lines.length);
      }
      case 'modes.list': return data.modes;
      case 'modes.details': return modes();
      case 'modes.select': data.selectedModeId = p.id; remember(); selection(); persist(); return data.modes;
      case 'modes.save': {
        const value = p as unknown as ModeWrite;
        const id = value.id ?? `mode-${Date.now()}`;
        details = [...details.filter(m => m.id !== id), { ...value, id, source: 'custom', editable: true, fileName: `${id}.json`, shareArgument: null } as ModeDetails];
        data.modes = [...data.modes.filter(m => m.id !== id), { id, name: value.name, kind: value.kind, supportsProcessSelection: value.kind === 'ProcessMode' }];
        emit('modes.changed', true); persist(); return modes();
      }
      case 'modes.delete': details = details.filter(m => m.id !== p.id); data.modes = data.modes.filter(m => m.id !== p.id); if (data.selectedModeId === p.id) data.selectedModeId = 'whole'; selection(); persist(); return modes();
      case 'routing.get': return data.routing;
      case 'routing.setProcesses':
        if (!p.processes?.length) throw new Error('Выберите хотя бы одно приложение.');
        data.routing = { mode: 'selected', processes: [...p.processes] }; data.selectedModeId = 'apps'; remember(); selection(); persist(); return { routing: data.routing, modeId: 'apps' };
      case 'processes.list': return demoProcesses;
      case 'processes.pickExecutable': return { processIds: [], name: 'Demo App', executable: 'demo-app.exe', path: 'C:\\Demo\\demo-app.exe', iconDataUrl: null };
      case 'profiles.list': return data.profiles.map(profile => ({ ...profile, status: !profile.serverId ? 'empty' : data.servers.some(s => s.id === profile.serverId) && data.modes.some(m => m.id === profile.modeId) ? 'ready' : 'missing' }));
      case 'profiles.save': {
        const profile = { slot: p.slot, status: 'ready' as const, name: p.name, serverId: p.serverId, modeId: p.modeId,
          serverName: data.servers.find(s => s.id === p.serverId)?.name ?? null, modeName: data.modes.find(m => m.id === p.modeId)?.name ?? null };
        data.profiles = data.profiles.map(item => item.slot === p.slot ? profile : item); emit('profiles.changed', data.profiles); persist(); return data.profiles;
      }
      case 'profiles.delete': data.profiles = data.profiles.map(item => item.slot === p.slot ? { slot: p.slot, status: 'empty', name: null, serverId: null, modeId: null, serverName: null, modeName: null } : item); persist(); return data.profiles;
      case 'profiles.activate': {
        const profile = data.profiles.find(item => item.slot === p.slot); if (!profile?.serverId) throw new Error('Профиль пуст');
        data.selectedServerId = profile.serverId; data.selectedModeId = profile.modeId; selection(); persist(); return { serverId: profile.serverId, modeId: profile.modeId };
      }
      case 'subscriptions.list': return data.subscriptions;
      case 'subscriptions.save': {
        const value = p as unknown as SubscriptionInput;
        const old = data.subscriptions.find(s => s.id === value.id);
        if (old) data.servers.forEach(s => { if (s.group === old.remark) s.group = value.remark; });
        const item = { ...value, id: value.id ?? `sub-${Date.now()}`, serverCount: old?.serverCount ?? 0, refreshStatus: 'idle' as const, lastUpdatedAt: null, lastError: null };
        data.subscriptions = [...data.subscriptions.filter(s => s.id !== item.id), item]; emit('subscriptions.changed', data.subscriptions); persist(); return data.subscriptions;
      }
      case 'subscriptions.delete': {
        const item = data.subscriptions.find(s => s.id === p.id);
        data.servers = data.servers.filter(s => s.group !== item?.remark); data.subscriptions = data.subscriptions.filter(s => s.id !== p.id);
        data.selectedServerId = data.servers[0]?.id ?? null; serversChanged(); return data.subscriptions;
      }
      case 'subscriptions.refresh':
      case 'subscriptions.refreshAll': {
        for (const sub of data.subscriptions.filter(s => method.endsWith('All') ? s.enabled : s.id === p.id)) {
          sub.refreshStatus = 'refreshing'; emit('subscriptions.changed', data.subscriptions); await pause(350);
          if (!data.servers.some(s => s.group === sub.remark)) data.servers.push({ ...demoBootstrap().servers[1], id: `sub-server-${sub.id}`, group: sub.remark });
          sub.serverCount = data.servers.filter(s => s.group === sub.remark).length; sub.refreshStatus = 'success'; sub.lastUpdatedAt = new Date().toISOString();
        }
        emit('subscriptions.changed', data.subscriptions); serversChanged();
        return method.endsWith('All') ? { serverCount: data.servers.length, updatedSubscriptions: data.subscriptions.length, failedSubscriptions: 0 } : data.subscriptions.find(s => s.id === p.id)?.serverCount ?? 0;
      }
      case 'settings.get': return settings;
      case 'settings.update': {
        settings.settings = p.settings as SettingsData;
        data.profiles = Array.from({ length: settings.settings.general.profileCount }, (_, slot) => data.profiles[slot] ?? { slot, status: 'empty', name: null, serverName: null, modeName: null, serverId: null, modeId: null });
        emit('settings.changed', settings); persist(); return settings;
      }
      case 'logs.get': return data.logs;
      case 'logs.clear': data.logs = []; emit('logs.cleared', true); return true;
      case 'diagnostics.get': return diagnostics;
      case 'drivers.install': await pause(500); diagnostics = demoDiagnostics(); emit('diagnostics.changed', diagnostics); return diagnostics;
      case 'updates.getState': return update;
      case 'updates.check':
        update.status = 'checking'; emit('updates.changed', update); await pause(500);
        update = { ...update, status: 'available', latestVersion: '1.9.8-demo', releaseNotes: '## Демо-обновление\n\nУлучшения подключения и интерфейса.', releaseUrl: 'https://example.invalid/release', canDownload: true }; emit('updates.changed', update); return update;
      case 'updates.download':
        update.status = 'downloading'; update.canDownload = false;
        for (let progress = 0; progress <= 100; progress += 20) { update.progress = progress; emit('updates.changed', update); await pause(150); }
        update.status = 'ready'; update.canApply = true; emit('updates.changed', update); return update;
      case 'updates.apply': update.status = 'applied'; update.canApply = false; emit('updates.changed', update); return update;
      case 'demo.scenario':
        ++attempt;
        if (p.scenario === 'empty') { data.servers = []; data.subscriptions = []; data.selectedServerId = null; setConnection('disconnected'); serversChanged(); }
        else if (p.scenario === 'driver-error') { diagnostics.components[0] = { ...diagnostics.components[0], status: 'missing', canRepair: true, summary: 'Демо: требуется установка драйвера' }; emit('diagnostics.changed', diagnostics); }
        else { setConnection(p.scenario as ConnectionStatus); if (p.scenario === 'error') log('Демо: истекло время подключения', 'ERROR'); }
        return true;
      case 'demo.reset':
        ++attempt; clearInterval(trafficTimer); data = demoBootstrap(); settings = demoSettings(); details = demoModeDetails(); diagnostics = demoDiagnostics(); update = demoUpdate();
        Object.keys(selections).forEach(key => delete selections[key]); persist(); return true;
      case 'window.beginDrag': case 'window.minimize': case 'window.toggleMaximize': case 'window.close':
        emit('notification', { level: 'info', message: 'Демо: управление окном доступно в приложении Netch.' }); return true;
      default: throw new Error(`No demo handler for ${method}`);
    }
  }

  return { invoke: async (method: string, params: object) => structuredClone(await invoke(method, params)),
    dispose: () => { ++attempt; clearInterval(trafficTimer); } };
}
