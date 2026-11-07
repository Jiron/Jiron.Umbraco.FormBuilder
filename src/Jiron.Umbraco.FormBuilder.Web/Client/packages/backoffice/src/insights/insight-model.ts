// What the Insights view and Forms at a glance work out before they draw (ROADMAP 11.5, IA 5.5, ADR-0211): the span of
// each date range and the span it compares with, a change said in words and numbers rather than in colour, the
// conversion, the time to complete by its bucket, and the bars' share of the most. Pure.

/** The date ranges of the toolbar, and a range of chosen dates. */
export const PRESETS = ['last7', 'last30', 'thisMonth', 'lastMonth', 'thisYear'] as const;
export type Preset = (typeof PRESETS)[number] | 'custom';

/** How a report groups its days. */
export const GROUPINGS = ['day', 'week', 'month'] as const;
export type Grouping = (typeof GROUPINGS)[number];

/** Days from one to another, both included, as `yyyy-MM-dd`. */
export interface Span {
  readonly from: string;
  readonly to: string;
}

/** The most days a span covers, as the server reads them. */
export const MOST_DAYS = 366;

const pad = (number: number): string => String(number).padStart(2, '0');

/** A day as `yyyy-MM-dd`, by the calendar of the date's own fields. */
export const dayOf = (date: Date): string => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;

/**
 * Today as the stats count days (ADR-0211): the UTC day, as a date at noon of that calendar day, so a user west of UTC
 * sees this evening's entries in today and one east of it never asks for a day that has not begun.
 */
export const utcToday = (now: Date): Date => new Date(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), 12);

/** The date of a `yyyy-MM-dd` day, at noon, so no change of the clock moves it to another day. */
export const dateOf = (day: string): Date => {
  const [year = 0, month = 1, date = 1] = day.split('-').map(Number);
  return new Date(year, month - 1, date, 12);
};

const addDays = (date: Date, days: number): Date =>
  new Date(date.getFullYear(), date.getMonth(), date.getDate() + days, 12);

/** How many days a span covers. */
export const daysIn = (span: Span): number =>
  Math.round((dateOf(span.to).getTime() - dateOf(span.from).getTime()) / 86_400_000) + 1;

/** Whether a span is one the server reads: its days real, the last on or after the first, at most a year apart. */
export function isSpan(span: Span): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(span.from) || !/^\d{4}-\d{2}-\d{2}$/.test(span.to)) return false;
  if (dayOf(dateOf(span.from)) !== span.from || dayOf(dateOf(span.to)) !== span.to) return false;
  const days = daysIn(span);
  return days >= 1 && days <= MOST_DAYS;
}

/** The day of the month, kept within a month that may be shorter. */
const dayIn = (year: number, month: number, day: number): Date =>
  new Date(year, month, Math.min(day, new Date(year, month + 1, 0).getDate()), 12);

/** The span of a date range, to today. */
export function spanOf(preset: Exclude<Preset, 'custom'>, today: Date): Span {
  const end = dayOf(today);
  switch (preset) {
    case 'last7':
      return { from: dayOf(addDays(today, -6)), to: end };
    case 'last30':
      return { from: dayOf(addDays(today, -29)), to: end };
    case 'thisMonth':
      return { from: dayOf(new Date(today.getFullYear(), today.getMonth(), 1, 12)), to: end };
    case 'lastMonth':
      return {
        from: dayOf(new Date(today.getFullYear(), today.getMonth() - 1, 1, 12)),
        to: dayOf(new Date(today.getFullYear(), today.getMonth(), 0, 12)),
      };
    case 'thisYear':
      return { from: dayOf(new Date(today.getFullYear(), 0, 1, 12)), to: end };
  }
}

/**
 * The span a range compares with (IA 5.5): a calendar range with the same days of the one before (this month so far
 * with the same days of last month, last month with the month before, this year so far with the same days of last
 * year), a rolling one or chosen dates with as many days just before.
 */
