// The visitor messages (ROADMAP 6.2g, ADR-0099), the same as Core's VisitorMessages: the package's default message for
// a visitor's error, a sentence of the page's language with the values the field's settings give, printed with the
// culture's formats (ADR-0064); the error summary's heading and its links to a default message. The vectors of
// tests/vectors/messages.json hold both runtimes to one text. A field's, a check's or a rule's own message comes first;
// this is what shows without one. The words of each language are a table of their own (lang/), so a page loads only
// its own.
import { bound, FILE_TYPES } from './checks.ts';
import { formatCalendar, formatNumber, type CultureFormats } from './culture.ts';
import { formatDate, parseDate } from './dates.ts';
import type { DisplayWords } from './display.ts';
import { isEmailAddress } from './forms.ts';
import { clean, isTrimmed } from './text.ts';

/** A message that counts: the words for one and for any other number. */
export interface PluralWords {
  readonly one: string;
  readonly other: string;
}

/** The words of one language: each a whole sentence with named values in braces (Core's VisitorMessageWords). */
export interface MessageWords {
  readonly or: string;
  readonly kilobytes: string;
  readonly megabytes: string;
  readonly answer: string;
  readonly email: string;
  readonly phone: string;
  readonly url: string;
  readonly number: string;
  readonly date: string;
  readonly time: string;
  readonly dateTime: string;
  readonly rating: string;
  readonly scale: string;
  readonly value: string;
  readonly choose: string;
  readonly chooseSeveral: string;
  readonly gridRows: string;
  readonly gridRow: string;
  readonly order: string;
  readonly tick: string;
  readonly yesNo: string;
  readonly agree: string;
  readonly country: string;
  readonly name: string;
  readonly address: string;
  readonly file: string;
  readonly sign: string;
  readonly answerThis: string;
  readonly emailFormat: string;
  readonly emailDomain: string;
  readonly phoneFormat: string;
  readonly urlFormat: string;
  readonly numberFormat: string;
  readonly dateFormat: string;
  readonly timeFormat: string;
  readonly dateTimeFormat: string;
  readonly ratingRange: string;
  readonly scaleRange: string;
  readonly valueRange: string;
  readonly options: string;
  readonly countryList: string;
  readonly nameCheck: string;
  readonly addressCheck: string;
  readonly signAgain: string;
  readonly checkThis: string;
  readonly minLength: PluralWords;
  readonly maxLength: PluralWords;
  readonly minWords: PluralWords;
  readonly maxWords: PluralWords;
  readonly numberBetween: string;
  readonly numberFrom: string;
  readonly numberUpTo: string;
  readonly timeBetween: string;
  readonly timeFrom: string;
  readonly timeUpTo: string;
  readonly numberStep: string;
  readonly numberStepFrom: string;
  readonly wholeNumber: string;
  readonly places: PluralWords;
  readonly valueStep: string;
  readonly timeStep: PluralWords;
  readonly timeStepFrom: PluralWords;
  readonly dateBetween: string;
  readonly dateFrom: string;
  readonly dateUpTo: string;
  readonly weekdayBetween: string;
  readonly weekdayFrom: string;
  readonly weekdayUpTo: string;
  readonly weekday: string;
  readonly dateUnavailable: string;
  readonly selectionsExactly: PluralWords;
  readonly selectionsBetween: string;
  readonly selectionsAtLeast: PluralWords;
  readonly selectionsAtMost: PluralWords;
  readonly fileFormat: string;
  readonly fileTooLarge: string;
  readonly fileLargerThan: string;
  readonly filesLargerThan: string;
  readonly filesAtMost: PluralWords;
  readonly optionFull: string;
  readonly choiceFull: string;
  readonly confirmation: string;
  readonly digits: string;
  readonly rowsAtLeast: PluralWords;
  readonly rowsAtMost: PluralWords;
  readonly pattern: string;
  readonly sameAs: string;
  readonly taken: string;
  readonly empty: string;
  readonly summaryHeading: PluralWords;
  readonly summaryEntry: string;
  /** A summary link after a label that already ends in a question mark, an exclamation mark, a full stop or a colon. */
  readonly summaryMarkedEntry: string;
  readonly summaryRowEntry: string;
}

