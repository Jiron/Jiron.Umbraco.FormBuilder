// A form's opening and closing in the site's time zone (ROADMAP 10.1a, ADR-0184): the definition stores an instant in
// UTC with whole seconds, `2026-12-01T08:00:00Z`; the Settings view shows and takes the time on the site's clock, as a
// date-and-time box writes it, `2026-12-01T09:00`. Pure, with the browser's own time zone data.

/** A time zone the browser knows by this IANA name, or UTC for one it doesn't. */
export function zoneOf(name: string | null | undefined): string {
  if (!name) return 'UTC';
  try {
    return new Intl.DateTimeFormat('en-US', { timeZone: name }).resolvedOptions().timeZone;
  } catch {
    return 'UTC';
  }
}

const PARTS = new Map<string, Intl.DateTimeFormat>();

function partsIn(zone: string): Intl.DateTimeFormat {
  let format = PARTS.get(zone);
  if (!format) {
    format = new Intl.DateTimeFormat('en-US', {
      timeZone: zone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
    PARTS.set(zone, format);
  }
  return format;
}

/** The time on the zone's clock at an instant, as milliseconds of a UTC date with the same digits. */
function wallAt(instant: number, zone: string): number {
  const parts = partsIn(zone).formatToParts(new Date(instant));
  const part = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((one) => one.type === type)?.value ?? 0);
  return Date.UTC(part('year'), part('month') - 1, part('day'), part('hour'), part('minute'), part('second'));
}

const LOCAL = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2}))?$/;
const INSTANT = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$/;

/** Whether a text is an instant as a definition stores one. */
export const isInstant = (text: unknown): text is string =>
  typeof text === 'string' && INSTANT.test(text) && !Number.isNaN(Date.parse(text));

const DAY = 86_400_000;

/**
 * The instant a time on the zone's clock names, as a definition stores it; null for a text that is no such time. A time
 * the clock shows twice, as it goes back, is its first; one it skips, as it goes forward, is moved on by the gap.
 */
export function toInstant(local: string, zone: string): string | null {
  const match = LOCAL.exec(local);
  if (!match) return null;
  const [, year, month, day, hour, minute, second] = match;
  const wall = Date.UTC(
    Number(year),
    Number(month) - 1,
    Number(day),
    Number(hour),
    Number(minute),
    Number(second ?? 0),
  );
  if (Number.isNaN(wall) || new Date(wall).toISOString().slice(0, 16) !== local.slice(0, 16)) return null;
  // The zone's offsets a day around the time, and the instants they give that show the time on its clock.
  const offset = (instant: number) => wallAt(instant, zone) - instant;
  const before = offset(wall - DAY);
  const fits = [...new Set([before, offset(wall), offset(wall + DAY)])]
    .map((one) => wall - one)
    .filter((instant) => wallAt(instant, zone) === wall)
    .sort((one, other) => one - other);
  const instant = fits[0] ?? wall - before;
  return new Date(instant).toISOString().replace(/\.\d{3}Z$/, 'Z');
}

/** An instant as a date-and-time box shows it on the zone's clock, to the minute; '' for no instant. */
export function toLocal(instant: unknown, zone: string): string {
  if (!isInstant(instant)) return '';
  return new Date(wallAt(Date.parse(instant), zone)).toISOString().slice(0, 16);
}
