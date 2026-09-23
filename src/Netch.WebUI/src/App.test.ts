import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import type { Bootstrap, SettingsData, SettingsDocument } from './lib/types';

const api = vi.hoisted(() => ({ bootstrap: vi.fn(), get: vi.fn(), update: vi.fn(), saveSubscription: vi.fn(), refreshSubscription: vi.fn(), subscriptions: vi.fn(), servers: vi.fn(), ping: vi.fn() }));
vi.mock('./lib/bridge', () => ({
  BridgeError: class extends Error {},
  bridge: { on: () => () => {} },
  netch: {
    bootstrap: api.bootstrap,
    settings: { get: api.get, update: api.update },
    subscriptions: { save: api.saveSubscription, refresh: api.refreshSubscription, list: api.subscriptions },
    servers: { list: api.servers, ping: api.ping },
    diagnostics: { get: async () => ({ components: [] }) }
  }
}));
import App from './App.svelte';
import { language } from './lib/i18n';

function settingsFixture(): SettingsDocument {
  return {
    languages: ['System', 'en-US', 'ru-RU', 'zh-CN'], canUpdate: true,
    settings: {
      general: { language: 'System', profileCount: 4, profileColumns: 4 },
      connection: { socks5Port: 2801, httpPort: 2802, localAddress: '127.0.0.1', pingMethod: 'tcp', requestTimeoutMs: 10000, detectionIntervalSeconds: 10, startupPingDelaySeconds: -1, stunHost: 'stun.example.org', stunPort: 3478 },
      routing: { filterTcp: true, filterUdp: true, filterIcmp: false, icmpDelayMs: 0, filterDns: true, includeChildProcesses: true, proxyDns: true, handleOnlyDns: false, dnsHost: '1.1.1.1:53' },
      subscriptions: { updateOnLaunch: false },
      dns: { chinaDns: 'tcp://223.5.5.5:53', otherDns: 'tcp://1.1.1.1:53', tunAddress: '10.0.0.1', tunNetmask: '255.255.255.0', tunGateway: '10.0.0.2', useCustomDns: false, tunDns: '1.1.1.1', proxyTunDns: true, bypassIps: ['192.168.0.0/16'] },
      startup: { runAtStartup: false, connectOnLaunch: false, minimizeOnLaunch: false, closeToTray: true, stopConnectionOnExit: true },
      updates: { checkOnLaunch: true, includeBetaVersions: false },
      advanced: { xrayCone: true, allowInsecureTls: false, useMux: false, tcpFastOpen: false, hideUnsupportedEnvironmentWarning: false, kcpMtu: 1350, kcpTti: 50, kcpUplinkCapacity: 5, kcpDownlinkCapacity: 20, kcpReadBufferSize: 2, kcpWriteBufferSize: 2, kcpCongestion: false }
    }
  };
}

const bootstrap: Bootstrap = {
  servers: [], modes: [], profiles: [], subscriptions: [], logs: [],
  connection: { status: 'disconnected', message: 'Ready', serverId: null, modeId: null, connectedAt: null },
  routing: { mode: 'selected', processes: [] }, selectedServerId: null, selectedModeId: null, closeToTray: true
};
beforeEach(() => {
  language.set('en-US');
  api.bootstrap.mockResolvedValue(structuredClone(bootstrap));
  api.ping.mockImplementation(async () => (await api.bootstrap()).servers);
  api.get.mockImplementation(async () => settingsFixture());
  api.update.mockImplementation(async (settings: SettingsData) => ({ ...settingsFixture(), settings }));
});
afterEach(cleanup);

async function openSettings() {
  await fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
  return screen.findByRole('heading', { name: 'General' });
}