/** An option of a field as its messages name it: its value and its label in the page's culture. */
export interface MessageOption {
  readonly value: string;
  readonly label: string;
}

/** A field as its messages read it: as the form endpoint shows it. */
export interface MessageField {
  readonly type: string;
  /** Whether the type is the built-in one of its alias; a site's own type gets the generic words. */
  readonly builtIn?: boolean;
  /** The public settings with their effective values, every text in the page's culture. */
  readonly settings?: Readonly<Record<string, unknown>>;
  readonly options?: readonly MessageOption[];
}

/** An error a visitor sees a message for: what the pipeline reports, and what the message reads. */
export interface VisitorError {
  /** A field's kind of error, `check`, `rule` or `empty`. */
  readonly code: string;
  readonly field?: MessageField;
  /** For `check`, the check's kind. */
  readonly check?: string;
  /** For a `matchesField` check, the label of the field it names. */
  readonly other?: string;
  /** Whether the error is at one of a grid's rows rather than at the grid. */
  readonly gridRow?: boolean;
  /** The field's answers as posted, which only `dateWindow`, an email's `invalid` and `full` read. */
  readonly answers?: readonly string[];
  /** For `rowCount`, the rows the visitor gave. */
  readonly rows?: number;
}

const KILOBYTE = 1024n;
const MEGABYTE = KILOBYTE * KILOBYTE;

/** The words for a count: English and German say one only for 1; 14.2 adds the rules of five more languages. */
export const plural = (words: PluralWords, count: number): string => (count === 1 ? words.one : words.other);

/** The words of a culture among the tables by language: its own, its more general culture's, or English's. */
export function messageWordsOf(culture: string, tables: Readonly<Record<string, MessageWords>>): MessageWords {
  let candidate = culture.toLowerCase();
  while (candidate.length > 0) {
    const own = Object.hasOwn(tables, candidate) ? tables[candidate] : undefined;
    if (own !== undefined) return own;
    const dash = candidate.lastIndexOf('-');
    candidate = dash < 0 ? '' : candidate.slice(0, dash);
  }
  const english = tables['en'];
  if (english === undefined) throw new Error('The message tables have no English.');
  return english;
}

/** A template with each `{name}` it holds replaced by its value, in one pass, so a value is never read as a template. */
export function fill(template: string, values: Readonly<Record<string, string>>): string {
  let text = '';
  let at = 0;
  while (at < template.length) {
    const open = template.indexOf('{', at);
    const close = open < 0 ? -1 : template.indexOf('}', open + 1);
    if (close < 0) {
      text += template.slice(at);
      break;
    }
    const name = template.slice(open + 1, close);
    text += template.slice(at, open) + (Object.hasOwn(values, name) ? values[name] : template.slice(open, close + 1));
    at = close + 1;
  }
  return text;
}

/** The error summary's heading for a number of errors. */
export const summaryHeading = (errors: number, words: MessageWords): string => plural(words.summaryHeading, errors);

// The marks that already close a label, so its summary link takes no colon after it (ADR-0106), the same as Core's: a
// question mark, an exclamation mark, a full stop and a colon, the ellipsis, the Greek question mark, the Arabic
// question mark, the ideographic full stop, and the full-width forms of the four.
const CLOSING_MARKS = new Set([0x3f, 0x21, 0x2e, 0x3a, 0x2026, 0x37e, 0x61f, 0x3002, 0xff01, 0xff0e, 0xff1a, 0xff1f]);

/** Whether the label's last code unit that cleaning keeps (ADR-0005) closes it as a question mark, an exclamation mark, a full stop or a colon does. */
export function endsWithMark(label: string): boolean {
  for (let at = label.length - 1; at >= 0; at--) {
    const unit = label.charCodeAt(at);
    if (!isTrimmed(unit)) return CLOSING_MARKS.has(unit);
  }
  return false;
}

