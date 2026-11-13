// What the designer adds (ROADMAP 9.3b, docs/ux/information-architecture.md section 5.1): a field of a type with what a
// new one starts with, a group, a page, the fields of pasted questions with a type guessed from their words, and the
// place a pick from the palette goes. Each goes through the editor model, which checks it as it checks every edit.
import type { DesignDefinition, DesignField, DesignGroup, DesignPage, DesignValidationRule } from './definition';
import { locate, validationRulesOf } from './definition';
import { insert, type Edit, type EditorState, type ModelContext, type Place } from './editor-model';

/** What a new field needs to know of its type. */
export interface NewFieldType {
  readonly alias: string;
  readonly holdsValue: boolean;
  readonly features: readonly string[];
}

/** The texts a new field starts with, in the backoffice's language, which the editor then changes. */
export interface NewFieldWords {
  /** An option's label: "Option 1". */
  readonly option: (number: number) => string;
  /** A grid's row: "Row 1". */
  readonly row: (number: number) => string;
}

/** How many options a new choice field starts with, and how many rows a grid. */
const NEW_OPTIONS = 3;
const NEW_ROWS = 2;

/**
 * An alias from a text: its words in camel case, letters and digits only, accents dropped and the sharp s spelled out,
 * from its first word that starts with a letter; the type's alias when nothing is left. The editor model makes it
 * unique when the field goes in.
 */
export function aliasOf(text: string, fallback: string): string {
  const words = text
    .replace(/\u00df/g, 'ss')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .split(/[^A-Za-z0-9]+/)
    .filter((word) => word.length > 0);
  // An alias starts with a letter: the words before the first that does go.
  const first = words.findIndex((word) => /^[A-Za-z]/.test(word));
  const camel = (first < 0 ? [] : words.slice(first))
    .map((word, at) => (at === 0 ? word.toLowerCase() : word.charAt(0).toUpperCase() + word.slice(1).toLowerCase()))
    .join('')
    .slice(0, 48);
  return /^[a-z][A-Za-z0-9]*$/.test(camel) ? camel : fallback;
}

/**
 * A field of a type, labelled. A choice field starts with three options and a grid with two rows as well, so that the
 * canvas shows it as it will be used; a heading starts with its label as its text; content without a value has no label.
 * A consent field starts required, as consent that a visitor may leave out is no basis for keeping the entry (section
 * 4.3.2); an editor who means an optional agreement, such as a newsletter's, turns it off.
 */
export function newField(type: NewFieldType, label: string, newKey: () => string, words: NewFieldWords): DesignField {
  const options = type.features.includes('options')
    ? {
        options: {
          source: 'items',
          items: Array.from({ length: NEW_OPTIONS }, (_, at) => ({
            value: `option${at + 1}`,
            label: { '': words.option(at + 1) },
          })),
        },
      }
    : {};
  const rows = type.features.includes('rows')
    ? { rows: Array.from({ length: NEW_ROWS }, (_, at) => ({ key: newKey(), label: { '': words.row(at + 1) } })) }
    : {};
  return {
    key: newKey(),
    type: type.alias,
    ...(type.holdsValue ? { alias: aliasOf(label, type.alias), label: { '': label } } : {}),
    ...(type.alias === 'heading' ? { settings: { text: { '': label } } } : {}),
    ...(type.alias === 'consent' ? { required: true } : {}),
    ...(type.features.includes('childFields') ? { fields: [] } : {}),
    ...options,
    ...rows,
  };
}

/** An empty group. */
export function newGroup(newKey: () => string): DesignGroup {
  return { key: newKey(), fields: [] };
}

/** An empty page, with the empty group that every page holds. */
export function newPage(newKey: () => string): DesignPage {
  return { key: newKey(), groups: [newGroup(newKey)] };
}

/**
 * Where a pick from the palette goes: the open insert point; else after the selected field, at the end of the selected
 * group, or at the end of the selected page; else at the end of the page shown. Null when that page has no group.
 */
export function placeFor(
  definition: DesignDefinition,
  insertAt: Place | null,
  selection: string | null,
  shownPage: number,
): Place | null {
  if (insertAt) return insertAt;
  const found = selection ? locate(definition, selection) : undefined;
  if (found?.kind === 'field') return { container: found.container.key, index: found.index + 1 };
  if (found?.kind === 'group') return { container: found.element.key, index: found.element.fields.length };
  const page = found?.kind === 'page' ? found.element : definition.pages[shownPage];
  const last = page?.groups.at(-1);
  return last ? { container: last.key, index: last.fields.length } : null;
}

/**
 * Adds fields at a place, in their order, as one edit that selects the last; with no place, in a new group at the end
 * of the page. Refused as the first field that can't go in is refused.
 */
export function addFields(
  state: EditorState,
  fields: readonly DesignField[],
  place: Place | null,
  pageKey: string,
  context: ModelContext,
): Edit {
  if (fields.length === 0) return { ok: false, refusal: 'unchanged' };
  if (!place) {
    const page = state.definition.pages.find((each) => each.key === pageKey);
    if (!page) return { ok: false, refusal: 'missing' };
    const group = newGroup(context.newKey);
    const added = insert(state, 'group', group, { container: page.key, index: page.groups.length }, context);
    if (!added.ok) return added;
    return addFields(added.state, fields, { container: group.key, index: 0 }, pageKey, context);
  }
  let current = state;
  for (const [at, field] of fields.entries()) {
    const edit = insert(current, 'field', field, { container: place.container, index: place.index + at }, context);
    if (!edit.ok) return edit;
    current = edit.state;
  }
  return { ok: true, state: current };
}