export function compareOf(preset: Preset, span: Span): Span {
  const from = dateOf(span.from);
  const to = dateOf(span.to);
  switch (preset) {
    case 'thisMonth':
      return {
        from: dayOf(new Date(from.getFullYear(), from.getMonth() - 1, 1, 12)),
        to: dayOf(dayIn(to.getFullYear(), to.getMonth() - 1, to.getDate())),
      };
    case 'lastMonth':
      return {
        from: dayOf(new Date(from.getFullYear(), from.getMonth() - 1, 1, 12)),
        to: dayOf(new Date(from.getFullYear(), from.getMonth(), 0, 12)),
      };
    case 'thisYear':
      return {
        from: dayOf(new Date(from.getFullYear() - 1, 0, 1, 12)),
        to: dayOf(dayIn(to.getFullYear() - 1, to.getMonth(), to.getDate())),
      };
    default: {
      const days = daysIn(span);
      return { from: dayOf(addDays(from, -days)), to: dayOf(addDays(from, -1)) };
    }
  }
}

/** A change from the number before: up or down by a share of it, the same, or up from none. */
export type Change =
  | { readonly kind: 'up' | 'down'; readonly percent: number }
  | { readonly kind: 'same' }
  | { readonly kind: 'fromNone' };

/** How a number changed from the one before, by a whole percent of it. */
export function changeOf(now: number, before: number): Change {
  if (now === before) return { kind: 'same' };
  if (before === 0) return { kind: 'fromNone' };
  const percent = Math.round((Math.abs(now - before) / before) * 100);
  return { kind: now > before ? 'up' : 'down', percent };
}

/** How a share changed, in points of a percent with one decimal: 0.081 against 0.077 is 0.4. */
export const pointsOf = (now: number, before: number): number => Math.round(Math.abs(now - before) * 1000) / 10;

/** A time to complete by its bucket: its upper limit in seconds, or `more` for longer than an hour. */
export type CompletionTime =
  | { readonly unit: 'seconds' | 'minutes'; readonly count: number }
  | { readonly unit: 'hour' }
  | { readonly unit: 'longer' };

/** The time a bucket of the time to complete stands for. */
export function completionOf(bucket: string): CompletionTime | undefined {
  if (bucket === 'more') return { unit: 'longer' };
  const seconds = Number(bucket);
  if (!Number.isInteger(seconds) || seconds <= 0) return undefined;
  if (seconds < 60) return { unit: 'seconds', count: seconds };
  if (seconds < 3600) return { unit: 'minutes', count: Math.round(seconds / 60) };
  return { unit: 'hour' };
}

/** The kinds of error the view names in words; any other is "another reason". */
export const KINDS = [
  'required',
  'invalid',
  'minLength',
  'maxLength',
  'minWords',
  'maxWords',
  'min',
  'max',
  'step',
  'dateWindow',
  'selections',
  'fileType',
  'fileSize',
  'fileCount',
  'full',
  'confirmation',
  'digits',
  'rowCount',
  'check',
  'rule',
] as const;

/** The word key of a kind of error. */
export const kindKey = (kind: string): string =>
  (KINDS as readonly string[]).includes(kind) ? `kind_${kind}` : 'kind_other';

/** Each value's share of the most, from 0 to 1: the length of its bar. */
export function sharesOf(values: readonly number[]): number[] {
  const most = Math.max(0, ...values);
  return values.map((value) => (most > 0 ? Math.max(0, value) / most : 0));
}

/** The index of the largest value, the first of equals; -1 for none above 0. */
export function mostAt(values: readonly number[]): number {
  let at = -1;
  values.forEach((value, index) => {
    if (value > 0 && (at < 0 || value > (values[at] ?? 0))) at = index;
  });
  return at;
}

/** The three labels below a row of bars: the first, the middle and the last. */
export function axisOf(labels: readonly string[]): string[] {
  if (labels.length <= 3) return [...labels];
  return [labels[0] ?? '', labels[Math.floor((labels.length - 1) / 2)] ?? '', labels[labels.length - 1] ?? ''];
}
