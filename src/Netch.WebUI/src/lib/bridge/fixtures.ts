import type { Bootstrap, DiagnosticsSnapshot, ModeDetails, ProcessInfo, SettingsDocument, UpdateState } from '../types';

// Edit these fixtures to design against your own content. No real credentials or endpoints.
export function demoBootstrap(): Bootstrap {
  return {
    language: 'ru-RU', closeToTray: true,
    selectedServerId: 'demo-0', selectedModeId: 'whole',
    connection: { status: 'disconnected', message: 'Ready', serverId: null, modeId: null, connectedAt: null },
    routing: { mode: 'all', processes: ['discord.exe', 'chrome.exe'] },
    modes: [
      { id: 'whole', name: 'Весь компьютер', kind: 'TunMode', supportsProcessSelection: false, selectionRole: 'whole-computer' },
      { id: 'apps', name: 'Выбранные приложения', kind: 'ProcessMode', supportsProcessSelection: true, selectionRole: 'selected-applications' },
      { id: 'game', name: 'Игровой профиль', kind: 'TunMode', supportsProcessSelection: false }
    ],
    servers: [
      ['Автоматический выбор', null], ['Польша, Варшава', 'PL'], ['Германия, Франкфурт', 'DE'],
      ['Нидерланды, Амстердам', 'NL'], ['Финляндия, Хельсинки', 'FI'], ['США, Нью-Йорк', 'US'],
      ['Япония, Токио', 'JP'], ['Великобритания, Лондон', 'GB'], ['Франция, Париж', 'FR']
    ].map(([name, countryCode], index) => ({
      id: `demo-${index}`, name: name!, countryCode, group: 'Demo VPN', protocol: 'VLESS',
      hostname: `node-${index}.example.invalid`, port: 443, latency: null, latencyMethod: 'http',
      managedBySubscription: true, isFavorite: index === 1, isAutomatic: index === 0
    })),
    subscriptions: [{ id: 'demo-sub', remark: 'Demo VPN', url: 'https://example.invalid/subscription', userAgent: '', enabled: true,
      serverCount: 9, refreshStatus: 'success', lastUpdatedAt: new Date().toISOString(), lastError: null }],
    profiles: Array.from({ length: 4 }, (_, slot) => ({ slot, status: slot ? 'empty' : 'ready', name: slot ? null : 'Для работы',
      serverName: slot ? null : 'Польша, Варшава', modeName: slot ? null : 'Выбранные приложения', serverId: slot ? null : 'demo-1', modeId: slot ? null : 'apps' })),
    logs: [{ id: 1, timestamp: new Date().toISOString(), level: 'INFO', message: 'Демо: интерфейс готов. Все действия имитируются.' }]
  };
}

export function demoSettings(): SettingsDocument {
  return { languages: ['System', 'en-US', 'ru-RU', 'zh-CN'], canUpdate: true, settings: {
    general: { language: 'ru-RU', profileCount: 4, profileColumns: 4 },
    connection: { socks5Port: 2801, httpPort: 2802, localAddress: '127.0.0.1', pingMethod: 'http', latencyTestUrl: 'https://example.invalid/ping', requestTimeoutMs: 10000, detectionIntervalSeconds: 10, startupPingDelaySeconds: 0, stunHost: 'stun.example.invalid', stunPort: 3478 },
    routing: { filterTcp: true, filterUdp: true, filterIcmp: false, icmpDelayMs: 0, filterDns: true, includeChildProcesses: true, proxyDns: true, handleOnlyDns: false, dnsHost: '1.1.1.1:53' },
    subscriptions: { updateOnLaunch: false },
    dns: { chinaDns: 'tcp://223.5.5.5:53', otherDns: 'tcp://1.1.1.1:53', tunAddress: '10.0.236.10', tunNetmask: '255.255.255.0', tunGateway: '10.0.236.1', useCustomDns: false, tunDns: '1.1.1.1', proxyTunDns: true, bypassIps: [] },
    startup: { runAtStartup: false, connectOnLaunch: false, minimizeOnLaunch: false, closeToTray: true, stopConnectionOnExit: true },
    updates: { checkOnLaunch: true, includeBetaVersions: false },
    advanced: { xrayCone: true, allowInsecureTls: false, useMux: false, tcpFastOpen: false, hideUnsupportedEnvironmentWarning: false, kcpMtu: 1350, kcpTti: 50, kcpUplinkCapacity: 5, kcpDownlinkCapacity: 20, kcpReadBufferSize: 2, kcpWriteBufferSize: 2, kcpCongestion: false }
  } };
}

export const demoProcesses: ProcessInfo[] = [
  ['Discord', 'discord.exe'], ['Google Chrome', 'chrome.exe'], ['Steam', 'steam.exe'], ['Telegram', 'telegram.exe'],
  ['Firefox', 'firefox.exe'], ['Visual Studio Code', 'Code.exe'], ['Counter-Strike 2', 'cs2.exe']
].map(([name, executable], index) => ({ name, executable, processIds: [1000 + index], path: `C:\\Apps\\${name}\\${executable}`, iconDataUrl: null }));

export function demoDiagnostics(): DiagnosticsSnapshot {
  return { appVersion: '1.9.7-demo', runtimeVersion: '.NET 8 (demo)', operatingSystem: 'Windows 11 (demo)', architecture: 'x64',
    isAdministrator: true, webView2Version: 'Browser preview', checkedAt: new Date().toISOString(),
    components: ['wintun', 'netfilter2', 'xray', 'webview2'].map(id => ({ id, name: id, status: 'ready', summary: 'Готово · демоданные', version: 'demo', canRepair: false })) };
}

export function demoModeDetails(): ModeDetails[] {
  return demoBootstrap().modes.map(mode => ({ ...mode, kind: mode.kind as ModeDetails['kind'], source: 'built-in', editable: false,
    fileName: `${mode.id}.json`, filterIcmp: null, filterTcp: null, filterUdp: null, filterDns: null, includeChildProcesses: null,
    icmpDelayMs: null, proxyDns: null, handleOnlyDns: null, dnsHost: null, filterLoopback: false, filterIntranet: true,
    bypassRules: [], handleRules: mode.id === 'whole' ? ['0.0.0.0/1', '128.0.0.0/1'] : mode.id === 'apps' ? ['discord\\.exe', 'chrome\\.exe'] : ['203.0.113.0/24'], shareArgument: null }));
}

export function demoUpdate(): UpdateState {
  return { status: 'idle', currentVersion: '1.9.7-demo', latestVersion: null, releaseNotes: null, releaseUrl: null,
    progress: 0, error: null, canDownload: false, canApply: false };
}
