/**
 * The rule search (ROADMAP 9.5c, 9.5d; ADR-0178, ADR-0179): what rules do for the answers a visitor can give, worked out
 * by the engine itself (`evaluateRule`) over sets of answers, as Core's `RuleSearch` does in C#; both pass the shared
 * vectors of tests/vectors/search.json (D10). It answers two questions:
 *
 * - **A conflict** (`conflictOf`): a require rule that holds while a hiding rule hides its field (a hide rule that
 *   holds, a show rule that doesn't: the field's own, its group's or its page's). A hidden field is never required, so
 *   visitors can send without it. A conflict is said only where the engine finds both for one set of answers, the
 *   answers of the fields the form hides for them counting as none (`formSight`), so a conflict said is always real;
 *   one hidden in answers no rule names, such as a pattern's, may go unsaid.
 * - **Whether an element ever shows** (`showsAtAll`): to a visitor who has given no answer yet to some fields (those on
 *   later pages, going forward). "Never" is said only where the answers tried stand for every answer a visitor gives
 *   (`complete`), so a page said to be unreachable is.
 *
 * The answers tried, for each field and part of the visit the rules look at: no answer, the values the rules name, a
 * choice's own options (and nothing else) where the form has them, one more and one less than a number, the days
 * before and after a date (a day counted from today counted as the rule counts it), a minute either side of a time,
 * where two answers are compared each other's values, a language of the site's (never none), today and the days after
 * it (never one before), member groups only signed in, and, for a text the rules only test for being equal, one they
 * don't name, which stands for every other.
 */
import type { Formulas } from './calc.ts';
import { applyRelative, formatDate, parseDate, parseRelative } from './dates.ts';
import { LogicError } from './errors.ts';
import { evaluateForm, hasCalculations, type FormState, type LogicField, type LogicForm } from './form.ts';
import { evaluateRule, type Context, type Kind, type Rule } from './rules.ts';
import { fold } from './text.ts';

/** A rule as a definition keeps it: `enabled` and `match` may be left out, as they come (in use, all). */
export interface SearchRule {
  readonly effect: string;
  readonly enabled?: boolean | null;
  readonly match?: string | null;
  readonly conditions: readonly unknown[];
}

/** What the search tries answers with: the fields' kinds, each choice's own options, the site's languages, today. */
export interface SearchScope {
  readonly fields: Readonly<Record<string, Kind>>;
  /** The options a choice offers that the form knows, by the key of the field or grid row; none where it doesn't. */
  readonly options: (key: string) => readonly string[];
  readonly cultures: readonly string[];
  /** Today, `YYYY-MM-DD`, from which days counted from today are counted; no visit is on an earlier day. */
  readonly today: string;
  /**
   * The fields the form hides for a set of answers, which count as unanswered: as the form engine decides
   * (`formSight`), or null where it can't tell. Without it, every field the rules look at counts as shown.
   */
  readonly hidden?: (
    values: Readonly<Record<string, readonly string[]>>,
    context: Context,
  ) => ReadonlySet<string> | null;
  /** Whether a field's answers, read where the rules read them, may be several, as a repeatable group's child's are. */
  readonly several?: (key: string) => boolean;
}

/** One answer of a conflict: what it is about (`field:<key>` or `context:<name>`) and the answers given. */
export interface SearchAnswer {
  readonly subject: string;
  /** The answers given; empty for none, or for an answer the rules don't name (`noneOf`). */
  readonly values: readonly string[];
  /** Set for a text the rules don't name: the texts they do, which any other answer stands for alike. */
  readonly noneOf?: readonly string[];
}

/** A require rule and a rule that hides its field holding together, and for which answers. */
export interface Conflict<R extends SearchRule = SearchRule> {
  /** The rule that hides the field for those answers: the first of those given that does. */
  readonly hiding: R;
  readonly requiring: R;
  /** The answers that matter, in the order the rules first name them; empty where the rules conflict for any answer. */
  readonly answers: readonly SearchAnswer[];
}

/** Whether an element shows for some answers (`shows`), for none of all a visitor can give (`never`), or can't tell. */
export type Showing = 'shows' | 'never' | 'unknown';

/** How many sets of answers are tried at most, so an element with many rules still answers at once. */
export const MOST_TRIED = 4096;
/** How many answers a choice of several is tried with at most. */
const MOST_SETS = 32;

