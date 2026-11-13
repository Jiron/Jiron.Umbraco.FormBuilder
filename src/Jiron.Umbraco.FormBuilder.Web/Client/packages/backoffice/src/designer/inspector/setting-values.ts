// A field type's settings as the inspector edits them (ROADMAP 9.4a, ADR-0170): a setting's value with its default and
// the host's lock, what the user typed checked against the setting's rules as the server checks it, and the field with
// the new value written back. Pure, so every rule is tested without the DOM.
import type { FieldSettingResponseModel } from '../../api';
import type { DesignField, LocalizedText } from '../definition';
import { keyIn } from '../preview/field-models';
import { bytesOf, megabytesOf } from '../../settings/settings-values';

/** Why a value can't be kept, in the terms the inspector's words name. */
export type Problem =
  | { readonly kind: 'required' }
  | { readonly kind: 'wholeNumber' }
  | { readonly kind: 'number' }
  | { readonly kind: 'time' }
  | { readonly kind: 'date' }
  | { readonly kind: 'relative' }
  | { readonly kind: 'below'; readonly limit: string; readonly exclusive: boolean }
  | { readonly kind: 'above'; readonly limit: string; readonly exclusive: boolean }
  | { readonly kind: 'tooShort'; readonly limit: number }
  | { readonly kind: 'tooLong'; readonly limit: number }
  | { readonly kind: 'tooFew'; readonly limit: number }
  | { readonly kind: 'tooMany'; readonly limit: number }
  | { readonly kind: 'notAllowed' }
  | { readonly kind: 'megabytes' };

/** A value read from what was typed: undefined leaves the setting to its default. */
export type Parsed = { readonly ok: true; readonly value: unknown } | { readonly ok: false; readonly problem: Problem };

const ok = (value: unknown): Parsed => ({ ok: true, value });
const refused = (problem: Problem): Parsed => ({ ok: false, problem });

/** A field's own settings, as stored. */
export const settingsOf = (field: DesignField): Readonly<Record<string, unknown>> =>
  (field['settings'] as Readonly<Record<string, unknown>> | undefined) ?? {};

/** A setting's value: the host's locked one, else the field's own, else the type's default. */
export function valueOf(settings: Readonly<Record<string, unknown>>, setting: FieldSettingResponseModel): unknown {
  const lock = setting.lock;
  if (lock && lock.value !== undefined && lock.value !== null) return lock.value;
  if (lock) return setting.default ?? undefined;
  return Object.hasOwn(settings, setting.name) ? settings[setting.name] : (setting.default ?? undefined);
}

/** A record without one of its keys. */
export function without<T>(record: Readonly<Record<string, T>>, name: string): Record<string, T> {
  return Object.fromEntries(Object.entries(record).filter(([key]) => key !== name));
}

/** A record with one key set, or taken away for undefined. */
export function withKey<T>(record: Readonly<Record<string, T>>, name: string, value: T | undefined): Record<string, T> {
  return value === undefined ? without(record, name) : { ...record, [name]: value };
}

/**
 * A field with one setting set, or left to its default when the value is undefined or the default itself; a field left
 * with no setting of its own has no settings at all, as the server writes it.
 */
export function withSetting(field: DesignField, setting: FieldSettingResponseModel, value: unknown): DesignField {
  const own = withKey(
    settingsOf(field),
    setting.name,
    value === undefined || same(value, setting.default) ? undefined : value,
  );
  const rest = without(field, 'settings') as DesignField;
  return Object.keys(own).length > 0 ? { ...rest, settings: own } : rest;
}

/** Whether two values are the same as JSON. */
export const same = (one: unknown, other: unknown) => JSON.stringify(one ?? null) === JSON.stringify(other ?? null);

/**
 * A text's own words in a language that is not the default: the language's own text, or a more general culture's
 * (`de` for `de-CH`), never the default's that visitors see in its place.
 */
