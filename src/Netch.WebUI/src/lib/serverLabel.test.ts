import { describe, expect, it } from 'vitest';
import { serverLabel } from './serverLabel';

describe('Server display name', () => {
  it.each(['FI Финляндия', 'fi / Финляндия', '[FI] Финляндия', '🇫🇮 Финляндия'])('replaces the country prefix in %s', name => {
    expect(serverLabel({ name, countryCode: 'FI' })).toBe('Финляндия');
  });
  it('preserves ordinary names and does not mutate the saved label', () => {
    const server = { name: 'FI Финляндия', countryCode: 'FI' };
    serverLabel(server);
    expect(server.name).toBe('FI Финляндия');
    expect(serverLabel({ name: 'Finland - premium', countryCode: 'FI' })).toBe('Finland - premium');
  });
});