/**
 * A summary link to a default message: the field's label first, and in a row the row's legend; the message alone
 * without a label. A label that already ends in a question mark, an exclamation mark, a full stop or a colon takes no
 * colon after it ("Where would you like to sit? Choose an answer").
 */
export function summaryEntry(
  message: string,
  label: string | null | undefined,
  row: string | null | undefined,
  words: MessageWords,
): string {
  if (label === null || label === undefined || label.trim().length === 0) return message;
  return row === null || row === undefined || row.trim().length === 0
    ? fill(endsWithMark(label) ? words.summaryMarkedEntry : words.summaryEntry, { label, message })
    : fill(words.summaryRowEntry, { label, row, message });
}

/** How a message prints its values. */
class Saying {
  readonly words: MessageWords;
  readonly formats: CultureFormats;

  constructor(words: MessageWords, formats: CultureFormats) {
    this.words = words;
    this.formats = formats;
  }

  count(count: number): string {
    return this.number(String(count));
  }

  number(text: string): string {
    return formatNumber(text, null, this.formats) ?? text;
  }

  date(day: number): string {
    return formatCalendar(formatDate(day), 'medium', this.formats) ?? formatDate(day);
  }

  time(text: string): string {
    return formatCalendar(text, 'time', this.formats) ?? text;
  }

  choices(choices: readonly string[]): string {
    const last = choices.at(-1) ?? '';
    return choices.length <= 1 ? last : `${choices.slice(0, -1).join(', ')} ${this.words.or} ${last}`;
  }
}

/** The settings by name, as the form endpoint shows them. */
class Settings {
  private readonly settings: Readonly<Record<string, unknown>>;

  constructor(settings: Readonly<Record<string, unknown>>) {
    this.settings = settings;
  }

  private find(name: string): unknown {
    return Object.hasOwn(this.settings, name) ? this.settings[name] : undefined;
  }

  text(name: string): string | null {
    const value = this.find(name);
    return typeof value === 'string' ? value : null;
  }

  whole(name: string): number | null {
    const value = this.find(name);
    return typeof value === 'number' && Number.isSafeInteger(value) ? value : null;
  }

  bool(name: string): boolean {
    return this.find(name) === true;
  }

  json(name: string): unknown {
    const value = this.find(name);
    return value === null ? undefined : value;
  }

  texts(name: string): string[] {
    const value = this.find(name);
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
  }
}

/**
 * The default message for an error, in the given words, with the culture's formats; `today` is the site's today as a
 * day number, and `display` the culture's words for yes and no.
 */
