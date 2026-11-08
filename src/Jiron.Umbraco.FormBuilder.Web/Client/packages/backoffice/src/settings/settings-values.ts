// The values the global settings' own editors read and write (ROADMAP 12.4a, ADR-0043, ADR-0218): the time zones as a
// list grouped by their area, so nobody types a zone's spelling; a file's largest size in megabytes; and a reference to
// a secret in the host configuration, which is all a screen ever holds of one.

/** The zone every list starts with. */
export const UTC = 'UTC';

/** The zones of one area, as a list's group shows them: Europe's Berlin, Lisbon and Paris. */
export interface ZoneGroup {
  /** The area, such as Europe; empty for the zones of no area, UTC first among them. */
  readonly area: string;
  readonly zones: readonly string[];
}

/** The zones the browser knows, as the server spells them; none in a browser that can't list them. */
export function browserZones(): readonly string[] {
  try {
    return Intl.supportedValuesOf('timeZone');
  } catch {
    return [];
  }
}

/**
 * The zones to choose from, by their area in the order of their names, UTC first: a zone of no area, such as UTC, sits
 * at the top; one stored that the browser doesn't list stays offered in its area, so it is never changed unasked.
 */
export function zoneGroups(zones: readonly string[], stored: string | null | undefined): readonly ZoneGroup[] {
  const all = new Set([UTC, ...zones]);
  if (stored) all.add(stored);
  const groups = new Map<string, string[]>();
  for (const zone of [...all].sort((one, other) => one.localeCompare(other, 'en'))) {
    const slash = zone.indexOf('/');
    const area = slash < 0 ? '' : zone.slice(0, slash);
    groups.set(area, [...(groups.get(area) ?? []), zone]);
  }
  const top = (groups.get('') ?? []).sort((one, other) => (one === UTC ? -1 : other === UTC ? 1 : 0));
  return [
    { area: '', zones: top },
    ...[...groups.entries()]
      .filter(([area]) => area !== '')
      .sort(([one], [other]) => one.localeCompare(other, 'en'))
      .map(([area, inArea]) => ({ area, zones: inArea })),
  ];
}

/** A zone's own name in its list: Argentina/Buenos_Aires under America reads "Argentina/Buenos Aires". */
export function zoneName(zone: string): string {
  const slash = zone.indexOf('/');
  return (slash < 0 ? zone : zone.slice(slash + 1)).replace(/_/g, ' ');
}

/** The bytes of a megabyte, as file sizes count them on the screen. */
export const MEGABYTE = 1_048_576;

/** A number of bytes in megabytes, as a box shows it: whole where it is, else with up to two decimals. */
export function megabytesOf(bytes: number): string {
  const megabytes = Math.round((bytes / MEGABYTE) * 100) / 100;
  return String(megabytes);
}

/**
 * What a size box holds, in bytes: a number of megabytes with up to two decimals, written with a point or a comma; null
 * for an empty box, which leaves the host's limit; 'invalid' for anything else, or a size below one byte.
 */
export function bytesOf(text: string): number | null | 'invalid' {
  const trimmed = text.trim().replace(',', '.');
  if (trimmed === '') return null;
  if (!/^\d+(\.\d{1,2})?$/.test(trimmed)) return 'invalid';
  const bytes = Math.round(Number(trimmed) * MEGABYTE);
  return bytes < 1 ? 'invalid' : bytes;
}

/** What a reference to a secret starts with; the name after it is the key's last part. */
export const SECRET_PREFIX = '$JironFormBuilder:Secrets:';

/** Whether a text is a reference to a secret: the prefix, then 1 to 64 letters, digits and underscores. */
export const isSecretReference = (text: string): boolean =>
  text.startsWith(SECRET_PREFIX) && /^[A-Za-z0-9_]{1,64}$/.test(text.slice(SECRET_PREFIX.length));