// A rule that is off the engine leaves undecided (null), as one it refuses, so neither ever conflicts or hides.
const used = (rule: SearchRule): boolean => rule.conditions.length > 0;

/**
 * The conflict of a require rule with rules that hide its field, or null where none of the answers tried make the
 * field required while one of them hides it. `hidings` are show and hide rules: the field's own, its group's, its
 * page's, in that order.
 */
export function conflictOf<R extends SearchRule>(
  requiring: R,
  hidings: readonly R[],
  scope: SearchScope,
): Conflict<R> | null {
  const hiders = hidings.filter((rule) => used(rule) && (rule.effect === 'hide' || rule.effect === 'show'));
  if (requiring.effect !== 'require' || !used(requiring) || hiders.length === 0) return null;
  // The hiding rules' answers are said first, as the rules overview says a conflict ("when Topic is Press").
  const all = [...hiders, requiring];
  const subjects = subjectsOf(all);
  const tries = subjects.map((subject) => triesOf(subject, all, scope));
  const candidates = tries.map(({ candidates }) => candidates);
  if (candidates.some((list) => list.length === 0)) return null;
  const hiderAt = (values: readonly (readonly string[])[]): R | null => {
    const answers = seen(answersOf(subjects, values, scope), scope);
    if (answers === null || matches(requiring, answers, scope) !== true) return null;
    return hiders.find((rule) => hides(rule, matches(rule, answers, scope))) ?? null;
  };
  const conflicts = (values: readonly (readonly string[])[]): boolean => hiderAt(values) !== null;
  const found = search(candidates, conflicts);
  if (!found) return null;
  const hiding = hiderAt(found.values);
  if (hiding === null) return null;
  return { hiding, requiring, answers: said(subjects, tries, found.values, conflicts) };
}

/**
 * Whether an element with these show and hide rules shows to a visitor who has not yet answered the fields of
 * `unanswered` (their keys), for some of the answers a visitor gives. An element without rules in use shows.
 */
export function showsAtAll(rules: readonly SearchRule[], scope: SearchScope, unanswered: readonly string[]): Showing {
  const own = rules.filter((rule) => used(rule) && (rule.effect === 'hide' || rule.effect === 'show'));
  if (own.length === 0) return 'shows';
  const subjects = subjectsOf(own);
  const blank = new Set(unanswered.map((key) => `field:${key}`));
  const tries = subjects.map((subject): Tries =>
    blank.has(subject) ? { candidates: [[]], fresh: null, literals: [], complete: true } : triesOf(subject, own, scope),
  );
  const candidates = tries.map(({ candidates }) => candidates);
  if (candidates.some((list) => list.length === 0)) return 'unknown';
  let undecided = false;
  const shows = (values: readonly (readonly string[])[]): boolean => {
    const answers = answersOf(subjects, values, scope);
    if (answers === null) return false;
    return own.every((rule) => {
      const holds = matches(rule, answers, scope);
      // A rule the engine refuses says nothing, and the element's showing can't be told.
      if (holds === null && rule.enabled !== false) undecided = true;
      return !hides(rule, holds);
    });
  };
  const found = search(candidates, shows);
  if (undecided) return 'unknown';
  if (found) return 'shows';
  return tries.every((one) => one.complete) &&
    candidates.every((list) => list.length > 0) &&
    trialsOf(candidates) <= MOST_TRIED
    ? 'never'
    : 'unknown';
}

/**
 * The fields a form hides for a set of answers, as the form engine decides (`evaluateForm`), for a scope's `hidden`: a
 * field or grid row is hidden where it, its grid or anything holding it is. A repeatable group's child fields are
 * answered in one row, as a visitor with one row answers them. Null where the engine refuses the form, or where it
 * works out calculations and no calculation language is given.
 */