export function ownText(texts: LocalizedText | null | undefined, culture: string): string {
  const key = keyIn(texts, culture);
  return key === '' ? '' : (texts?.[key] ?? '');
}

/**
 * A text in one language, written back where the language reads it from: in the default language the key it reads
 * (the default's own `''` when it has no key of its own), in another its own key, or the language's own new key where
 * it reads the default's. An empty text takes the key away. Every text keeps its default (`''`), as the server needs:
 * a text written in another language first gets an empty default beside it, and the default emptied stays while
 * other languages have a text; a text with nothing left in any language is none.
 */
export function withText(
  texts: LocalizedText | null | undefined,
  culture: string,
  text: string,
  isDefault: boolean,
): LocalizedText | undefined {
  const read = keyIn(texts, culture);
  const key = isDefault || read !== '' ? read : culture;
  const next = withKey(texts ?? {}, key, text === '' ? undefined : text);
  const others = Object.keys(next).filter((one) => one !== '');
  if (others.length === 0) return next[''] ? { '': next[''] } : undefined;
  return { '': next[''] ?? '', ...without(next, '') };
}

const decimal = /^-?\d+(\.\d+)?$/;

function inRange(value: number, rules: FieldSettingResponseModel['rules']): Problem | null {
  const { minimum, maximum, minimumIsExclusive, maximumIsExclusive } = rules;
  if (minimum != null) {
    const limit = Number(minimum);
    if (minimumIsExclusive ? value <= limit : value < limit)
      return { kind: 'below', limit: minimum, exclusive: minimumIsExclusive };
  }
  if (maximum != null) {
    const limit = Number(maximum);
    if (maximumIsExclusive ? value >= limit : value > limit)
      return { kind: 'above', limit: maximum, exclusive: maximumIsExclusive };
  }
  return null;
}

// A text's length in UTF-16 code units, as the server and the browser's own boxes count it.
function lengthOf(text: string, rules: FieldSettingResponseModel['rules']): Problem | null {
  const length = text.length;
  if (rules.minimumLength != null && length < rules.minimumLength)
    return { kind: 'tooShort', limit: rules.minimumLength };
  if (rules.maximumLength != null && length > rules.maximumLength)
    return { kind: 'tooLong', limit: rules.maximumLength };
  return null;
}

function countOf(count: number, rules: FieldSettingResponseModel['rules']): Problem | null {
  if (rules.minimumCount != null && count < rules.minimumCount) return { kind: 'tooFew', limit: rules.minimumCount };
  if (rules.maximumCount != null && count > rules.maximumCount) return { kind: 'tooMany', limit: rules.maximumCount };
  return null;
}

/**
 * What was typed into a setting's box, as its value: a whole number, a number in the wire form (ADR-0005), a time, a
 * text, or a list with one item a line. An empty box leaves the setting to its default, unless the setting is required.
 */
export function parseSetting(setting: FieldSettingResponseModel, raw: string): Parsed {
  const text = setting.kind === 'list' ? raw : raw.trim();
  if (text === '') return setting.rules.required ? refused({ kind: 'required' }) : ok(undefined);
  const { rules } = setting;
  const allowed = (value: string) => rules.allowedValues.length === 0 || rules.allowedValues.includes(value);
  switch (setting.kind) {
    case 'wholeNumber': {
      if (!/^-?\d+$/.test(text)) return refused({ kind: 'wholeNumber' });
      const value = Number(text);
      const problem = inRange(value, rules);
      return problem ? refused(problem) : ok(value);
    }
    case 'number': {
      const normal = text.replace(/^(-?)0+(?=\d)/, '$1');
      if (!decimal.test(normal)) return refused({ kind: 'number' });
      const problem = inRange(Number(normal), rules);
      return problem ? refused(problem) : ok(normal);
    }
    case 'time':
      return /^([01]\d|2[0-3]):[0-5]\d$/.test(text) ? ok(text) : refused({ kind: 'time' });
    case 'list': {
      const items = raw
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => line !== '');
      if (items.length === 0) return setting.rules.required ? refused({ kind: 'required' }) : ok(undefined);
      if (setting.itemKind === 'date' && items.some((item) => !isDate(item))) return refused({ kind: 'date' });
      const long = items.map((item) => lengthOf(item, rules)).find(Boolean);
      if (long) return refused(long);
      const count = countOf(items.length, rules);
      return count ? refused(count) : ok(items);
    }
    default: {
      const long = lengthOf(text, rules);
      if (long) return refused(long);
      return allowed(text) ? ok(text) : refused({ kind: 'notAllowed' });
    }
  }
}

