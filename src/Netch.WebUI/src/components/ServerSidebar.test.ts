import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen } from '@testing-library/svelte';
import ServerSidebar from './ServerSidebar.svelte';
import { language } from '../lib/i18n';
import type { Server } from '../lib/types';

afterEach(() => { cleanup(); language.set('en-US'); });
const servers: Server[] = [
  { id: '0', name: 'Netherlands', group: 'Provider', hostname: 'example.org', port: 443, protocol: 'VLESS', latency: null, managedBySubscription: true, isFavorite: false, countryCode: 'NL' },
  { id: '1', name: 'Automatic', group: 'All', hostname: 'example.net', port: 443, protocol: 'VLESS', latency: undefined as unknown as null, managedBySubscription: true, isFavorite: false, countryCode: 'DE', isAutomatic: true }
];
function show(pingingIds: string[] = []) {
  return render(ServerSidebar, { servers, pingingIds, selectedId: null, loading: false, disabled: false, mutationDisabled: false, favoriteDisabled: false,
    onSubscriptions: vi.fn(), onSelect: vi.fn(), onPingAll: vi.fn(), onPingOne: vi.fn(), onFavorite: vi.fn(), onCreate: vi.fn(), onImport: vi.fn(), onEdit: vi.fn(), onDelete: vi.fn() });
}
describe('Server list localization and metadata', () => {
  it('shows a spinner while testing and removes it when the request finishes', async () => {
    const view = show(['0']);
    expect(screen.getByRole('status', { name: 'Testing latency' }).querySelector('.spin')).toBeTruthy();
    expect((screen.getByRole('button', { name: 'Test all servers' }) as HTMLButtonElement).disabled).toBe(true);
    await view.rerender({ pingingIds: [] });
    expect(screen.queryByRole('status', { name: 'Testing latency' })).toBeNull();
  });
  it('keeps All independent of the language and of a group named All', async () => {
    language.set('ru-RU'); show();
    expect(screen.getAllByRole('option', { selected: false }).length).toBeGreaterThan(0);
    expect(screen.getByText('Netherlands')).toBeTruthy();
    expect(screen.getByText('Automatic')).toBeTruthy();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Фильтр групп серверов' }), { target: { value: 'Provider' } });
    expect(screen.queryByText('Automatic')).toBeNull();
    await fireEvent.change(screen.getByRole('combobox', { name: 'Фильтр групп серверов' }), { target: { value: '' } });
    expect(screen.getByText('Automatic')).toBeTruthy();
    language.set('en-US');
    expect(await screen.findByText('Netherlands')).toBeTruthy();
  });

  it('uses an SVG flag, an Auto icon, and never shows a missing latency placeholder', () => {
    language.set('ru-RU'); show();
    expect(screen.getByRole('img', { name: 'NL' }).getAttribute('src')).toMatch(/nl\.svg|data:image\/svg\+xml/);
    expect(screen.queryByRole('img', { name: 'DE' })).toBeNull();
    expect(screen.getByLabelText('Автоматический выбор сервера')).toBeTruthy();
    expect(screen.queryByText(/\{0\}/)).toBeNull();
  });
});
