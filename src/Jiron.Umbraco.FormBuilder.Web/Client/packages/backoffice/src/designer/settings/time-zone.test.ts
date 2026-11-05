// A form's opening and closing on the site's clock (ROADMAP 10.1a, ADR-0184): a time on the zone's clock read as the
// instant the definition stores, in winter and in summer, at the hour the clock skips and the hour it repeats, and back;
// a zone the browser doesn't know is UTC.
import { describe, expect, it } from 'vitest';
import { isInstant, toInstant, toLocal, zoneOf } from './time-zone';

describe('the site clock', () => {
  it('names a zone the browser knows, and UTC for one it does not or for none', () => {
    expect(zoneOf('Europe/Berlin')).toBe('Europe/Berlin');
    expect(zoneOf('Mars/Olympus')).toBe('UTC');
    expect(zoneOf('')).toBe('UTC');
    expect(zoneOf(null)).toBe('UTC');
  });

  it("reads a time on the zone's clock as the instant in UTC, whole seconds, in winter and in summer", () => {
    expect(toInstant('2026-12-01T09:00', 'Europe/Berlin')).toBe('2026-12-01T08:00:00Z');
    expect(toInstant('2026-07-01T09:00', 'Europe/Berlin')).toBe('2026-07-01T07:00:00Z');
    expect(toInstant('2026-07-01T09:00', 'America/New_York')).toBe('2026-07-01T13:00:00Z');
    expect(toInstant('2026-07-01T09:00:30', 'UTC')).toBe('2026-07-01T09:00:30Z');
  });

  it('reads the hour the clock repeats as its first, and the hour it skips moved on by the gap', () => {
    // Berlin goes from 03:00 back to 02:00 on 25 October 2026, and from 02:00 on to 03:00 on 29 March 2026.
    expect(toInstant('2026-10-25T02:30', 'Europe/Berlin')).toBe('2026-10-25T00:30:00Z');
    expect(toInstant('2026-03-29T02:30', 'Europe/Berlin')).toBe('2026-03-29T01:30:00Z');
    expect(toLocal('2026-03-29T01:30:00Z', 'Europe/Berlin')).toBe('2026-03-29T03:30');
  });

  it('reads nothing from a text that is no time', () => {
    for (const text of ['', '2026-12-01', '2026-02-30T09:00', '2026-12-01T25:00', 'tomorrow'])
      expect(toInstant(text, 'UTC'), text).toBeNull();
  });

  it("shows an instant on the zone's clock to the minute, and nothing for no instant", () => {
    expect(toLocal('2026-12-01T08:00:00Z', 'Europe/Berlin')).toBe('2026-12-01T09:00');
    expect(toLocal('2026-07-01T07:00:59Z', 'Europe/Berlin')).toBe('2026-07-01T09:00');
    expect(toLocal(undefined, 'UTC')).toBe('');
    expect(toLocal('2026-12-01T08:00:00', 'UTC')).toBe('');
  });

  it('round-trips each hour of a year in a zone with summer time, but the second of the hour shown twice', () => {
    const missed: string[] = [];
    for (let hour = 0; hour < 365 * 24; hour++) {
      const instant = new Date(Date.UTC(2026, 0, 1) + hour * 3_600_000).toISOString().replace(/\.\d{3}Z$/, 'Z');
      if (toInstant(toLocal(instant, 'Europe/Berlin'), 'Europe/Berlin') !== instant) missed.push(instant);
    }
    // 02:00 to 03:00 on 25 October is shown twice, and reads as its first.
    expect(missed).toEqual(['2026-10-25T01:00:00Z']);
  });

  it('knows an instant as a definition stores one', () => {
    expect(isInstant('2026-12-01T08:00:00Z')).toBe(true);
    expect(isInstant('2026-12-01T08:00:00.000Z')).toBe(false);
    expect(isInstant('2026-12-01T08:00Z')).toBe(false);
    expect(isInstant(42)).toBe(false);
  });
});