export function formSight(
  form: LogicForm,
  formulas: Formulas | null,
): (values: Readonly<Record<string, readonly string[]>>, context: Context) => ReadonlySet<string> | null {
  // Only what holds what and the show and hide rules decide what is hidden.
  const pages: LogicForm = { pages: form.pages };
  const repeaterOf = new Map<string, string>();
  const gridOf = new Map<string, string>();
  const walk = (fields: readonly LogicField[], repeater: string | null): void => {
    for (const field of fields) {
      if (repeater !== null) repeaterOf.set(field.id, repeater);
      for (const row of field.rows ?? []) gridOf.set(row.id, field.id);
      const children = field.fields ?? [];
      walk(children, children.length > 0 ? field.id : repeater);
    }
  };
  for (const page of form.pages) for (const group of page.groups ?? []) walk(group.fields ?? [], null);
  const blind = formulas === null && hasCalculations(pages);
  return (values, context) => {
    if (blind) return null;
    const outside: Record<string, readonly string[]> = {};
    const row: Record<string, Record<string, readonly string[]>> = {};
    for (const [key, given] of Object.entries(values)) {
      const repeater = repeaterOf.get(gridOf.get(key) ?? key);
      if (repeater === undefined) outside[key] = given;
      else (row[repeater] ??= {})[key] = given;
    }
    const rows = Object.fromEntries(Object.entries(row).map(([repeater, one]) => [repeater, [one]]));
    let state: FormState;
    try {
      state = evaluateForm(pages, { values: outside, rows }, context, formulas);
    } catch (error) {
      if (error instanceof LogicError) return null;
      throw error;
    }
    const hidden = new Set(state.hidden);
    for (const one of Object.values(state.rows)) for (const id of one[0]?.hidden ?? []) hidden.add(id);
    for (const [id, grid] of gridOf) if (hidden.has(grid)) hidden.add(id);
    return hidden;
  };
}

/**
 * The answers as the form sees them: those of the fields it hides for them count as unanswered (a hidden field is no
 * answer). Null where the form can't tell, which then makes no conflict.
 */
function seen(answers: Answers | null, scope: SearchScope): Answers | null {
  if (answers === null || !scope.hidden) return answers;
  const hidden = scope.hidden(answers.values, answers.context);
  if (hidden === null) return null;
  const values = Object.fromEntries(
    Object.entries(answers.values).map(([key, given]): [string, readonly string[]] => [
      key,
      hidden.has(key) ? [] : given,
    ]),
  );
  return { ...answers, values };
}

/** Whether a rule hides its element, given whether it holds: a hide rule that holds, a show rule that doesn't. */
const hides = (rule: SearchRule, holds: boolean | null): boolean =>
  holds !== null && (rule.effect === 'hide' ? holds : !holds);

/**
 * The answers that matter: those that conflict whatever they are, together, are left out, each one left out staying
 * free while the next is tried, so the answers said hold the conflict whatever the others are.
 */
function said(
  subjects: readonly string[],
  tries: readonly Tries[],
  found: readonly (readonly string[])[],
  conflicts: (values: readonly (readonly string[])[]) => boolean,
): SearchAnswer[] {
  const candidates = tries.map(({ candidates }) => candidates);
  const free: number[] = [];
  subjects.forEach((_, at) => {
    if (freeTogether([...free, at], candidates, found, conflicts)) free.push(at);
  });
  return subjects.flatMap((subject, at): SearchAnswer[] => {
    if (free.includes(at)) return [];
    const values = found[at] ?? [];
    const { fresh, literals } = tries[at] ?? { fresh: null, literals: [] };
    return [fresh !== null && values[0] === fresh ? { subject, values: [], noneOf: literals } : { subject, values }];
  });
}

/** Whether the rules conflict for every answer of these subjects tried together, the others as found. */
function freeTogether(
  free: readonly number[],
  candidates: readonly (readonly string[][])[],
  found: readonly (readonly string[])[],
  conflicts: (values: readonly (readonly string[])[]) => boolean,
): boolean {
  const lists = found.map((one, at) => (free.includes(at) ? (candidates[at] ?? []) : [one]));
  // Too many to try counts as not free, so an answer is said rather than claimed not to matter.
  if (trialsOf(lists) > MOST_TRIED) return false;
  return search(lists, (values) => !conflicts(values)) === null;
}

// What the rules look at, and what to try.

type Plain = Readonly<Record<string, unknown>>;

/** The field a operand compares with (`{ field }`); null for any other operand. */
const otherFieldOf = (operand: unknown): string | null =>
  typeof operand === 'object' &&
  operand !== null &&
  !Array.isArray(operand) &&
  typeof (operand as Plain)['field'] === 'string'
    ? ((operand as Plain)['field'] as string)
    : null;

/** The offset of a date relative to today an operand holds (`+2d`, '' for today); null for any other operand. */
const relativeOf = (operand: unknown): string | null =>
  typeof operand === 'object' &&
  operand !== null &&
  !Array.isArray(operand) &&
  typeof (operand as Plain)['today'] === 'string' &&
  !('field' in operand)
    ? ((operand as Plain)['today'] as string)
    : null;

