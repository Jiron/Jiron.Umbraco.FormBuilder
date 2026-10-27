// The built-in field types' checks (ROADMAP section 4.3.3, ADR-0062), the same as each built-in type's Validate in
// Core (FieldTypes/BuiltIn), and the field's own checks that need no other entry, the same as Core's FieldValues; the
// vectors of tests/vectors/validation.json hold both runtimes to one behaviour.
import { applyRelative, parseDate, parseRelative, parseTime, today as todayIn } from './dates.ts';
import { compareText, fits, isMultiple, parseDecimal, subtract, type Decimal } from './decimal.ts';
import { LogicError, own } from './errors.ts';
import { isEmailAddress, isPhoneNumber, isWebAddress } from './forms.ts';
import { compilePattern, type Pattern } from './pattern.ts';
import { sameValues, type Context, type Kind } from './rules.ts';
import { countWords, fold } from './text.ts';

/** One of a field's own checks, as the public projection writes its `validation` list (ADR-0002). */
export interface FieldCheck {
  readonly kind: string;
  /** A `pattern` check's pattern, in the safe subset (ADR-0005). */
  readonly pattern?: string;
  /** A `matchesField` check's other field, by id. */
  readonly field?: string;
}

/** A grid's row as its checks read it: its id, and whether it needs an answer when the grid is not required. */
export interface CheckedRow {
  readonly id: string;
  readonly required?: boolean;
}

/**
 * An option as a field's checks read it (ADR-0077): its value, or its value with the parent answers it belongs to; one
 * that names no parent values belongs to every answer.
 */
export type CheckedOption = string | { readonly value: string; readonly parentValues?: readonly string[] };

/** A field as its checks read it: its type, its rule kind, whether it is required, its settings, options and checks. */
export interface CheckedField {
  readonly type: string;
  readonly kind?: Kind;
  /** Whether the field is required, by its own setting or by a rule. */
  readonly required?: boolean;
  /** The public settings with their effective values, as the public projection writes them; a missing one sets no limit. */
  readonly settings?: Readonly<Record<string, unknown>>;
  /**
   * The options the answers are held to (ADR-0077): the field's own, or those its list or another source resolved for
   * the page; absent when its source could not give them, and then any answer is taken, as on the server.
   */
  readonly options?: readonly CheckedOption[];
  /** The id of the field whose answers filter the options (cascading); an option belongs to the answers it names. */
  readonly parent?: string;
  /** A grid's rows. */
  readonly rows?: readonly CheckedRow[];
  /** The field's own checks, in their order. */
  readonly checks?: readonly FieldCheck[];
}

/** A file a visitor chose: its name, as the device gave it, and its size in bytes. */
export interface FieldFile {
  readonly name: string;
  readonly size: number;
}

/** What a visitor gave one field (ADR-0058), cleaned: its values in the wire form, its named parts and its files. */
export interface FieldAnswers {
  readonly values: readonly string[];
  readonly parts?: Readonly<Record<string, readonly string[]>>;
  /** For a field that stores files, the file each value stands for, in order. */
  readonly files?: readonly FieldFile[];
}

/** A field's errors: the error kinds of its type's checks in the order they are reported, and its own failing checks. */
export interface FieldErrors {
  readonly errors: readonly string[];
  readonly checks: readonly number[];
}

type Settings = Readonly<Record<string, unknown>>;

/** What a type's check reads, and where it reports. */
interface Validation {
  /** The first value. */
  readonly value: string;
  /** The values, as Core reads the answers. */
  readonly values: readonly string[];
  readonly answers: FieldAnswers;
  readonly settings: Settings;
  /** The values of the options the answers are held to; null when the field's source could not give them. */
  readonly options: ReadonlySet<string> | null;
  /** A grid's rows; none for any other field. */
  readonly rows: readonly CheckedRow[];
  readonly required: boolean;
  readonly today: () => number;
  fail(kind: string): void;
}

const number = (settings: Settings, name: string): number | undefined =>
  typeof settings[name] === 'number' ? (settings[name] as number) : undefined;
const text = (settings: Settings, name: string): string | undefined =>
  typeof settings[name] === 'string' ? (settings[name] as string) : undefined;
const texts = (settings: Settings, name: string): readonly string[] =>
  Array.isArray(settings[name])
    ? (settings[name] as unknown[]).filter((item): item is string => typeof item === 'string')
    : [];