export function defaultMessage(
  error: VisitorError,
  words: MessageWords,
  formats: CultureFormats,
  today: number,
  display: DisplayWords,
): string {
  switch (error.code) {
    case 'empty':
      return words.empty;
    case 'rule':
      return words.checkThis;
    case 'check':
      if (error.check === 'pattern') return words.pattern;
      if (error.check === 'matchesField' && error.other !== undefined && error.other.length > 0)
        return fill(words.sameAs, { field: error.other });
      if (error.check === 'uniquePerForm') return words.taken;
      return words.checkThis;
  }
  const field = error.field;
  if (field === undefined) return words.checkThis;
  if (field.builtIn === false) return error.code === 'required' ? words.answerThis : words.checkThis;
  const settings = new Settings(field.settings ?? {});
  const said = new Saying(words, formats);
  const answers = error.answers ?? [];
  const whole = (name: string, template: PluralWords, key: string): string | null => {
    const count = settings.whole(name);
    return count === null ? null : fill(plural(template, count), { [key]: said.count(count) });
  };
  switch (error.code) {
    case 'required':
      return required(error, field, settings, words, display);
    case 'invalid':
      return invalid(field, settings, said, answers);
    case 'minLength':
      return whole('minLength', words.minLength, 'min') ?? words.checkThis;
    case 'maxLength':
      return whole('maxLength', words.maxLength, 'max') ?? words.checkThis;
    case 'minWords':
      return whole('minWords', words.minWords, 'min') ?? words.checkThis;
    case 'maxWords':
      return whole('maxWords', words.maxWords, 'max') ?? words.checkThis;
    case 'min':
    case 'max':
      return range(field, settings, said);
    case 'step':
      return step(field, settings, said);
    case 'dateWindow':
      return field.type === 'date' || field.type === 'datetime'
        ? dateWindow(settings, said, today, answers)
        : words.checkThis;
    case 'selections':
      return selections(settings, said);
    case 'fileType':
      return fileFormats(settings, said);
    case 'fileSize':
      return size(settings, said);
    case 'fileCount':
      return whole('maxFiles', words.filesAtMost, 'max') ?? words.checkThis;
    case 'full':
      return full(field, words, answers);
    case 'confirmation':
      return words.confirmation;
    case 'digits':
      return words.digits;
    case 'rowCount':
      return rowCount(settings, said, error.rows);
    default:
      return words.checkThis;
  }
}

function required(
  error: VisitorError,
  field: MessageField,
  settings: Settings,
  words: MessageWords,
  display: DisplayWords,
): string {
  switch (field.type) {
    case 'text':
    case 'textarea':
      return words.answer;
    case 'email':
      return words.email;
    case 'phone':
      return words.phone;
    case 'url':
      return words.url;
    case 'number':
      return words.number;
    case 'date':
      return words.date;
    case 'time':
      return words.time;
    case 'datetime':
      return words.dateTime;
    case 'rating':
      return words.rating;
    case 'scale':
      return words.scale;
    case 'slider':
      return words.value;
    case 'dropdown':
      return settings.bool('multiple') ? words.chooseSeveral : words.choose;
    case 'radio':
      return words.choose;
    case 'checkboxes':
      return words.chooseSeveral;
    case 'grid':
      return error.gridRow === true ? words.gridRow : words.gridRows;
    case 'ranking':
      return words.order;
    case 'checkbox':
      return words.tick;
    case 'yesno': {
      const yes = settings.text('yesLabel');
      const no = settings.text('noLabel');
      return fill(words.yesNo, {
        yes: yes !== null && yes.length > 0 ? yes : display.yes,
        no: no !== null && no.length > 0 ? no : display.no,
      });
    }
    case 'consent':
      return words.agree;
    case 'country':
      return words.country;
    case 'name':
      return words.name;
    case 'address':
      return words.address;
    case 'file':
      return words.file;
    case 'signature':
      return words.sign;
    default:
      return words.answerThis;
  }
}

// An email address that is well formed but refused can only be at a domain the field does not take, which stays on
// the server (ADR-0062).
function invalid(field: MessageField, settings: Settings, said: Saying, answers: readonly string[]): string {
  const words = said.words;
  switch (field.type) {
    case 'email': {
      const address = answers[0];
      return address !== undefined && isEmailAddress(clean(address)) ? words.emailDomain : words.emailFormat;
    }
    case 'phone':
      return words.phoneFormat;
    case 'url':
      return words.urlFormat;
    case 'number':
      return fill(words.numberFormat, { example: said.number((settings.whole('decimals') ?? 0) > 0 ? '12.5' : '12') });
    case 'date':
      return words.dateFormat;
    case 'time':
      return words.timeFormat;
    case 'datetime':
      return words.dateTimeFormat;
    case 'rating':
      return fill(words.ratingRange, { count: said.count(settings.whole('count') ?? 5) });
    case 'scale':
      return fill(words.scaleRange, {
        from: said.count(settings.whole('from') ?? 0),
        to: said.count(settings.whole('to') ?? 10),
      });
    case 'slider':
      return sliderRange(settings, said);
    case 'dropdown':
    case 'radio':
    case 'checkboxes':
    case 'grid':
    case 'ranking':
      return words.options;
    case 'country':
      return words.countryList;
    case 'name':
      return words.nameCheck;
    case 'address':
      return words.addressCheck;
    case 'signature':
      return words.signAgain;
    default:
      return words.checkThis;
  }
}

