import type { Server } from './types';

/** Display-only cleanup. Subscription names and identity remain unchanged. */
export function serverLabel(server: Pick<Server, 'name' | 'countryCode'>): string {
  let name = server.name.replace(/^(?:[\u{1F1E6}-\u{1F1FF}]{2}\s*)+/u, '').trim();
  if (server.countryCode && /^[a-z]{2}$/i.test(server.countryCode)) {
    name = name.replace(new RegExp(`^(?:\\[${server.countryCode}\\]|${server.countryCode})(?=\\s|[|:/—–-])\\s*[|:/—–-]?\\s*`, 'i'), '');
  }
  return name || server.name;
}
