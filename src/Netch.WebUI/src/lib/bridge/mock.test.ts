import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createMockBridge } from './mock';
import type { Bootstrap, ConnectionState, Routing } from '../types';

beforeEach(() => { localStorage.clear(); vi.useFakeTimers(); });
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); });

describe('Browser development transport', () => {
  it('keeps app selection across servers and a fresh preview', async () => {
    let mock = createMockBridge(vi.fn());
    await mock.invoke('routing.setProcesses', { processes: ['discord.exe', 'cs2.exe'] });
    await mock.invoke('servers.select', { id: 'demo-2' });
    expect(await mock.invoke('routing.get', {})).toEqual({ mode: 'selected', processes: ['discord.exe', 'cs2.exe'] });
    mock.dispose(); mock = createMockBridge(vi.fn());
    const state = await mock.invoke('app.bootstrap', {}) as Bootstrap;
    expect(state.selectedServerId).toBe('demo-2');
    expect(state.routing.processes).toEqual(['discord.exe', 'cs2.exe']);
    mock.dispose();
  });

  it('cancels an in-flight connection without a late connected event', async () => {
    const emit = vi.fn(); const mock = createMockBridge(emit);
    const connecting = mock.invoke('connection.connect', { serverId: 'demo-1', modeId: 'whole' });
    await mock.invoke('connection.cancel', {});
    await vi.advanceTimersByTimeAsync(1500);
    expect((await connecting as ConnectionState).status).toBe('disconnected');
    expect(emit.mock.calls.filter(([event, value]) => event === 'connection.stateChanged' && value.status === 'connected')).toHaveLength(0);
    mock.dispose();
  });

  it('returns snapshot events and implements diagnostics, updates and reset', async () => {
    const emit = vi.fn(); const mock = createMockBridge(emit);
    const check = mock.invoke('updates.check', {});
    await vi.advanceTimersByTimeAsync(600); await check;
    expect(emit.mock.calls.find(([event]) => event === 'updates.changed')?.[1].status).toBe('checking');
    await mock.invoke('demo.scenario', { scenario: 'empty' });
    expect((await mock.invoke('app.bootstrap', {}) as Bootstrap).servers).toHaveLength(0);
    await mock.invoke('demo.reset', {});
    expect((await mock.invoke('app.bootstrap', {}) as Bootstrap).servers).toHaveLength(9);
    expect((await mock.invoke('routing.get', {}) as Routing).processes).toContain('discord.exe');
    mock.dispose();
  });
});