/**
 * Adds a group, a new empty one or one given, such as a saved group's fresh copy with the validation rules of its fields:
 * after the group of the place given, else at the end of the page shown, selected; one step, so Undo takes both. A place
 * in a repeatable group puts it after the group that holds the repeatable group.
 */
export function addGroup(
  state: EditorState,
  place: Place | null,
  pageKey: string,
  context: ModelContext,
  group: DesignGroup = newGroup(context.newKey),
  checks: readonly DesignValidationRule[] = [],
): Edit {
  const added = placeGroup(state, place, pageKey, context, group);
  if (!added.ok || checks.length === 0) return added;
  const definition = added.state.definition;
  return {
    ...added,
    state: {
      ...added.state,
      definition: { ...definition, validationRules: [...validationRulesOf(definition), ...checks] },
    },
  };
}

function placeGroup(
  state: EditorState,
  place: Place | null,
  pageKey: string,
  context: ModelContext,
  group: DesignGroup,
): Edit {
  const definition = state.definition;
  let found = place?.container ? locate(definition, place.container) : undefined;
  if (found?.kind === 'field') found = locate(definition, found.container.key);
  const target =
    found?.kind === 'group'
      ? { container: found.container.key, index: found.index + 1 }
      : found?.kind === 'page'
        ? { container: found.element.key, index: place?.index ?? found.element.groups.length }
        : {
            container: pageKey,
            index: definition.pages.find((page) => page.key === pageKey)?.groups.length ?? 0,
          };
  return insert(state, 'group', group, target, context);
}

/** A question pasted on a line, with the field type guessed for it. */
export interface PastedQuestion {
  readonly label: string;
  readonly type: string;
}

/** The most questions one paste turns into fields. */
export const MOST_PASTED = 50;

// A list's marker before a question: "1.", "2)", "a)", "-", "*", a bullet or a dash.
const MARKER = /^\s*(?:\d{1,3}[.)]|[a-z][.)]|[-*\u2022\u2013\u2014])\s+/i;

/** The questions of a pasted text: one per line, without list markers, empty lines left out, the first 50. */
export function questionsOf(text: string, available: ReadonlySet<string>): PastedQuestion[] {
  return text
    .split(/\r?\n/)
    .map((line) => line.replace(MARKER, '').replace(/\s+/g, ' ').trim())
    .filter((line) => line.length > 0)
    .slice(0, MOST_PASTED)
    .map((label) => {
      const guessed = guessType(label);
      return { label: label.slice(0, 500), type: available.has(guessed) ? guessed : 'text' };
    });
}

// The words that make a type likely, in English and German, the more particular first: an email address is no address,
// a phone number no number.
const GUESSES: readonly (readonly [string, RegExp])[] = [
  ['email', /\be-?mail\b|\be-?mail-?adresse\b/],
  ['phone', /\b(phone|telephone|mobile|cell ?phone|telefon\w*|handy\w*|mobil\w*|rufnummer)\b/],
  ['url', /\b(website|web ?site|web address|url|homepage|webseite|internetseite|internetadresse)\b/],
  ['signature', /\b(signature|sign here|unterschrift|unterschreiben)\b/],
  ['file', /\b(upload|attach\w*|files?|cv|resume|datei\w*|anhang|anh[a\u00e4]nge|hochladen|lebenslauf)\b/],
  ['consent', /\b(i agree|agree to|consent|accept the|einverstanden|stimme\b.*\bzu|zustimm\w*|datenschutz\w*)\b/],
  ['date', /\b(date|birthday|date of birth|datum|geburtstag|geburtsdatum)\b/],
  ['time', /\b(time|uhrzeit)\b/],
  ['address', /\b(address|street|anschrift|adresse|stra(\u00df|ss)e)\b/],
  ['country', /\b(country|land)\b/],
  ['rating', /\b(rate|rating|how satisfied|bewerten|bewertung|wie zufrieden)\b/],
  ['number', /\b(how many|number of|quantity|amount|age|anzahl|wie viele|menge|alter)\b/],
  [
    'yesno',
    /^(do|does|did|are|is|was|were|have|has|had|can|could|will|would|should|may|haben|hast|habt|sind|bist|ist|war|waren|kann|kannst|k\u00f6nnen|k\u00f6nnten|m\u00f6chten|m\u00f6chtest|wollen|willst|darf|d\u00fcrfen|brauchen|brauchst|ben\u00f6tigen)\b.*\?$/,
  ],
  [
    'textarea',
    /\b(message|comments?|notes?|anything else|describe|description|details|feedback|nachricht|kommentar\w*|anmerkung\w*|beschreib\w*|sonst noch|mitteilung)\b/,
  ],
];

/** The field type a question's words suggest; a short text when none does. */
export function guessType(question: string): string {
  const text = question.toLowerCase();
  return GUESSES.find(([, words]) => words.test(text))?.[0] ?? 'text';
}
