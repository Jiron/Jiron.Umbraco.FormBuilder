// What a text that names a dictionary item (`#key`) shows in the designer (ROADMAP 14.1, ADR-0228): the keys the draft's
// texts name in the language shown, their items' texts as the server finds them, and the draft as the canvas draws it,
// each such text replaced by its item's. Pure but for the reader, which asks the server once per language and key.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { dictionaryTextGet } from '../api';
import type { LocalizedText } from './definition';
import { keyIn } from './preview/field-models';

/** A dictionary item's text in the language shown, or that the site has no such item. */
export interface DictionaryLookup {
  readonly found: boolean;
  /** The item's text there or in a language it falls back to; empty where none has one. */
  readonly text: string;
}

/** The items of the keys looked up, by key in lower case. */
export type DictionaryLookups = ReadonlyMap<string, DictionaryLookup>;

const KEY = /^#([A-Za-z0-9._-]+)$/;

/** The most keys the server takes in one request. */
const MOST_KEYS = 100;

/** The key a text names, as the server reads it (`#` and letters, digits, dots, dashes, underscores); null for none. */
export function dictionaryKeyOf(text: string | null | undefined): string | null {
  return text ? (KEY.exec(text)?.[1] ?? null) : null;
}

/**
 * What visitors in the language shown see for a text that names a dictionary item (ADR-0228), in the inspector's words:
 * the item's text, or that the item has none or doesn't exist, so the text shows as written; null for a text that
 * names none, or whose item is still being read.
 */
export function dictionaryLineOf(
  text: string,
  lookups: DictionaryLookups,
  term: (key: string, ...args: unknown[]) => string,
): string | null {
  const key = dictionaryKeyOf(text);
  const found = key ? lookups.get(key.toLowerCase()) : undefined;
  if (!key || !found) return null;
  if (!found.found) return term('dictionaryMissing', key, text);
  return found.text ? term('dictionaryShows', found.text) : term('dictionaryEmpty', text);
}

/** Whether a value is an editor's text in each language: a record with the default (`''`) whose values are all text. */
export function isLocalizedText(value: unknown): value is LocalizedText {
  if (!value || typeof value !== 'object' || Array.isArray(value) || !Object.hasOwn(value, '')) return false;
  return Object.values(value).every((text) => typeof text === 'string');
}

const textOf = (texts: LocalizedText, culture: string) => texts[keyIn(texts, culture)] ?? '';

/** The keys the texts in a value name in the language shown, anywhere inside it, in lower case. */
export function dictionaryKeysIn(value: unknown, culture: string, into = new Set<string>()): Set<string> {
  if (!value || typeof value !== 'object') return into;
  if (isLocalizedText(value)) {
    const key = dictionaryKeyOf(textOf(value, culture));
    if (key) into.add(key.toLowerCase());
    return into;
  }
  for (const inner of Array.isArray(value) ? value : Object.values(value)) dictionaryKeysIn(inner, culture, into);
  return into;
}

/** What a text shows in the language: the item's text for one that names an item with a text, else the text itself. */
export function shownText(
  texts: LocalizedText | null | undefined,
  culture: string,
  lookups: DictionaryLookups,
): string {
  if (!texts) return '';
  const text = textOf(texts, culture);
  const key = dictionaryKeyOf(text);
  const found = key ? lookups.get(key.toLowerCase()) : undefined;
  return found?.found && found.text !== '' ? found.text : text;
}

/**
 * A value as the canvas draws it: each text inside it that names an item with a text in the language shows the item's
 * text there. What changes nothing stays the same object, so the canvas keeps what it drew for it.
 */
export function withDictionary<T>(value: T, culture: string, lookups: DictionaryLookups): T {
  if (lookups.size === 0 || !value || typeof value !== 'object') return value;
  if (isLocalizedText(value)) {
    const key = keyIn(value, culture);
    const shown = shownText(value, culture, lookups);
    return shown === (value[key] ?? '') ? value : ({ ...value, [key]: shown } as T);
  }
  if (Array.isArray(value)) {
    let changed = false;
    const mapped = value.map((inner: unknown) => {
      const next = withDictionary(inner, culture, lookups);
      changed ||= next !== inner;
      return next;
    });
    return (changed ? mapped : value) as T;
  }
  let copy: Record<string, unknown> | null = null;
  for (const [name, inner] of Object.entries(value)) {
    const next = withDictionary(inner, culture, lookups);
    if (next !== inner) (copy ??= { ...(value as Record<string, unknown>) })[name] = next;
  }
  return (copy ?? value) as T;
}

/** Reads items' texts from the server, once per language and key, and keeps them for the designer's session. */
export class DictionaryReader {
  readonly #known = new Map<string, DictionaryLookup>();

  /** The lookups of these keys in the language, from the server for those not read yet; none where it fails. */
  async read(host: UmbControllerHost, keys: Iterable<string>, culture: string): Promise<DictionaryLookups> {
    const wanted = [...new Set([...keys].map((key) => key.toLowerCase()))];
    const missing = wanted.filter((key) => !this.#known.has(`${culture}|${key}`));
    for (let at = 0; at < missing.length; at += MOST_KEYS) {
      const asked = missing.slice(at, at + MOST_KEYS);
      const { data } = await tryExecute(host, dictionaryTextGet({ query: { key: asked, culture } }), {
        disableNotifications: true,
      });
      if (!data) continue;
      for (const each of data.texts) this.#known.set(`${culture}|${each.key.toLowerCase()}`, each);
    }
    const lookups = new Map<string, DictionaryLookup>();
    for (const key of wanted) {
      const known = this.#known.get(`${culture}|${key}`);
      if (known) lookups.set(key, { found: known.found, text: known.text });
    }
    return lookups;
  }

  /** Forgets every text read, so the next read asks the server again: after a dictionary item was picked, say. */
  forget() {
    this.#known.clear();
  }
}