describe('Settings through the real reactive App parent', () => {
  it('starts latency testing after bootstrap without blocking the interface', async () => {
    const server = { id: '0', name: 'Test endpoint', hostname: 'example.org', port: 443, protocol: 'VLESS', group: 'Example', latency: null, managedBySubscription: true, isFavorite: false, countryCode: 'PL' };
    api.bootstrap.mockResolvedValue({ ...bootstrap, servers: [server], selectedServerId: '0' });
    api.ping.mockReturnValue(new Promise(() => {}));
    render(App);
    expect(await screen.findByRole('heading', { name: 'Test endpoint' })).toBeTruthy();
    await waitFor(() => expect(api.ping).toHaveBeenCalledOnce());
    expect(screen.queryByText('Loading Netch configuration')).toBeNull();
  });
  it('shows the same flag and clean name in the list, overview, and connection bar', async () => {
    api.bootstrap.mockResolvedValue({ ...bootstrap, selectedServerId: '0', servers: [
      { id: '0', name: 'FI Финляндия', countryCode: 'FI', protocol: 'VLESS', hostname: 'example.org', port: 443, group: 'Provider', latency: 327, latencyMethod: 'http', isFavorite: false }
    ] });
    render(App);
    expect(await screen.findByRole('heading', { name: 'Финляндия' })).toBeTruthy();
    expect(screen.getAllByRole('img', { name: 'FI' })).toHaveLength(3);
    expect(screen.queryByText('FI Финляндия')).toBeNull();
    expect(screen.queryByText(/^HTTP/)).toBeNull();
  });
  it('saves HTTP latency method and its target URL', async () => {
    render(App);
    await openSettings();
    await fireEvent.click(screen.getByRole('button', { name: 'Connection' }));
    await fireEvent.change(screen.getByRole('combobox', { name: /Latency method/ }), { target: { value: 'http' } });
    await fireEvent.input(screen.getByRole('textbox', { name: /Latency test URL/ }), { target: { value: 'https://example.org/ping' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));
    await waitFor(() => expect(api.update).toHaveBeenCalledOnce());
    expect(api.update.mock.calls[0][0].connection).toMatchObject({ pingMethod: 'http', latencyTestUrl: 'https://example.org/ping' });
  });

  it('shows friendly language names and translates the UI after saving Russian', async () => {
    render(App);
    await openSettings();
    expect(screen.getByRole('option', { name: 'English' })).toBeTruthy();
    expect(screen.getByRole('option', { name: 'Русский' })).toBeTruthy();
    expect(screen.getByRole('option', { name: /Chinese/ })).toBeTruthy();
    await fireEvent.change(screen.getByRole('combobox', { name: /Interface language/ }), { target: { value: 'ru-RU' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));
    await waitFor(() => expect(api.update).toHaveBeenCalledOnce());
    expect(api.update.mock.calls[0][0].general.language).toBe('ru-RU');
    expect(await screen.findByRole('button', { name: 'Настройки' })).toBeTruthy();
    expect(await screen.findByRole('heading', { name: /^Серверы/ })).toBeTruthy();
  });

  it('downloads servers immediately after adding an enabled subscription', async () => {
    const source = { id: 'source-1', remark: 'Example', url: 'https://example.org/sub', userAgent: '', enabled: true, serverCount: 1, refreshStatus: 'success', lastUpdatedAt: null, lastError: null };
    api.saveSubscription.mockResolvedValue([source]);
    api.refreshSubscription.mockResolvedValue(1);
    api.subscriptions.mockResolvedValue([source]);
    api.servers.mockResolvedValue([{ id: '0', name: 'Imported endpoint', hostname: 'example.org', port: 443, protocol: 'VLESS', group: 'Example', latency: null, managedBySubscription: true, isFavorite: false, countryCode: null }]);
    render(App);
    await screen.findByRole('heading', { name: /^Servers/ });
    await fireEvent.click(screen.getByRole('button', { name: 'Add subscription' }));
    await fireEvent.input(screen.getByRole('textbox', { name: 'Name' }), { target: { value: 'Example' } });
    await fireEvent.input(screen.getByRole('textbox', { name: 'Subscription URL' }), { target: { value: source.url } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(api.refreshSubscription).toHaveBeenCalledWith('source-1'));
    expect(await screen.findByText('Imported endpoint')).toBeTruthy();
  });

  it('opens all sections instead of staying on Preparing settings', async () => {
    render(App);
    await openSettings();
    expect(screen.queryByText('Preparing settings…')).toBeNull();
    for (const name of ['Connection', 'Routing', 'Subscriptions', 'DNS', 'Startup', 'Updates', 'Advanced', 'General']) {
      await fireEvent.click(screen.getByRole('button', { name }));
      expect(await screen.findByRole('heading', { name })).toBeTruthy();
    }
  });

  it('discards edits on cancel and reopens with backend values', async () => {
    render(App);
    await openSettings();
    await fireEvent.change(screen.getByRole('combobox', { name: /Interface language/ }), { target: { value: 'en-US' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    await openSettings();
    expect((screen.getByRole('combobox', { name: /Interface language/ }) as HTMLSelectElement).value).toBe('System');
    expect(api.update).not.toHaveBeenCalled();
  });

  it('sends edited data as a plain snapshot, including normalized DNS networks', async () => {
    render(App);
    await openSettings();
    await fireEvent.change(screen.getByRole('combobox', { name: /Interface language/ }), { target: { value: 'en-US' } });
    await fireEvent.click(screen.getByRole('button', { name: 'DNS' }));
    await fireEvent.input(screen.getByRole('textbox', { name: /Bypass networks/ }), { target: { value: ' 10.0.0.0/8,\n192.168.0.0/16\n' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));
    await waitFor(() => expect(api.update).toHaveBeenCalledOnce());
    const sent = structuredClone(api.update.mock.calls[0][0]);
    expect(sent.general.language).toBe('en-US');
    expect(sent.dns.bypassIps).toEqual(['10.0.0.0/8', '192.168.0.0/16']);
    expect(settingsFixture().settings.general.language).toBe('System');
  });

  it('allows retry after a failed load', async () => {
    api.get.mockRejectedValueOnce(new Error('Settings service unavailable'));
    render(App);
    await fireEvent.click(screen.getByRole('button', { name: 'Settings' }));
    await screen.findByText('Settings service unavailable');
    await fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(await screen.findByRole('heading', { name: 'General' })).toBeTruthy();
  });

  it('keeps the form and draft available after a failed save', async () => {
    api.update.mockRejectedValueOnce(new Error('Could not save settings'));
    render(App);
    await openSettings();
    await fireEvent.change(screen.getByRole('combobox', { name: /Interface language/ }), { target: { value: 'en-US' } });
    await fireEvent.click(screen.getByRole('button', { name: 'Save settings' }));
    await screen.findByText('Could not save settings');
    expect((screen.getByRole('combobox', { name: /Interface language/ }) as HTMLSelectElement).value).toBe('en-US');
    expect((screen.getByRole('button', { name: 'Save settings' }) as HTMLButtonElement).disabled).toBe(false);
  });

  it('keeps settings readable but prevents saving during a connection', async () => {
    api.bootstrap.mockResolvedValue({ ...bootstrap, connection: { ...bootstrap.connection, status: 'connected' } });
    render(App);
    await openSettings();
    expect((screen.getByRole('button', { name: 'Save settings' }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText('Disconnect to change networking settings.')).toBeTruthy();
  });
});
