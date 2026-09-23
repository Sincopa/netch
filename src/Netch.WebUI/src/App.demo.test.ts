import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/svelte';
import App from './App.svelte';
import { bridge } from './lib/bridge';

afterEach(async () => { await bridge.invoke('demo.scenario', { scenario: 'disconnected' }); cleanup(); });

describe('Real application in browser demo mode', () => {
  it('guides a new user from the empty state through help to the subscription form', async () => {
    await bridge.invoke('demo.reset');
    await bridge.invoke('demo.scenario', { scenario: 'empty' });
    render(App);
    await fireEvent.click(await screen.findByRole('button', { name: /Где взять ссылку/ }));
    expect(await screen.findByRole('heading', { name: 'Три шага до подключения' })).toBeTruthy();
    await fireEvent.click(screen.getByRole('button', { name: 'Открыть подписки' }));
    expect(await screen.findByRole('textbox', { name: 'Адрес подписки' })).toBeTruthy();
    expect(screen.queryByRole('heading', { name: 'Три шага до подключения' })).toBeNull();
  });

  it('closes the help dialog with Escape and exposes the main controls again', async () => {
    await bridge.invoke('demo.reset');
    render(App);
    await fireEvent.click(await screen.findByRole('button', { name: 'Как пользоваться' }));
    expect(await screen.findByRole('dialog')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Подключиться' })).toBeNull();
    await fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(screen.getByRole('button', { name: 'Подключиться' })).toBeTruthy();
  });

  it('recovers an empty search without changing the selected server', async () => {
    await bridge.invoke('demo.reset');
    render(App);
    const search = await screen.findByRole('textbox', { name: 'Поиск серверов' });
    await fireEvent.input(search, { target: { value: 'no-such-location' } });
    await fireEvent.click(await screen.findByRole('button', { name: 'Сбросить фильтры' }));
    expect((search as HTMLInputElement).value).toBe('');
    expect(screen.getAllByRole('option', { selected: true }).some(option => option.classList.contains('server-row'))).toBe(true);
  });

  it('opens every main dialog through the development toolbar', async () => {
    await bridge.invoke('demo.reset');
    render(App);
    const picker = await screen.findByRole('combobox', { name: 'Открыть демо-экран' });
    for (const value of ['settings', 'subscriptions', 'processes', 'import', 'server', 'profiles', 'modes', 'diagnostics', 'updates']) {
      await fireEvent.change(picker, { target: { value } });
      await waitFor(() => expect(screen.getAllByRole('dialog')).toHaveLength(1));
      await fireEvent.keyDown(window, { key: 'Escape' });
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    }
  });

  it('switches routing without a dialog, opens apps from the row and preserves the saved selection', async () => {
    await bridge.invoke('demo.reset');
    render(App);
    await screen.findByRole('combobox', { name: 'Открыть демо-экран' });
    await fireEvent.click(await screen.findByRole('button', { name: /^Выбрать приложения/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: /^Выбрать приложения/ }).getAttribute('aria-pressed')).toBe('true'));
    expect(screen.queryByRole('dialog')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: /^Выбрать приложения/ }));
    expect(screen.queryByRole('dialog')).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: /^Весь компьютер/ }));
    await waitFor(() => expect(screen.getByRole('button', { name: /^Весь компьютер/ }).getAttribute('aria-pressed')).toBe('true'));
    expect(screen.queryByRole('button', { name: /^Приложения/ })).toBeNull();
    await fireEvent.click(screen.getByRole('button', { name: /^Выбрать приложения/ }));
    await fireEvent.click(await screen.findByRole('button', { name: /^Приложения/ }));
    expect(await screen.findByRole('dialog')).toBeTruthy();
    await fireEvent.click(screen.getByRole('button', { name: 'Применить' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await bridge.invoke('servers.select', { id: 'demo-2' });
    await waitFor(() => expect(screen.getByText('Выбрано приложений: 2')).toBeTruthy());
  });

  it('shows app controls before a saved profile exists and requires saving before connecting', async () => {
    await bridge.invoke('demo.reset');
    await bridge.invoke('modes.delete', { id: 'apps' });
    render(App);
    await fireEvent.click(await screen.findByRole('button', { name: /^Выбрать приложения/ }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByRole('button', { name: /^Выбрать приложения/ }).getAttribute('aria-pressed')).toBe('true');
    expect((screen.getByRole('button', { name: 'Подключиться' }) as HTMLButtonElement).disabled).toBe(true);
    await fireEvent.click(screen.getByRole('button', { name: /^Приложения/ }));
    expect(await screen.findByRole('dialog')).toBeTruthy();
    await fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    await fireEvent.click(screen.getByRole('button', { name: /^Весь компьютер/ }));
    await waitFor(() => expect((screen.getByRole('button', { name: 'Подключиться' }) as HTMLButtonElement).disabled).toBe(false));
  });
});