function sliderRange(settings: Settings, said: Saying): string {
  const low = settings.text('min');
  const high = settings.text('max');
  return low !== null && high !== null
    ? fill(said.words.valueRange, { min: said.number(low), max: said.number(high) })
    : said.words.checkThis;
}

// min and max: the field's whole range, whichever limit the answer passed.
function range(field: MessageField, settings: Settings, said: Saying): string {
  const words = said.words;
  const low = settings.text('min');
  const high = settings.text('max');
  if (field.type === 'slider') return sliderRange(settings, said);
  if (field.type !== 'number' && field.type !== 'time') return words.checkThis;
  const print = (text: string): string => (field.type === 'number' ? said.number(text) : said.time(text));
  const [between, from, upTo] =
    field.type === 'number'
      ? [words.numberBetween, words.numberFrom, words.numberUpTo]
      : [words.timeBetween, words.timeFrom, words.timeUpTo];
  if (low !== null && high !== null) return fill(between, { min: print(low), max: print(high) });
  if (low !== null) return fill(from, { min: print(low) });
  if (high !== null) return fill(upTo, { max: print(high) });
  return words.checkThis;
}

// step: a number's own step (from its least when it has one), else the places its decimals allow; a slider's step;
// a time's minutes, from its earliest, and a date and time's, from midnight.
function step(field: MessageField, settings: Settings, said: Saying): string {
  const words = said.words;
  switch (field.type) {
    case 'number': {
      const own = settings.text('step');
      if (own !== null) {
        const min = settings.text('min');
        return min !== null
          ? fill(words.numberStepFrom, { step: said.number(own), min: said.number(min) })
          : fill(words.numberStep, { step: said.number(own) });
      }
      const places = settings.whole('decimals');
      return places !== null && places > 0
        ? fill(plural(words.places, places), { places: said.count(places) })
        : words.wholeNumber;
    }
    case 'slider': {
      const own = settings.text('step');
      return own !== null ? fill(words.valueStep, { step: said.number(own) }) : words.checkThis;
    }
    case 'time':
    case 'datetime': {
      const minutes = settings.whole('stepMinutes');
      if (minutes === null) return words.checkThis;
      const earliest = settings.text('min');
      return field.type === 'time' && earliest !== null
        ? fill(plural(words.timeStepFrom, minutes), { minutes: said.count(minutes), min: said.time(earliest) })
        : fill(plural(words.timeStep, minutes), { minutes: said.count(minutes) });
    }
    default:
      return words.checkThis;
  }
}

// dateWindow: the window a date must keep, its dates relative to today counted from the site's today; an answer the
// window would take is one the field excludes.
function dateWindow(settings: Settings, said: Saying, today: number, answers: readonly string[]): string {
  const words = said.words;
  const low = bound(settings.json('min'), () => today);
  const high = bound(settings.json('max'), () => today);
  if (low === null || high === null) return words.dateUnavailable;
  const weekdays = settings.bool('weekdaysOnly');
  const answer = answers[0];
  const day = answer === undefined ? null : parseDate(answer.length >= 10 ? answer.slice(0, 10) : answer);
  // Day 0 is 1970-01-01, a Thursday: 0 is Sunday and 6 Saturday.
  const weekday = day === null ? -1 : (((day + 4) % 7) + 7) % 7;
  if (
    day !== null &&
    (low === undefined || day >= low) &&
    (high === undefined || day <= high) &&
    !(weekdays && (weekday === 0 || weekday === 6))
  )
    return words.dateUnavailable;
  const monday = said.formats.days[1] ?? '';
  const friday = said.formats.days[5] ?? '';
  if (!weekdays) {
    if (low !== undefined && high !== undefined)
      return fill(words.dateBetween, { min: said.date(low), max: said.date(high) });
    if (low !== undefined) return fill(words.dateFrom, { min: said.date(low) });
    if (high !== undefined) return fill(words.dateUpTo, { max: said.date(high) });
    return words.dateUnavailable;
  }
  if (low !== undefined && high !== undefined)
    return fill(words.weekdayBetween, { min: said.date(low), max: said.date(high), monday, friday });
  if (low !== undefined) return fill(words.weekdayFrom, { min: said.date(low), monday, friday });
  if (high !== undefined) return fill(words.weekdayUpTo, { max: said.date(high), monday, friday });
  return fill(words.weekday, { monday, friday });
}

