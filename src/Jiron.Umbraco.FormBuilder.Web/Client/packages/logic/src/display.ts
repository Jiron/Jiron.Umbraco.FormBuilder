// The answers as merge tags show them (ROADMAP section 4.3.3, ADR-0066, ADR-0068): the texts a field's values show as,
// the same as each built-in type's Display in Core (FieldTypes/BuiltIn), and the default by the rule kind for a site's
// own type; the vectors of tests/vectors/merge-tags.json hold both runtimes to one behaviour.
import type { FieldFile } from './checks.ts';
import { formatCalendar, formatNumber, type CultureFormats } from './culture.ts';
import { own } from './errors.ts';
import type { Kind } from './rules.ts';
import type { MergeItem } from './template.ts';
import { isWellFormed, trim } from './text.ts';

/** An option as answers show it: its value and its label in the page's culture. */
export interface DisplayOption {
  readonly value: string;
  readonly label: string;
}

/** A grid's row as answers show it: its id and its statement in the page's culture. */
export interface DisplayRow {
  readonly id: string;
  readonly label: string;
}

/**
 * A field as its answers' texts read it: its type, the kind rules compare its answers as, whether it holds its value
 * as named parts, its public settings with their effective values and every text in the page's culture, the labels of
 * its options (a choice's options, a grid's columns, a country's or an address's countries) and a grid's rows.
 */
export interface DisplayedField {
  readonly type: string;
  /**
   * Whether the type is the built-in one of its alias, whose own display texts this module writes; a site's own type,
   * even one that replaces a built-in alias, shows by its rule kind.
   */
  readonly builtIn?: boolean;
  readonly kind?: Kind | null;
  /** Whether the type stores each value as named parts, such as a name or an address. */
  readonly parts?: boolean;
  readonly settings?: Readonly<Record<string, unknown>>;
  readonly options?: readonly DisplayOption[];
  readonly rows?: readonly DisplayRow[];
}

/** A value a field holds in the wire form (ADR-0005, ADR-0058), and for a grid the row it answers. */
export interface AnswerValue {
  readonly text: string;
  readonly row?: string | null;
}

/** The words a ticked box or a yes, and a no, show as in the page's culture. */
export interface DisplayWords {
  readonly yes: string;
  readonly no: string;
}

/** What the texts of one field are written with. */
interface Display {
  readonly field: DisplayedField;
  readonly words: DisplayWords;
  readonly culture: CultureFormats;
  readonly texts: MergeItem[];
}

type Settings = Readonly<Record<string, unknown>>;

const settingsOf = (field: DisplayedField): Settings => field.settings ?? {};

/** A setting that is text, or the empty text. */
const textSetting = (settings: Settings, name: string): string => {
  const value = own(settings, name);
  return typeof value === 'string' ? value : '';
};

/** Shows a text; an empty one shows nothing. */
function add(display: Display, text: string): void {
  if (text.length > 0) display.texts.push(text);
}

/** Shows a text with the number it stands for behind it. */
function addNumber(display: Display, text: string, number: string): void {
  if (text.length > 0) display.texts.push({ text, number });
}

/** Shows a text with the date, time or date-time it stands for behind it. */
function addDate(display: Display, text: string, date: string): void {
  if (text.length > 0) display.texts.push({ text, date });
}

/** A number in the wire form as the culture writes it, rounded and padded to `places` (0 to 15) when given. */
function number(display: Display, text: string, places: number | null = null): string {
  const clamped = places === null ? null : Math.min(Math.max(places, 0), 15);
  return formatNumber(text, clamped, display.culture) ?? text;
}

/** A date, time or date-time in the culture's short, time or date-and-time format. */
function date(display: Display, text: string): string {
  const format = text.length > 10 && text.charAt(10) === 'T' ? 'dateTime' : text.includes(':') ? 'time' : 'short';
  return formatCalendar(text, format, display.culture) ?? text;
}

/** The label of the option with this value; the value itself without one, such as a visitor's "Other" text. */
function optionLabel(display: Display, value: string): string {
  const label = display.field.options?.find((option) => option.value === value)?.label ?? '';
  return label.length > 0 ? label : value;
}

/** A text between an editor's prefix and suffix, each apart by a space when it has one. */
const around = (prefix: string, text: string, suffix: string): string =>
  [trim(prefix), text, trim(suffix)].filter((piece) => piece.length > 0).join(' ');

/**
 * The parts of a value with parts in the order the text writes them, as Core's FieldPartsJson.Read reads them: null
 * unless the text is a JSON object of at least one part, each with a name and a text that are not empty, no name twice
 * and no lone surrogate. The members are read from the text itself, since JSON.parse keeps a name's last text and puts
 * names that look like numbers first.
 */