/** Each field and part of the visit the rules look at or compare with, in the order they first name them. */
function subjectsOf(rules: readonly SearchRule[]): string[] {
  const out: string[] = [];
  const add = (id: string) => {
    if (!out.includes(id)) out.push(id);
  };
  for (const condition of conditionsIn(rules)) {
    if (typeof condition['field'] === 'string') add(`field:${condition['field']}`);
    else if (typeof condition['context'] === 'string') add(`context:${condition['context']}`);
    for (const operand of operandsOf(condition)) {
      const other = otherFieldOf(operand);
      if (other !== null) add(`field:${other}`);
      if (relativeOf(operand) !== null) add('context:today');
    }
  }
  return out;
}

function conditionsIn(rules: readonly { readonly conditions?: readonly unknown[] | null }[]): Plain[] {
  const out: Plain[] = [];
  const walk = (items: readonly unknown[]) => {
    for (const item of items) {
      const given = (typeof item === 'object' && item !== null ? item : {}) as Plain;
      if (Array.isArray(given['conditions'])) walk(given['conditions'] as readonly unknown[]);
      else out.push(given);
    }
  };
  for (const rule of rules) walk(rule.conditions ?? []);
  return out;
}

const operandsOf = (condition: Plain): unknown[] => {
  const value = condition['value'];
  return Array.isArray(value)
    ? [...(value as readonly unknown[])]
    : value === undefined || value === null
      ? []
      : [value];
};

const kindOf = (subject: string, scope: SearchScope): Kind | undefined => {
  if (subject.startsWith('field:')) {
    const key = subject.slice('field:'.length);
    return Object.hasOwn(scope.fields, key) ? scope.fields[key] : undefined;
  }
  const name = subject.slice('context:'.length);
  if (name === 'culture' || name.startsWith('query.')) return 'text';
  return name === 'memberSignedIn' ? 'checkbox' : name === 'memberGroup' ? 'choices' : 'date';
};

const COUNTS: ReadonlySet<string> = new Set(['countAtLeast', 'countAtMost']);

const subjectOf = (condition: Plain): string =>
  typeof condition['field'] === 'string' ? `field:${condition['field']}` : `context:${String(condition['context'])}`;

/** The conditions on a subject. */
const conditionsOn = (subject: string, rules: readonly SearchRule[]): Plain[] =>
  conditionsIn(rules).filter((condition) => subjectOf(condition) === subject);

/**
 * The texts the rules compare a subject with: the values of its own conditions (a count only where `counts` asks for
 * it, as no option is a number of ticks) and, where two answers are compared, the other's values too, so the answers
 * tried can stand on either side of each other.
 */
function literalsOf(subject: string, rules: readonly SearchRule[], scope: SearchScope, counts: boolean): string[] {
  const out: string[] = [];
  const add = (text: string) => {
    if (text !== '' && !out.includes(text)) out.push(text);
  };
  const conditions = conditionsIn(rules);
  const own = (of: string) => {
    for (const condition of conditions) {
      if (subjectOf(condition) !== of || (!counts && COUNTS.has(String(condition['op'])))) continue;
      for (const operand of operandsOf(condition)) {
        if (typeof operand === 'string') add(operand);
        const offset = relativeOf(operand);
        if (offset !== null) {
          const steps = parseRelative(offset);
          const base = parseDate(scope.today);
          const day = steps && base !== null ? applyRelative(base, steps) : null;
          if (day !== null) add(formatDate(day));
        }
      }
    }
  };
  own(subject);
  for (const condition of conditions) {
    const others = operandsOf(condition).flatMap((operand) => {
      const other = otherFieldOf(operand);
      return other === null ? [] : [`field:${other}`];
    });
    if (subjectOf(condition) === subject) others.forEach(own);
    else if (others.includes(subject)) own(subjectOf(condition));
  }
  return out;
}

/** The operators for which every answer the rules don't name is alike: none of them looks inside an answer. */
const ALIKE: ReadonlySet<string> = new Set([
  'equals',
  'notEquals',
  'isOneOf',
  'isNoneOf',
  'isEmpty',
  'isNotEmpty',
  'countAtLeast',
  'countAtMost',
]);

/** The operators that only ask which texts are ticked or given, not how many or whether any is. */
const NAMED: ReadonlySet<string> = new Set(['equals', 'notEquals', 'isOneOf', 'isNoneOf', 'isChecked', 'isNotChecked']);

