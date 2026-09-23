import { derived, writable } from 'svelte/store';
import ru from './ru.json';

export const language = writable('System');
export const locale = derived(language, value => {
  const resolved = value === 'System' ? (globalThis.navigator?.language ?? 'en-US') : value;
  return resolved.toLowerCase().startsWith('ru') ? 'ru-RU' : 'en-US';
});
export function translate(code: string, key: string, ...values: unknown[]): string {
  const text = code === 'ru-RU' ? (ru as Record<string, string>)[key] ?? key : key;
  return text.replace(/\{(\d+)\}/g, (match, index) => values[Number(index)] === undefined ? match : String(values[Number(index)]));
}
export const t = derived(locale, code => (key: string, ...values: unknown[]) => translate(code, key, ...values));
const names: Record<string, string> = {
  'en-US': 'English', 'ru-RU': 'Русский', 'zh-CN': 'Chinese · 简体中文',
  'zh-TW': 'Chinese · 繁體中文', 'ja-JP': 'Japanese · 日本語', 'fa-IR': 'Persian · فارسی'
};
export function languageName(code: string): string { return names[code] ?? code; }