export function valueParts(text: string): [string, string][] | null {
  let value: unknown;
  try {
    value = JSON.parse(text);
  } catch {
    return null;
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return null;
  // The text is one JSON object, so its members are strings, colons and commas between JSON whitespace.
  const found: [string, string][] = [];
  const names = new Set<string>();
  let at = space(text, text.indexOf('{') + 1);
  while (text.charAt(at) !== '}') {
    const name = stringAt(text, at);
    at = space(text, space(text, name.end) + 1);
    if (text.charAt(at) !== '"') return null;
    const part = stringAt(text, at);
    if (!isPart(name.text) || !isPart(part.text) || names.has(name.text)) return null;
    names.add(name.text);
    found.push([name.text, part.text]);
    at = space(text, part.end);
    if (text.charAt(at) === ',') at = space(text, at + 1);
  }
  return found.length === 0 ? null : found;
}

const isPart = (text: string): boolean => text.length > 0 && isWellFormed(text);

/** The index of the first character at or after `at` that is no JSON whitespace. */
function space(text: string, at: number): number {
  let i = at;
  while (i < text.length && ' \t\n\r'.includes(text.charAt(i))) i++;
  return i;
}

/** The JSON string that starts at `at`, decoded, and the index after its closing quote. */
function stringAt(text: string, at: number): { text: string; end: number } {
  let i = at + 1;
  while (i < text.length && text.charAt(i) !== '"') i += text.charAt(i) === '\\' ? 2 : 1;
  return { text: JSON.parse(text.slice(at, i + 1)) as string, end: i + 1 };
}

/** The default by the rule kind (Core's FieldDisplayDefaults). */
function byKind(display: Display, values: readonly AnswerValue[], files: readonly FieldFile[]): void {
  const { field } = display;
  values.forEach((value, i) => {
    const text = value.text;
    switch (field.kind) {
      case 'choice':
      case 'choices':
        add(display, optionLabel(display, text));
        break;
      case 'checkbox':
        add(display, text === 'true' ? display.words.yes : display.words.no);
        break;
      case 'files':
        if (files.length > 0) add(display, files[i]?.name ?? '');
        break;
      case 'number':
        addNumber(display, number(display, text), text);
        break;
      case 'date':
      case 'time':
      case 'dateTime':
        addDate(display, date(display, text), text);
        break;
      default: {
        const parts = field.parts === true ? valueParts(text) : null;
        add(display, parts === null ? text : parts.map(([, part]) => part).join(', '));
      }
    }
  });
}

/** The address's lines apart by commas: the postcode and the city together, a country code by its name. */
function address(display: Display, text: string): string {
  const parts = valueParts(text);
  if (parts === null) return text;
  const of = (name: string): string => parts.find(([part]) => part === name)?.[1] ?? '';
  const country = of('country');
  return [
    of('street'),
    of('street2'),
    [of('postcode'), of('city')].filter((piece) => piece.length > 0).join(' '),
    of('region'),
    optionLabel(display, country),
  ]
    .filter((line) => line.length > 0)
    .join(', ');
}

/** The built-in types that show more than the default, each as its type's Display in Core. */
const BUILT_IN: Readonly<Record<string, (display: Display, values: readonly AnswerValue[]) => void>> = {
  number: (display, values) => {
    const settings = settingsOf(display.field);
    for (const { text } of values) {
      const shown = around(textSetting(settings, 'prefix'), number(display, text), textSetting(settings, 'suffix'));
      addNumber(display, shown, text);
    }
  },
  slider: (display, values) => {
    const unit = textSetting(settingsOf(display.field), 'unit');
    for (const { text } of values) addNumber(display, around('', number(display, text), unit), text);
  },
  calculation: (display, values) => {
    const format = own(settingsOf(display.field), 'format');
    const settings: Settings = typeof format === 'object' && format !== null ? (format as Settings) : {};
    const decimals = own(settings, 'decimals');
    const places = typeof decimals === 'number' ? decimals : 2;
    for (const { text } of values) {
      const shown = around(
        textSetting(settings, 'prefix'),
        number(display, text, places),
        textSetting(settings, 'suffix'),
      );
      addNumber(display, shown, text);
    }
  },
  grid: (display, values) => {
    const scale = own(settingsOf(display.field), 'scale');
    const numbered = scale !== undefined && scale !== null;
    for (const row of display.field.rows ?? []) {
      const columns = values
        .filter((value) => value.row === row.id)
        .map(({ text }) => (numbered ? number(display, text) : optionLabel(display, text)));
      if (columns.length === 0) continue;
      const chosen = columns.join(' / ');
      add(display, row.label.length > 0 ? `${row.label}: ${chosen}` : chosen);
    }
  },
  yesno: (display, values) => {
    const settings = settingsOf(display.field);
    for (const { text } of values) {
      const yes = text === 'yes';
      const label = textSetting(settings, yes ? 'yesLabel' : 'noLabel');
      add(display, label.length > 0 ? label : yes ? display.words.yes : display.words.no);
    }
  },
  country: (display, values) => {
    for (const { text } of values) add(display, optionLabel(display, text));
  },
  name: (display, values) => {
    for (const { text } of values) {
      const parts = valueParts(text);
      add(display, parts === null ? text : parts.map(([, part]) => part).join(' '));
    }
  },
  address: (display, values) => {
    for (const { text } of values) add(display, address(display, text));
  },
};

/**
 * The texts a field's values show as in a culture (ADR-0066): a built-in type's as it writes them, a site's own type's by
 * its rule kind, a number or a date with its wire form behind the text. `files` are the files a field that stores
 * files holds, one for each value.
 */
export function displayAnswer(
  field: DisplayedField,
  values: readonly AnswerValue[],
  files: readonly FieldFile[],
  words: DisplayWords,
  culture: CultureFormats,
): MergeItem[] {
  const display: Display = { field, words, culture, texts: [] };
  const builtIn = field.builtIn === true ? own(BUILT_IN, field.type) : undefined;
  if (builtIn === undefined) byKind(display, values, files);
  else builtIn(display, values);
  return display.texts;
}