/** Whether a subject is compared with another answer, or another with it. */
const compared = (subject: string, rules: readonly SearchRule[]): boolean =>
  conditionsIn(rules).some(
    (condition) =>
      operandsOf(condition).some((operand) => otherFieldOf(operand) !== null) &&
      (subjectOf(condition) === subject ||
        operandsOf(condition).some((operand) => `field:${otherFieldOf(operand)}` === subject)),
  );

/**
 * Whether a subject's answers the rules don't name are all alike to them: each condition on it only tests it for being
 * equal to texts it names, and no condition compares it with another answer. One such answer then stands for them all.
 */
const othersAlike = (subject: string, rules: readonly SearchRule[]): boolean =>
  !compared(subject, rules) &&
  conditionsOn(subject, rules).every(
    (condition) =>
      ALIKE.has(String(condition['op'])) && operandsOf(condition).every((operand) => typeof operand === 'string'),
  );

/** A text none of the rules' texts is, compared as the engine compares texts. */
function freshText(literals: readonly string[]): string {
  const taken = new Set(literals.map(fold));
  let text = 'zz-other';
  while (taken.has(fold(text))) text += '-';
  return text;
}

/** A plain answer of an ordered kind, tried where the rules name none. */
function plainOf(kind: Kind, today: string): string | null {
  switch (kind) {
    case 'number':
      return '1';
    case 'date':
      return today;
    case 'time':
      return '12:00';
    case 'dateTime':
      return `${today}T12:00`;
    default:
      return null;
  }
}

const PLAIN_NUMBER = /^-?\d+(\.\d+)?$/;

/** Next to a number, a date, a time or a date and time: one less and one more, which comparisons tell apart. */
function neighbours(kind: Kind, text: string): string[] {
  if (kind === 'number') {
    // A plain decimal well inside what both runtimes write without an exponent, so they write its neighbours alike.
    const value = Number(text);
    if (!PLAIN_NUMBER.test(text) || Math.abs(value) >= 1e15) return [];
    // Below a ten-thousandth, C# writes a number with an exponent and JavaScript doesn't, so such a neighbour isn't tried.
    return [value - 1, value + 1].filter((one) => one === 0 || Math.abs(one) >= 1e-4).map(String);
  }
  if (kind === 'date' || kind === 'dateTime') {
    const day = parseDate(text.slice(0, 10));
    if (day === null) return [];
    const rest = text.slice(10);
    return [formatDate(day - 1) + rest, formatDate(day + 1) + rest];
  }
  if (kind === 'time') {
    const match = /^(\d{2}):(\d{2})/.exec(text);
    if (!match) return [];
    const minutes = Number(match[1]) * 60 + Number(match[2]);
    const at = (value: number) => {
      const wrapped = (value + 1440) % 1440;
      return `${String(Math.floor(wrapped / 60)).padStart(2, '0')}:${String(wrapped % 60).padStart(2, '0')}`;
    };
    return [at(minutes - 1), at(minutes + 1)];
  }
  return [];
}

/**
 * What a subject is tried with; where one text stands for the answers the rules don't name, that one; and whether the
 * answers tried stand for every answer a visitor can give (`complete`).
 */
interface Tries {
  readonly candidates: string[][];
  readonly fresh: string | null;
  readonly literals: readonly string[];
  readonly complete: boolean;
}

/**
 * The answers a subject is tried with, each one a visitor can give: none first (a language is always one of the
 * site's), then what the rules name and what lies next to it. A choice keeps to its options where the form knows them;
 * member groups are tried with signing in (`answersOf`). They stand for every answer where no answer outside them could
 * make a condition on the subject come out otherwise: ticks, the signing in, the site's languages, numbers of files,
 * whole days not compared with another answer (between two neighbours lies no other), a choice whose options the form
 * knows, a text or a number only tested for being equal (one tried being none the rules name), and several choices
 * where at most two named options matter and counts tell at most two ticked from fewer, one from more. A field whose
 * answers are several where the rules read them (`several`), or a query value, which a link may give twice, never does.
 */