/**
 * Checks a list of choices against the setting's count rules. None chosen leaves the setting to its default, so where
 * the default chooses some, one at least is needed: the boxes can't be emptied into the default's choices.
 */
export function parseChoices(setting: FieldSettingResponseModel, chosen: readonly string[]): Parsed {
  if (chosen.length === 0) {
    if (setting.rules.required) return refused({ kind: 'required' });
    const fewest = Math.max(
      setting.rules.minimumCount ?? 0,
      Array.isArray(setting.default) && setting.default.length > 0 ? 1 : 0,
    );
    return fewest > 0 ? refused({ kind: 'tooFew', limit: fewest }) : ok(undefined);
  }
  const count = countOf(chosen.length, setting.rules);
  return count ? refused(count) : ok([...chosen]);
}

/**
 * What is typed into a file's largest size: megabytes with up to two decimals, as the global settings take the site's
 * (ADR-0218), stored as bytes within the setting's limits; empty leaves the site's limit.
 */
export function parseFileSize(setting: FieldSettingResponseModel, raw: string): Parsed {
  const bytes = bytesOf(raw);
  if (bytes === null) return setting.rules.required ? refused({ kind: 'required' }) : ok(undefined);
  if (bytes === 'invalid') return refused({ kind: 'megabytes' });
  const most = setting.rules.maximum ? Number(setting.rules.maximum) : null;
  return most !== null && bytes > most
    ? refused({ kind: 'above', limit: megabytesOf(most), exclusive: false })
    : ok(bytes);
}

/** Whether a text is a date as settings store one: 2026-12-31. */
export function isDate(text: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(text)) return false;
  const date = new Date(`${text}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(text);
}

/** A date's limit as the inspector shows it: none, a date, or a number of days, weeks, months or years from today. */
export type DateBound =
  | { readonly kind: 'none' }
  | { readonly kind: 'date'; readonly date: string }
  | { readonly kind: 'today'; readonly count: number; readonly unit: 'd' | 'w' | 'm' | 'y' }
  | { readonly kind: 'steps'; readonly steps: string };

/** Reads a date's limit as settings store it: "2026-12-31", or { "today": "+2d" }. */
export function readDateBound(value: unknown): DateBound {
  if (typeof value === 'string' && isDate(value)) return { kind: 'date', date: value };
  const today = (value as { today?: unknown } | null | undefined)?.today;
  if (typeof today === 'string') {
    const one = /^([+-])(\d{1,4})([dwmy])$/.exec(today);
    if (one) {
      const [, sign, digits, unit] = one;
      return { kind: 'today', count: (sign === '-' ? -1 : 1) * Number(digits), unit: unit as 'd' | 'w' | 'm' | 'y' };
    }
    if (/^([+-]\d{1,4}[dwmy])+$/.test(today)) return { kind: 'steps', steps: today };
  }
  return { kind: 'none' };
}

/** Writes a date's limit as settings store it; undefined for none. */
export function writeDateBound(bound: DateBound): unknown {
  switch (bound.kind) {
    case 'none':
      return undefined;
    case 'date':
      return bound.date;
    case 'today':
      return { today: `${bound.count < 0 ? '-' : '+'}${Math.abs(bound.count)}${bound.unit}` };
    case 'steps':
      return { today: bound.steps };
  }
}