function selections(settings: Settings, said: Saying): string {
  const words = said.words;
  const min = settings.whole('minSelections');
  const max = settings.whole('maxSelections');
  if (min !== null && max !== null)
    return min === max
      ? fill(plural(words.selectionsExactly, min), { count: said.count(min) })
      : fill(words.selectionsBetween, { min: said.count(min), max: said.count(max) });
  if (min !== null) return fill(plural(words.selectionsAtLeast, min), { min: said.count(min) });
  if (max !== null) return fill(plural(words.selectionsAtMost, max), { max: said.count(max) });
  return words.checkThis;
}

// fileType: the formats the field takes, its groups' extensions first and then its own, in capitals, each once.
function fileFormats(settings: Settings, said: Saying): string {
  const formats: string[] = [];
  for (const group of settings.texts('types')) formats.push(...(FILE_TYPES.get(group.toLowerCase()) ?? []));
  for (const extension of settings.texts('extensions')) formats.push(extension.replace(/^\.+/, ''));
  const shown = [...new Set(formats.map((format) => format.toUpperCase()).filter((format) => format.length > 0))];
  return shown.length === 0 ? said.words.checkThis : fill(said.words.fileFormat, { formats: said.choices(shown) });
}

// fileSize: the field's limit in KB below a megabyte and in MB from one, 1,024 based, with one place at most; a field
// without a limit of its own is held to the host's, which the page does not know.
function size(settings: Settings, said: Saying): string {
  const words = said.words;
  const limit = settings.whole('maxBytes');
  if (limit === null || limit <= 0) return words.fileTooLarge;
  const bytes = BigInt(limit);
  const [unit, template] = bytes < MEGABYTE ? [KILOBYTE, words.kilobytes] : [MEGABYTE, words.megabytes];
  const tenths = (bytes * 10n + unit / 2n) / unit;
  const text = tenths % 10n === 0n ? String(tenths / 10n) : `${tenths / 10n}.${tenths % 10n}`;
  const shown = fill(template, { size: said.number(text) });
  return fill((settings.whole('maxFiles') ?? 1) > 1 ? words.filesLargerThan : words.fileLargerThan, { size: shown });
}

// full: the option an answer of one option holds, by its label; several, or none known, in general.
function full(field: MessageField, words: MessageWords, answers: readonly string[]): string {
  const [answer] = answers;
  if (answers.length !== 1 || answer === undefined) return words.choiceFull;
  const option = (field.options ?? []).find((each) => each.value === answer);
  return fill(words.optionFull, { option: option !== undefined && option.label.length > 0 ? option.label : answer });
}

// rowCount: too few rows asks for the least, too many for the most; without the count, the least when there is one.
function rowCount(settings: Settings, said: Saying, rows: number | undefined): string {
  const words = said.words;
  const min = settings.whole('minRows') ?? 0;
  const max = settings.whole('maxRows');
  const tooMany = rows !== undefined ? max !== null && rows > max : min === 0;
  return tooMany && max !== null
    ? fill(plural(words.rowsAtMost, max), { max: said.count(max) })
    : fill(plural(words.rowsAtLeast, min), { min: said.count(min) });
}
