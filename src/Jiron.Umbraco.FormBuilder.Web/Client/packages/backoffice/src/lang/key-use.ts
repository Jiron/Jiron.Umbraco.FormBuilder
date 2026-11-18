// Which of a dictionary area's keys the backoffice's code uses (ROADMAP 14.1): read from the sources, never run. A key
// is used where the code names it whole (`jfbArea_key`, `#jfbArea_key` in a manifest or a server's answer); where a
// file that builds the area's keys from a prefix (`jfbArea_${key}`, `jfbArea_part_${key}`) holds the rest of it as a
// literal ('key'); where a file of the area's own folders holds it as a literal, as those hand short keys to the area's
// term functions; and a key the code builds from a family's name and a part (`section_${id}`) is used for each part the
// lists naming the family hold, which each check passes in. The other way round, `builtKeyGaps` finds a key the code
// builds from a value that the area lacks, which a screen would show as the key. Only tests read this module.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

/** The backoffice package's sources. */
export const sourceFolder = join(dirname(fileURLToPath(import.meta.url)), '..');

/** A source file of the backoffice: its path below src, with forward slashes, and its text. */
export interface Source {
  readonly file: string;
  readonly text: string;
}

let read: readonly Source[] | undefined;

/** Every source of the backoffice that is no test, no dictionary and no generated client. */
export function sources(): readonly Source[] {
  read ??= readdirSync(sourceFolder, { recursive: true, encoding: 'utf8' })
    .map((file) => file.replaceAll('\\', '/'))
    .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts') && !/^(lang|api)\//.test(file))
    .map((file) => ({ file, text: readFileSync(join(sourceFolder, file), 'utf8') }));
  return read;
}

/** The sources in these folders below src, and below them; a path that ends in .ts names one file. */
export const inFolders = (...folders: readonly string[]): readonly Source[] =>
  sources().filter(({ file }) => folders.some((folder) => file === folder || file.startsWith(`${folder}/`)));

const LITERAL = /(?:'|")([A-Za-z][A-Za-z0-9_]*)(?:'|")/g;
const TEMPLATE_PART = /`([A-Za-z][A-Za-z0-9_]*)(?:`|\$\{)/g;
const SUFFIX = /\$\{[^}]+\}([A-Za-z_][A-Za-z0-9_]*)`/g;

const literals = (text: string) =>
  [...text.matchAll(LITERAL), ...text.matchAll(TEMPLATE_PART)].map((match) => match[1] ?? '');

/**
 * The keys of an area the sources use, of those it holds: named whole anywhere; as the rest of a prefix in a file that
 * builds the area's keys from it; as a literal in the area's own folders (`owners`); and a family's key for each part.
 */
export function usedKeys(
  area: string,
  keys: Iterable<string>,
  families: Readonly<Record<string, readonly string[]>> = {},
  owners: readonly Source[] = [],
): Set<string> {
  const held = new Set(keys);
  const used = new Set<string>();
  const add = (key: string) => {
    if (held.has(key)) used.add(key);
  };
  const whole = new RegExp(`${area}_([A-Za-z0-9_]+)`, 'g');
  const prefixes = new RegExp(`\`${area}_((?:[A-Za-z0-9]+_)*)\\$\\{`, 'g');
  // In the area's own folders, a key built of a prefix and a part (`style_${style}` beside ['plain', 'card'], or
  // `export_delimiter_${each}` over the DELIMITERS of the file beside), or of a part and a suffix (`${prefix}Nowhere`
  // beside 'outcome_name_redirect').
  const found = new Set(owners.flatMap(({ text }) => literals(text)));
  const parts = [
    ...[...found].filter((literal) => literal.endsWith('_')),
    ...owners.flatMap(({ text }) => [...text.matchAll(prefixes)].map((match) => match[1] ?? '')).filter(Boolean),
  ];
  const suffixes = new Set(owners.flatMap(({ text }) => [...text.matchAll(SUFFIX)].map((match) => match[1] ?? '')));
  for (const literal of found) {
    add(literal);
    for (const part of parts) add(part + literal);
    for (const suffix of suffixes) add(literal + suffix);
  }
  for (const { text } of sources()) {
    for (const match of text.matchAll(whole)) add(match[1] ?? '');
    const built = [...text.matchAll(prefixes)].map((match) => match[1] ?? '');
    if (built.length === 0) continue;
    const rests = literals(text);
    for (const prefix of built) for (const rest of rests) add(prefix + rest);
  }
  for (const [family, parts] of Object.entries(families)) for (const part of parts) add(`${family}_${part}`);
  return used;
}

const BUILT = /`([A-Za-z][A-Za-z0-9_]*)\$\{([^}`]+)\}([A-Za-z0-9_]*)`/g;

/**
 * The keys of an area the code builds from a value but the area lacks, which a screen would show as the key itself.
 * Keys built in one file from the same prefix and the same value with different endings (`retention_${state}`,
 * `retention_${state}Help`, `retention_${state}Unit`) are siblings: every value the area holds a sibling for, it must
 * hold each sibling for. A prefix counts where the area holds a key that starts with it, with or without the area's
 * own name before it. A word only some values have is named whole, never built (Retention's help lines).
 */
export function builtKeyGaps(area: string, keys: Iterable<string>, from: readonly Source[] = sources()): string[] {
  const held = new Set(keys);
  const groups = new Map<string, { prefix: string; endings: Set<string> }>();
  for (const { file, text } of from)
    for (const [, written = '', value = '', ending = ''] of text.matchAll(BUILT)) {
      const prefix = written.startsWith(`${area}_`) ? written.slice(area.length + 1) : written;
      if (![...held].some((key) => key.startsWith(prefix))) continue;
      const group = groups.get(`${file}|${prefix}|${value}`) ?? { prefix, endings: new Set<string>() };
      group.endings.add(ending);
      groups.set(`${file}|${prefix}|${value}`, group);
    }
  const gaps = new Set<string>();
  for (const { prefix, endings } of groups.values()) {
    if (endings.size < 2) continue;
    const ends = [...endings];
    // Each value, by the endings the area holds it with. A value held with the empty ending alone may be another
    // key's whole rest ('submittedHelp' beside 'submitted'), so a value counts once a written ending or two endings
    // hold it.
    const found = new Map<string, Set<string>>();
    for (const key of held) {
      if (!key.startsWith(prefix)) continue;
      const rest = key.slice(prefix.length);
      for (const ending of ends) {
        if (!rest.endsWith(ending) || rest.length === ending.length) continue;
        const value = rest.slice(0, rest.length - ending.length);
        if (/^[A-Za-z0-9]+$/.test(value)) found.set(value, (found.get(value) ?? new Set()).add(ending));
      }
    }
    const values = [...found]
      .filter(([, by]) => by.size > 1 || [...by].some((ending) => ending !== ''))
      .map(([value]) => value);
    for (const value of values)
      for (const ending of ends) if (!held.has(prefix + value + ending)) gaps.add(prefix + value + ending);
  }
  return [...gaps].sort();
}