const part = (answers: FieldAnswers, name: string): readonly string[] => own(answers.parts ?? {}, name) ?? [];

/** A text with its ASCII capitals made small and every other character left as it is. */
const asciiLower = (value: string): string =>
  value.replace(/[A-Z]/g, (letter) => String.fromCharCode(letter.charCodeAt(0) + 32));

/** Fails minLength or maxLength for a text whose UTF-16 code units fall outside the limits. */
function length(v: Validation): void {
  const min = number(v.settings, 'minLength');
  const max = number(v.settings, 'maxLength');
  if (min !== undefined && v.value.length < min) v.fail('minLength');
  else if (max !== undefined && v.value.length > max) v.fail('maxLength');
}

/** Fails minWords or maxWords for a text whose words fall outside the limits. */
function words(v: Validation): void {
  const count = countWords(v.value);
  const min = number(v.settings, 'minWords');
  const max = number(v.settings, 'maxWords');
  if (min !== undefined && count < min) v.fail('minWords');
  else if (max !== undefined && count > max) v.fail('maxWords');
}

/**
 * Fails min or max for a number outside its limits, and step for one off its step, counted from the minimum or 0. A
 * limit or a step that is no number, or a step of 0, limits nothing: publishing refuses them, and a draft's preview
 * must not fail on them.
 */
function range(v: Validation, step: string | undefined): void {
  const min = text(v.settings, 'min');
  const max = text(v.settings, 'max');
  if (min !== undefined && (compareText(v.value, min) ?? 0) < 0) v.fail('min');
  else if (max !== undefined && (compareText(v.value, max) ?? 0) > 0) v.fail('max');
  const value = parseDecimal(v.value);
  const from = parseDecimal(min ?? '0');
  const by = step === undefined ? null : parseDecimal(step);
  if (value === null || from === null || by === null || by.n === 0n) return;
  if (!isMultiple(subtract(value, from), by)) v.fail('step');
}

/** The step of a number field without one: 10 to the power of minus its decimals, so 1 without decimals. */
const stepOfPlaces = (decimals: number): string => (decimals === 0 ? '1' : `0.${'0'.repeat(decimals - 1)}1`);

/** Whether a number in the wire form is a whole number from low to high; a missing bound limits nothing. */
function isWholeNumber(value: string, low: number | undefined, high: number | undefined): boolean {
  const d: Decimal | null = parseDecimal(value);
  if (d === null || d.s !== 0) return false;
  return (low === undefined || d.n >= BigInt(low)) && (high === undefined || d.n <= BigInt(high));
}

/** The day number of a limit: undefined for none, null for one outside the calendar or no limit at all. */
function bound(limit: unknown, today: () => number): number | null | undefined {
  if (limit === undefined || limit === null) return undefined;
  if (typeof limit === 'string') return parseDate(limit);
  if (typeof limit === 'object' && typeof (limit as { today?: unknown }).today === 'string') {
    const steps = parseRelative((limit as { today: string }).today);
    return steps === null ? null : applyRelative(today(), steps);
  }
  return null;
}

/** Fails dateWindow for a date outside the limits, on a weekend when the field takes weekdays only, or excluded. */
function dateWindow(v: Validation, day: number): void {
  const low = bound(v.settings['min'], v.today);
  const high = bound(v.settings['max'], v.today);
  // Day 0 is 1970-01-01, a Thursday: 0 is Sunday and 6 Saturday.
  const weekday = (((day + 4) % 7) + 7) % 7;
  const fits =
    low !== null &&
    high !== null &&
    (low === undefined || day >= low) &&
    (high === undefined || day <= high) &&
    !(v.settings['weekdaysOnly'] === true && (weekday === 0 || weekday === 6)) &&
    !texts(v.settings, 'excluded').some((date) => parseDate(date) === day);
  if (!fits) v.fail('dateWindow');
}

/** Whether a time, in seconds since midnight, keeps a step of minutes counted from a start; any time without one. */
const onMinuteStep = (seconds: number, from: number, stepMinutes: number | undefined): boolean =>
  stepMinutes === undefined || (seconds - from) % (stepMinutes * 60) === 0;

/** Fails invalid unless each of the answers' own values is an option; the "Other" text, a part, is the visitor's own. */
function amongOptions(v: Validation): void {
  const options = v.options;
  if (options !== null && !v.answers.values.every((value) => options.has(value))) v.fail('invalid');
}