function triesOf(subject: string, rules: readonly SearchRule[], scope: SearchScope): Tries {
  const kind = kindOf(subject, scope);
  const literals = literalsOf(subject, rules, scope, kind === 'files');
  const ops = conditionsOn(subject, rules).map((condition) => String(condition['op']));
  const tried = (candidates: string[][], complete: boolean): Tries => ({ candidates, fresh: null, literals, complete });
  const relative = conditionsIn(rules).some((condition) =>
    operandsOf(condition).some((one) => relativeOf(one) !== null),
  );
  if (subject === 'context:memberSignedIn' || kind === 'checkbox') return tried([[], ['true']], true);
  if (subject === 'context:today') {
    const days = literals.flatMap((text) => [text, ...neighbours('date', text)]);
    // No visit is on a day before today; days counted from today move with it, so a today tried at another day stands
    // for no other.
    const coming = days.filter((day) => day >= scope.today);
    return tried(unique([[scope.today], ...coming.map((day) => [day])]), !relative);
  }
  if (subject === 'context:culture') {
    const cultures = scope.cultures.length > 0 ? scope.cultures : literals;
    return tried(unique(ordered(literals, cultures).map((culture) => [culture])), scope.cultures.length > 0);
  }
  if (kind === 'files') {
    const counts = literals.flatMap((text) => {
      const count = Number(text);
      return PLAIN_NUMBER.test(text) && Number.isInteger(count) ? [count - 1, count, count + 1] : [];
    });
    return tried(unique([[], ...[1, ...counts].filter((count) => count > 0).map((count) => files(count))]), true);
  }
  const known = subject.startsWith('field:') ? scope.options(subject.slice('field:'.length)) : [];
  // At most two options ticked are tried: "at least" two can be told from fewer, "at most" one from more.
  const fewCounts = conditionsOn(subject, rules).every((condition) => {
    const op = String(condition['op']);
    return op === 'countAtLeast' ? countOf(condition) <= 2 : op === 'countAtMost' ? countOf(condition) <= 1 : true;
  });
  const several = subject.startsWith('field:') && scope.several?.(subject.slice('field:'.length)) === true;
  if (kind === 'choices') {
    const values = known.length > 0 ? ordered(literals, known) : literals;
    const named = literals.filter((value) => values.includes(value));
    const all = unique([[], ...values.map((value) => [value]), named, ...pairs(values)]);
    const groups = subject === 'context:memberGroup';
    // A member may be in any group, so groups stand for the others only where the conditions name them.
    const complete =
      all.length <= MOST_SETS &&
      named.length <= 2 &&
      fewCounts &&
      (groups ? ops.every((op) => NAMED.has(op)) : known.length > 0);
    return tried(all.slice(0, MOST_SETS), complete && !several);
  }
  if (kind === 'choice')
    return tried(
      unique([[], ...(known.length > 0 ? ordered(literals, known) : literals).map((v) => [v])]),
      known.length > 0 && !several,
    );
  if (kind === 'text') {
    const fresh = othersAlike(subject, rules) ? freshText(literals) : null;
    return {
      candidates: unique([[], ...[...literals, ...(fresh !== null ? [fresh] : [])].map((v) => [v])]),
      fresh,
      literals,
      // A query value may be given several times, and so may a repeatable group's child read from outside it.
      complete: fresh !== null && !several && !subject.startsWith('context:query.'),
    };
  }
  if (kind === undefined) return tried([[], ...literals.map((value) => [value])], false);
  const near = literals.flatMap((text) => neighbours(kind, text));
  const given = literals.length > 0 ? [] : [plainOf(kind, scope.today)].filter((value) => value !== null);
  // Whole days have no answer between two neighbours (times may hold seconds); numbers and moments stand for the others
  // only where tested for equality and one tried is none the rules name. An answer compared with another stands for
  // nothing, as the other moves too.
  const discrete = kind === 'date' && !relative && !compared(subject, rules);
  const other = near.some((value) => !literals.includes(value)) || literals.length === 0;
  const complete = !several && (discrete || (othersAlike(subject, rules) && !relative && other));
  return tried(unique([[], ...[...literals, ...near, ...given].map((value) => [value])]), complete);
}

/** The number of options a count names, as both runtimes read it: a plain decimal; NaN for any other operand. */
const countOf = (condition: Plain): number => {
  const value = condition['value'];
  return typeof value === 'string' && PLAIN_NUMBER.test(value) ? Number(value) : NaN;
};

/** The values a subject may take, those the rules name first. */
const ordered = (named: readonly string[], all: readonly string[]): string[] => [
  ...new Set([...named.filter((value) => all.includes(value)), ...all]),
];

const files = (count: number): string[] => Array.from({ length: Math.min(count, 99) }, (_, i) => `file${i + 1}`);

