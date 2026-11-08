// The global settings' own values (ROADMAP 12.4a, ADR-0043, ADR-0218): the time zones grouped by area with UTC first and
// a stored zone kept; a zone's own name; sizes in megabytes both ways; and what a reference to a secret looks like.
import { describe, expect, it } from 'vitest';
import {
  browserZones,
  bytesOf,
  isSecretReference,
  MEGABYTE,
  megabytesOf,
  UTC,
  zoneGroups,
  zoneName,
} from './settings-values';

describe('the time zones', () => {
  it('group by area in the order of their names, UTC first among the zones of no area', () => {
    const groups = zoneGroups(
      ['Europe/Paris', 'America/New_York', 'Europe/Berlin', 'Etc/GMT+2', 'GMT'],
      'Europe/Berlin',
    );
    expect(groups.map((group) => group.area)).toEqual(['', 'America', 'Etc', 'Europe']);
    expect(groups[0]?.zones).toEqual([UTC, 'GMT']);
    expect(groups[3]?.zones).toEqual(['Europe/Berlin', 'Europe/Paris']);
  });

  it('keep a stored zone the browser does not list, in its area, so it is never changed unasked', () => {
    const groups = zoneGroups(['Europe/Berlin'], 'Antarctica/Troll');
    expect(groups.find((group) => group.area === 'Antarctica')?.zones).toEqual(['Antarctica/Troll']);
  });

  it('name a zone by what follows its area, its underscores as spaces', () => {
    expect(zoneName('America/Argentina/Buenos_Aires')).toBe('Argentina/Buenos Aires');
    expect(zoneName(UTC)).toBe(UTC);
  });

  it("are the browser's, as the server spells them", () => {
    const zones = browserZones();
    expect(zones.length).toBeGreaterThan(100);
    expect(zones).toContain('Europe/Berlin');
  });
});

describe('a file size', () => {
  it('shows in megabytes, whole where it is and with up to two decimals otherwise', () => {
    expect(megabytesOf(50 * MEGABYTE)).toBe('50');
    expect(megabytesOf(2.5 * MEGABYTE)).toBe('2.5');
    expect(megabytesOf(1_000_000)).toBe('0.95');
  });

  it('reads megabytes with a point or a comma, an empty box as the host limit, and anything else as invalid', () => {
    expect(bytesOf('20')).toBe(20 * MEGABYTE);
    expect(bytesOf(' 2,5 ')).toBe(2.5 * MEGABYTE);
    expect(bytesOf('')).toBeNull();
    for (const text of ['-1', '0', '1e3', 'ten', '2.555', '0.000']) expect(bytesOf(text), text).toBe('invalid');
  });
});

describe('a reference to a secret', () => {
  it('is the prefix and a name of letters, digits and underscores, never a secret itself', () => {
    expect(isSecretReference('$JironFormBuilder:Secrets:Signing_2')).toBe(true);
    expect(isSecretReference('$JironFormBuilder:Secrets:')).toBe(false);
    expect(isSecretReference('$JironFormBuilder:Secrets:has space')).toBe(false);
    expect(isSecretReference('https://hooks.example.test/abc')).toBe(false);
    expect(isSecretReference(`$JironFormBuilder:Secrets:${'a'.repeat(65)}`)).toBe(false);
  });
});