/** Fails selections for a number of values below the fewest or above the most. */
function selections(v: Validation): void {
  const min = number(v.settings, 'minSelections');
  const max = number(v.settings, 'maxSelections');
  const count = v.values.length;
  if ((min !== undefined && count < min) || (max !== undefined && count > max)) v.fail('selections');
}

/**
 * Fails required when a part the value needs has no text (ADR-0056): the listed parts the field asks for, or, when it
 * lists none, the given parts once the field is required.
 */
function requiredParts(v: Validation, asked: readonly string[], unlisted: readonly string[]): void {
  const listed = texts(v.settings, 'requiredParts');
  const needed = listed.length > 0 ? listed.filter((name) => asked.includes(name)) : v.required ? unlisted : [];
  if (needed.some((name) => part(v.answers, name).length === 0)) v.fail('required');
}

/** The parts an address always asks for. */
const ADDRESS = ['street', 'postcode', 'city'];

/** The extensions of each group of file types (ADR-0062). SVG is no image here, because it can carry script. */
const FILE_TYPES: ReadonlyMap<string, readonly string[]> = new Map([
  ['images', ['jpg', 'jpeg', 'png', 'gif', 'webp', 'avif', 'heic', 'heif']],
  ['pdf', ['pdf']],
  ['office', ['doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'odt', 'ods', 'odp', 'rtf']],
  ['text', ['txt', 'csv']],
  ['archives', ['zip', '7z', 'rar', 'tar', 'gz']],
]);

/** A file name's extension: the text after its last point, in ASCII lowercase; empty without a point. */
function extensionOf(name: string): string {
  const point = name.lastIndexOf('.');
  return point < 0 ? '' : asciiLower(name.slice(point + 1));
}

/** Each built-in type's check of a shown field's readable answers, in the order Core's reports. */
const CHECKS: Readonly<Record<string, (v: Validation) => void>> = {
  text: length,
  textarea(v) {
    length(v);
    words(v);
  },
  email(v) {
    if (!isEmailAddress(v.value)) v.fail('invalid');
    // The domains stay on the server, which alone refuses them (ADR-0062).
    if (v.settings['confirm'] !== true) return;
    const again = part(v.answers, 'confirm');
    if (again.length !== 1 || fold(again[0] ?? '') !== fold(v.value)) v.fail('confirmation');
  },
  phone(v) {
    if (!isPhoneNumber(v.value)) v.fail('invalid');
  },
  url(v) {
    const colon = v.value.indexOf(':');
    const scheme = colon < 0 ? null : asciiLower(v.value.slice(0, colon));
    if (Array.isArray(v.settings['schemes']) && !texts(v.settings, 'schemes').some((allowed) => allowed === scheme)) {
      v.fail('invalid');
    }
    if (!isWebAddress(v.value)) v.fail('invalid');
  },
  number(v) {
    const decimals = number(v.settings, 'decimals');
    range(v, text(v.settings, 'step') ?? (decimals === undefined ? undefined : stepOfPlaces(decimals)));
  },
  slider(v) {
    range(v, text(v.settings, 'step'));
  },
  rating(v) {
    const count = number(v.settings, 'count');
    if (count !== undefined && !isWholeNumber(v.value, 1, count)) v.fail('invalid');
  },
  scale(v) {
    const from = number(v.settings, 'from');
    const to = number(v.settings, 'to');
    if (from !== undefined && to !== undefined && !isWholeNumber(v.value, from, to)) v.fail('invalid');
  },
  date(v) {
    const day = parseDate(v.value);
    if (day !== null) dateWindow(v, day);
  },
  time(v) {
    const seconds = parseTime(v.value);
    if (seconds === null) return;
    const min = text(v.settings, 'min');
    const max = text(v.settings, 'max');
    const earliest = min === undefined ? null : parseTime(min);
    const latest = max === undefined ? null : parseTime(max);
    if (earliest !== null && seconds < earliest) v.fail('min');
    else if (latest !== null && seconds > latest) v.fail('max');
    if (!onMinuteStep(seconds, earliest ?? 0, number(v.settings, 'stepMinutes'))) v.fail('step');
  },
  datetime(v) {
    const day = parseDate(v.value.slice(0, 10));
    const seconds = parseTime(v.value.slice(11));
    if (day === null || seconds === null) return;
    dateWindow(v, day);
    if (!onMinuteStep(seconds, 0, number(v.settings, 'stepMinutes'))) v.fail('step');
  },
  dropdown(v) {
    amongOptions(v);
    if (v.settings['multiple'] === true) selections(v);
  },
  radio: amongOptions,
  checkboxes(v) {
    amongOptions(v);
    selections(v);
  },
  grid(v) {
    // The columns: the scale's whole numbers when the grid has one, otherwise its options.
    const scale = v.settings['scale'];
    const options = v.options;
    let column: ((value: string) => boolean) | null = null;
    if (typeof scale === 'object' && scale !== null) {
      const from = number(scale as Settings, 'from');
      const to = number(scale as Settings, 'to');
      column = (value) => isWholeNumber(value, from, to);
    } else if (options !== null) {
      column = (value) => options.has(value);
    }
    if (column !== null && !v.values.every(column)) v.fail('invalid');
    // The rows it needs answered: every row of a required grid, otherwise the rows marked required (ADR-0069).
    const needed = v.rows.filter((row) => v.required || row.required === true);
    if (needed.some((row) => part(v.answers, row.id).length === 0)) v.fail('required');
  },
  ranking(v) {
    // Each option once, and without script each position once (ADR-0069).
    const positions = Object.values(v.answers.parts ?? {}).map((texts) => Number(texts[0]));
    if (new Set(v.values).size !== v.values.length || new Set(positions).size !== positions.length) v.fail('invalid');
    const options = v.options;
    if (options === null) return;
    const top = number(v.settings, 'top');
    const ranked = top === undefined ? options.size : Math.min(top, options.size);
    if (v.values.length !== ranked || !v.values.every((value) => options.has(value))) v.fail('invalid');
  },
  country(v) {
    const allowed = texts(v.settings, 'allowed');
    if (allowed.length > 0 && !v.values.every((value) => allowed.includes(value))) v.fail('invalid');
  },
  name(v) {
    const asked = texts(v.settings, 'parts');
    requiredParts(v, asked, asked);
  },
  address(v) {
    requiredParts(v, [...ADDRESS, ...texts(v.settings, 'parts')], ADDRESS);
  },
  file(v) {
    const files = v.answers.files ?? [];
    const most = number(v.settings, 'maxFiles');
    if (most !== undefined && v.values.length > most) v.fail('fileCount');
    const types = texts(v.settings, 'types');
    const extensions = texts(v.settings, 'extensions');
    const groups = types.length === 0 && extensions.length === 0 ? ['images', 'pdf'] : types;
    const taken = new Set([...groups.flatMap((group) => FILE_TYPES.get(group) ?? []), ...extensions]);
    if (files.some((file) => !taken.has(extensionOf(file.name)))) v.fail('fileType');
    const bytes = number(v.settings, 'maxBytes');
    if (bytes !== undefined && files.some((file) => file.size > bytes)) v.fail('fileSize');
  },
};

/** The "Other" text of a field that offers "Other": one text in the part `other` (ADR-0058). */
function otherText(answers: FieldAnswers, settings: Settings): string | undefined {
  const other = part(answers, 'other');
  return settings['other'] === true && other.length === 1 ? other[0] : undefined;
}

/** The texts of the parts a field asks for, in order. */
const partsOf = (answers: FieldAnswers, asked: readonly string[]): string[] =>
  asked.flatMap((name) => part(answers, name));

/**
 * The values of the types whose answers have parts, as Core reads answers it can read (ADR-0058): the "Other" text in
 * place of an option on radio buttons and after the options on checkboxes, each grid row's values, a ranking's options
 * that were given positions without script (ADR-0069; their order is the server's to store, and no check reads it),
 * and the texts of the parts of a name or an address that the field asks for. Every other type's values are its
 * answers' own.
 */
const READ: Readonly<
  Record<string, (field: CheckedField, answers: FieldAnswers, settings: Settings) => readonly string[]>
> = {
  radio(_, answers, settings) {
    const other = otherText(answers, settings);
    return answers.values.length === 0 && other !== undefined ? [other] : answers.values;
  },
  checkboxes(_, answers, settings) {
    const other = otherText(answers, settings);
    return other === undefined || answers.values.includes(other) ? answers.values : [...answers.values, other];
  },
  grid: (field, answers) => (field.rows ?? []).flatMap((row) => part(answers, row.id)),
  ranking: (_, answers) =>
    Object.keys(answers.parts ?? {}).length === 0 ? answers.values : Object.keys(answers.parts ?? {}),
  name: (_, answers, settings) => partsOf(answers, texts(settings, 'parts')),
  address: (_, answers, settings) => partsOf(answers, [...ADDRESS, ...texts(settings, 'parts')]),
};

/** Whether a number in the wire form keeps the digits a stored number holds exactly; any other text is the type's to check. */
function fitsStored(value: string): boolean {
  const d = parseDecimal(value);
  return d === null || fits(d);
}

/** A pattern compiled, or null for one outside the safe subset, which publishing refuses and which checks nothing. */
function compiled(pattern: string): Pattern | null {
  try {
    return compilePattern(pattern);
  } catch (error) {
    if (error instanceof LogicError) return null;
    throw error;
  }
}

/**
 * The indexes of the field's own checks that fail, of those that need no other entry (ADR-0062): every value matches
 * the whole pattern, and the values match the other field's, which has none while it is hidden. `uniquePerForm` is the
 * server's.
 */
function ownChecks(
  field: CheckedField,
  values: readonly string[],
  others: Readonly<Record<string, readonly string[]>>,
): number[] {
  const failing: number[] = [];
  (field.checks ?? []).forEach((check, index) => {
    let holds = true;
    if (check.kind === 'pattern') {
      const pattern = compiled(check.pattern ?? '');
      holds = pattern === null || values.every((value) => pattern.test(value));
    } else if (check.kind === 'matchesField') {
      holds = sameValues(field.kind, values, own(others, check.field ?? '') ?? []);
    }
    if (!holds) failing.push(index);
  });
  return failing;
}

/**
 * The values of the options a field's answers are held to (ADR-0077): with a parent, only those that belong to the
 * parent's answers, an option that names no parent value belonging to every answer; null when the field has none to
 * give, as a source that could not give its options.
 */
function offered(field: CheckedField, values: Readonly<Record<string, readonly string[]>>): ReadonlySet<string> | null {
  if (field.options === undefined) return null;
  const answers = field.parent === undefined ? null : (own(values, field.parent) ?? []);
  const kept = new Set<string>();
  for (const option of field.options) {
    const [value, belongsTo] = typeof option === 'string' ? [option, []] : [option.value, option.parentValues ?? []];
    if (answers === null || belongsTo.length === 0 || belongsTo.some((each) => answers.includes(each))) kept.add(value);
  }
  return kept;
}

/**
 * The errors of a field the rules leave visible (ADR-0058, ADR-0062): `required` when it has no answer and is
 * required, or has a row marked required (ADR-0069); `digits`, or `invalid` for a type without that kind, alone when
 * it compares as a number and a value has more digits than a stored number keeps exactly (ADR-0003); otherwise what
 * its type's check reports, each kind once, and the field's own checks that fail. A type without a check here has
 * none in the browser, and the server decides. Today comes from the context's instant and time zone, read only by a
 * date check; `values` are the form's other fields' values by id, as the rules see them, of which a cascading field's
 * parent's filter its options.
 */
export function checkField(
  field: CheckedField,
  answers: FieldAnswers,
  context: Context,
  values: Readonly<Record<string, readonly string[]>> = {},
): FieldErrors {
  const settings = field.settings ?? {};
  const rows = field.rows ?? [];
  const read = own(READ, field.type)?.(field, answers, settings) ?? answers.values;
  if (read.length === 0) {
    return {
      errors: field.required === true || rows.some((row) => row.required === true) ? ['required'] : [],
      checks: [],
    };
  }
  if (field.kind === 'number' && read.some((value) => !fitsStored(value))) {
    return { errors: [field.type === 'number' ? 'digits' : 'invalid'], checks: [] };
  }
  const errors: string[] = [];
  let day: number | undefined;
  own(
    CHECKS,
    field.type,
  )?.({
    value: read[0] ?? '',
    values: read,
    answers,
    settings,
    options: offered(field, values),
    rows,
    required: field.required === true,
    today: () => (day ??= todayIn(context.now, context.timeZone)),
    fail(kind) {
      if (!errors.includes(kind)) errors.push(kind);
    },
  });
  return { errors, checks: ownChecks(field, read, values) };
}