const pairs = (values: readonly string[]): string[][] =>
  values.flatMap((one, at) => values.slice(at + 1).map((other) => [one, other]));

const unique = (lists: readonly (readonly string[])[]): string[][] => {
  const seen = new Set<string>();
  const out: string[][] = [];
  for (const list of lists) {
    const key = JSON.stringify(list);
    if (seen.has(key)) continue;
    seen.add(key);
    out.push([...list]);
  }
  return out;
};

// Trying.

interface Answers {
  readonly values: Readonly<Record<string, readonly string[]>>;
  readonly context: Context;
}

/** The answers and the visit of one try; null for a visit no visitor makes, member groups without signing in. */
function answersOf(
  subjects: readonly string[],
  values: readonly (readonly string[])[],
  scope: SearchScope,
): Answers | null {
  const fields: Record<string, readonly string[]> = {};
  const query: Record<string, string> = {};
  let culture = scope.cultures[0] ?? '';
  let signedIn: boolean | null = null;
  let groups: readonly string[] = [];
  let today = scope.today;
  for (const [at, subject] of subjects.entries()) {
    const given = values[at] ?? [];
    if (subject.startsWith('field:')) fields[subject.slice('field:'.length)] = given;
    else if (subject === 'context:culture') culture = given[0] ?? culture;
    else if (subject === 'context:memberSignedIn') signedIn = given.length > 0;
    else if (subject === 'context:memberGroup') groups = given;
    else if (subject === 'context:today') today = given[0] ?? today;
    else if (subject.startsWith('context:query.') && given[0] !== undefined)
      query[subject.slice('context:query.'.length)] = given[0];
  }
  // Only a signed-in member is in groups. The engine reads each part of the visit alone, so where no rule asks whether
  // the visitor signed in, it makes no difference what the visit says.
  if (signedIn === false && groups.length > 0) return null;
  return {
    values: fields,
    context: {
      now: `${today}T12:00:00Z`,
      timeZone: 'UTC',
      culture,
      member: { signedIn: signedIn === true, groups: [...groups] },
      query,
      hidden: [],
    },
  };
}

/**
 * A rule as the engine reads it: the definition leaves out what is as it comes (a rule in use, a group matching all),
 * which the engine is given in full.
 */
function forEngine(rule: SearchRule): Rule {
  const group = (item: unknown): unknown => {
    const given = (typeof item === 'object' && item !== null ? item : {}) as Plain;
    return Array.isArray(given['conditions'])
      ? {
          ...given,
          match: given['match'] === 'any' ? 'any' : 'all',
          conditions: (given['conditions'] as unknown[]).map(group),
        }
      : given;
  };
  return { ...(group(rule) as Rule), enabled: rule.enabled !== false };
}

/** Whether a rule matches the answers; null where the engine refuses the rule or it is off. */
function matches(rule: SearchRule, answers: Answers, scope: SearchScope): boolean | null {
  try {
    return evaluateRule(forEngine(rule), { fields: scope.fields, values: answers.values, context: answers.context });
  } catch (error) {
    if (error instanceof LogicError) return null;
    throw error;
  }
}

/** How many sets of answers the lists make. */
const trialsOf = (lists: readonly (readonly unknown[])[]): number =>
  lists.reduce((total, list) => total * Math.max(list.length, 1), 1);

/** The first set of answers, one from each list, for which `holds` says yes; at most MOST_TRIED sets are tried. */
function search(
  lists: readonly (readonly (readonly string[])[])[],
  holds: (values: readonly (readonly string[])[]) => boolean,
): { readonly values: (readonly string[])[] } | null {
  // The longest lists lose their last tries first, so every subject keeps its answers that matter most.
  const trimmed = lists.map((list) => [...list]);
  while (trialsOf(trimmed) > MOST_TRIED) {
    const longest = trimmed.reduce((best, list, at) => (list.length > (trimmed[best]?.length ?? 0) ? at : best), 0);
    trimmed[longest]?.pop();
  }
  const at = trimmed.map(() => 0);
  for (;;) {
    const values = trimmed.map((list, i) => list[at[i] ?? 0] ?? []);
    if (holds(values)) return { values };
    let i = 0;
    for (; i < at.length; i++) {
      at[i] = (at[i] ?? 0) + 1;
      if ((at[i] ?? 0) < (trimmed[i]?.length ?? 0)) break;
      at[i] = 0;
    }
    if (i === at.length) return null;
  }
}
