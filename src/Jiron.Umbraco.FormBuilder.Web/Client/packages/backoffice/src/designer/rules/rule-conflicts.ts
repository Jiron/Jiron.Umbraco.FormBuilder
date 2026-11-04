// A conflict between a field's rules (ROADMAP 9.5c, IA 5.1, ADR-0178): a hide rule, or a show rule that doesn't hold,
// and a require rule that hold for the same answers. A hidden field is never required, so visitors can send without it.
// A conflict is said only where the engine itself (`evaluateRule` of `@jfb/logic`) finds both holding for a set of
// answers a visitor can give: no answer, those the rules name, their neighbours (one more, one less, the day before and
// after), a choice's own options, the site's languages, and, for a text the rules only test for being equal, one they
// don't name. So a conflict said is always real; one hidden in answers no rule names, such as a pattern's, may go
// unsaid, and the form check (9.5d) reasons about the form as a whole.
import {
  applyRelative,
  evaluateRule,
  formatDate,
  LogicError,
  parseDate,
  parseRelative,
  type Context,
  type Kind,
  type Operand,
  type Rule,
} from '@jfb/logic';
import type { DesignRule } from '../definition';
import { otherFieldOf, relativeOf } from './rule-draft';

/** What a conflict is found with: the kinds of the form's fields, each choice's own options, and the site's languages. */
export interface ConflictScope {
  readonly fields: Readonly<Record<string, Kind>>;
  /** The options a choice offers that the form knows, by the key of the field or grid row. */
  readonly options: (key: string) => readonly string[];
  readonly cultures: readonly string[];
  /** Today, `YYYY-MM-DD`, from which days counted from today are counted. */
  readonly today: string;
}

/** One answer of a conflict: what it is about (`field:<key>` or `context:<name>`) and the answers given. */
export interface ConflictAnswer {
  readonly subject: string;
  /** The answers given; empty for none, or for an answer the rules don't name (`noneOf`). */
  readonly values: readonly string[];
  /** Set for a text the rules don't name: the texts they do, which any other answer stands for alike. */
  readonly noneOf?: readonly string[];
}

/** A hide rule (or a show rule that doesn't hold) and a require rule that hold together, and for which answers. */
export interface Conflict {
  readonly hiding: DesignRule;
  readonly requiring: DesignRule;
  /** The answers that matter, in the order the rules first name them; empty where the rules conflict for any answer. */
  readonly answers: readonly ConflictAnswer[];
}

/** How many sets of answers are tried at most, so a field with many rules still answers at once. */
const MOST_TRIED = 4096;
/** How many answers a choice of several is tried with at most. */
const MOST_SETS = 32;

// A rule that is off the engine leaves undecided (null), as one it refuses, so neither ever conflicts.
const used = (rule: DesignRule | undefined): rule is DesignRule => rule !== undefined && rule.conditions.length > 0;

/** The conflict of a field's own rules, or null where its rules can't conflict or no answers tried make them. */
export function conflictOf(rules: readonly DesignRule[], scope: ConflictScope): Conflict | null {
  const requiring = rules.find((rule) => rule.effect === 'require');
  const hiding = rules.find((rule) => rule.effect === 'hide' || rule.effect === 'show');
  if (!used(requiring) || !used(hiding)) return null;
  const both = [hiding, requiring];
  const subjects = subjectsOf(both);
  const tries = subjects.map((subject) => triesOf(subject, both, scope));
  const candidates = tries.map(({ candidates }) => candidates);
  if (candidates.some((list) => list.length === 0)) return null;
  const conflicts = (values: readonly (readonly string[])[]): boolean => {
    const answers = answersOf(subjects, values, scope);
    if (answers === null) return false;
    const hides = matches(hiding, answers, scope);
    const requires = matches(requiring, answers, scope);
    return hides !== null && requires === true && (hiding.effect === 'hide' ? hides : !hides);
  };
  const found = search(candidates, conflicts);
  if (!found) return null;
  // Answers that conflict whatever they are, together, say nothing, so they are left out; each one left out stays
  // free while the next is tried, so the answers said hold the conflict whatever the others are.
  const free: number[] = [];
  subjects.forEach((_, at) => {
    if (freeTogether([...free, at], candidates, found, conflicts)) free.push(at);
  });
  const answers = subjects.flatMap((subject, at): ConflictAnswer[] => {
    if (free.includes(at)) return [];
    const values = found[at] ?? [];
    const { fresh, literals } = tries[at] ?? { fresh: null, literals: [] };
    return [fresh !== null && values[0] === fresh ? { subject, values: [], noneOf: literals } : { subject, values }];
  });
  return { hiding, requiring, answers };
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
  if (lists.reduce((total, list) => total * list.length, 1) > MOST_TRIED) return false;
  return search(lists, (values) => !conflicts(values)) === null;
}

// What the rules look at, and what to try.

/** Each field and part of the visit the rules look at or compare with, in the order they first name them. */
function subjectsOf(rules: readonly DesignRule[]): string[] {
  const out: string[] = [];
  const add = (id: string) => {
    if (!out.includes(id)) out.push(id);
  };
  for (const condition of conditionsIn(rules)) {
    if (typeof condition['field'] === 'string') add(`field:${condition['field']}`);
    else if (typeof condition['context'] === 'string') add(`context:${condition['context']}`);
    for (const operand of operandsOf(condition)) {
      const other = otherFieldOf(operand as Operand);
      if (other !== null) add(`field:${other}`);
      if (relativeOf(operand as Operand) !== null) add('context:today');
    }
  }
  return out;
}

type Plain = Readonly<Record<string, unknown>>;

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
  return Array.isArray(value) ? [...(value as readonly unknown[])] : value === undefined ? [] : [value];
};

const kindOf = (subject: string, scope: ConflictScope): Kind | undefined => {
  if (subject.startsWith('field:')) return scope.fields[subject.slice('field:'.length)];
  const name = subject.slice('context:'.length);
  if (name === 'culture' || name.startsWith('query.')) return 'text';
  return name === 'memberSignedIn' ? 'checkbox' : name === 'memberGroup' ? 'choices' : 'date';
};

const COUNTS: ReadonlySet<string> = new Set(['countAtLeast', 'countAtMost']);

const subjectOf = (condition: Plain): string =>
  typeof condition['field'] === 'string' ? `field:${condition['field']}` : `context:${String(condition['context'])}`;

/**
 * The texts the rules compare a subject with: the values of its own conditions (a count only where `counts` asks for
 * it, as no option is a number of ticks) and, where two answers are compared, the other's values too, so the answers
 * tried can stand on either side of each other.
 */
function literalsOf(subject: string, rules: readonly DesignRule[], scope: ConflictScope, counts: boolean): string[] {
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
        const offset = relativeOf(operand as Operand);
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
      const other = otherFieldOf(operand as Operand);
      return other === null ? [] : [`field:${other}`];
    });
    if (subjectOf(condition) === subject) others.forEach(own);
    else if (others.includes(subject)) own(subjectOf(condition));
  }
  return out;
}

/** The operators for which every answer the rules don't name is alike: none of them looks inside a text. */
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

/**
 * Whether a text's answers the rules don't name are all alike to them: each condition on it only tests it for being
 * equal to texts it names, and no condition compares it with another answer. One such answer then stands for them all.
 */
function othersAlike(subject: string, rules: readonly DesignRule[]): boolean {
  return conditionsIn(rules).every((condition) => {
    const operands = operandsOf(condition);
    if (operands.some((operand) => `field:${otherFieldOf(operand as Operand)}` === subject)) return false;
    if (subjectOf(condition) !== subject) return true;
    return ALIKE.has(String(condition['op'])) && operands.every((operand) => typeof operand === 'string');
  });
}

/** A text none of the rules' texts is, folded as the engine folds them. */
function freshText(literals: readonly string[]): string {
  const taken = new Set(literals.map((text) => text.toLocaleLowerCase('en-US')));
  let text = 'zz-other';
  while (taken.has(text)) text += '-';
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

/** Next to a number, a date, a time or a date and time: one less and one more, which comparisons tell apart. */
function neighbours(kind: Kind, text: string): string[] {
  if (kind === 'number') {
    const value = Number(text);
    return Number.isFinite(value) ? [String(value - 1), String(value + 1)] : [];
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

/** What a subject is tried with, and, where one stands for the answers the rules don't name, that one. */
interface Tries {
  readonly candidates: string[][];
  readonly fresh: string | null;
  readonly literals: readonly string[];
}

/**
 * The answers a subject is tried with, each one a visitor can give: none first (a language is always one of the
 * site's), then what the rules name and what lies next to it. A choice keeps to its options where the form knows them;
 * member groups are tried with signing in (`answersOf`).
 */
function triesOf(subject: string, rules: readonly DesignRule[], scope: ConflictScope): Tries {
  const kind = kindOf(subject, scope);
  const literals = literalsOf(subject, rules, scope, kind === 'files');
  const plain = (candidates: string[][]): Tries => ({ candidates, fresh: null, literals });
  if (subject === 'context:memberSignedIn' || kind === 'checkbox') return plain([[], ['true']]);
  if (subject === 'context:today') {
    const days = literals.flatMap((text) => [text, ...neighbours('date', text)]);
    return plain(unique([[scope.today], ...days.map((day) => [day])]));
  }
  if (subject === 'context:culture') {
    const cultures = scope.cultures.length > 0 ? scope.cultures : literals;
    return plain(unique(ordered(literals, cultures).map((culture) => [culture])));
  }
  if (kind === 'files') {
    const counts = literals.flatMap((text) => {
      const count = Number(text);
      return Number.isInteger(count) ? [count - 1, count, count + 1] : [];
    });
    return plain(unique([[], ...[1, ...counts].filter((count) => count > 0).map((count) => files(count))]));
  }
  const known = subject.startsWith('field:') ? scope.options(subject.slice('field:'.length)) : [];
  if (kind === 'choices') {
    const values = known.length > 0 ? ordered(literals, known) : literals;
    const named = literals.filter((value) => values.includes(value));
    return plain(unique([[], ...values.map((value) => [value]), named, ...pairs(values)]).slice(0, MOST_SETS));
  }
  if (kind === 'choice')
    return plain(unique([[], ...(known.length > 0 ? ordered(literals, known) : literals).map((v) => [v])]));
  if (kind === 'text') {
    const fresh = othersAlike(subject, rules) ? freshText(literals) : null;
    return {
      candidates: unique([[], ...[...literals, ...(fresh !== null ? [fresh] : [])].map((v) => [v])]),
      fresh,
      literals,
    };
  }
  if (kind === undefined) return plain([[], ...literals.map((value) => [value])]);
  const near = literals.flatMap((text) => neighbours(kind, text));
  const given = literals.length > 0 ? [] : [plainOf(kind, scope.today)].filter((value) => value !== null);
  return plain(unique([[], ...[...literals, ...near, ...given].map((value) => [value])]));
}

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
  scope: ConflictScope,
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
function forEngine(rule: DesignRule): Rule {
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

/** Whether a rule matches the answers; null where the engine refuses the rule, which then has no conflict. */
function matches(rule: DesignRule, answers: Answers, scope: ConflictScope): boolean | null {
  try {
    return evaluateRule(forEngine(rule), { fields: scope.fields, values: answers.values, context: answers.context });
  } catch (error) {
    if (error instanceof LogicError) return null;
    throw error;
  }
}

/** The first set of answers, one from each list, for which `holds` says yes; at most MOST_TRIED sets are tried. */
function search(
  lists: readonly (readonly (readonly string[])[])[],
  holds: (values: readonly (readonly string[])[]) => boolean,
): (readonly string[])[] | null {
  // The longest lists lose their last tries first, so every subject keeps its answers that matter most.
  const trimmed = lists.map((list) => [...list]);
  const count = () => trimmed.reduce((total, list) => total * Math.max(list.length, 1), 1);
  while (count() > MOST_TRIED) {
    const longest = trimmed.reduce((best, list, at) => (list.length > (trimmed[best]?.length ?? 0) ? at : best), 0);
    trimmed[longest]?.pop();
  }
  const at = trimmed.map(() => 0);
  for (;;) {
    const values = trimmed.map((list, i) => list[at[i] ?? 0] ?? []);
    if (holds(values)) return values;
    let i = 0;
    for (; i < at.length; i++) {
      at[i] = (at[i] ?? 0) + 1;
      if ((at[i] ?? 0) < (trimmed[i]?.length ?? 0)) break;
      at[i] = 0;
    }
    if (i === at.length) return null;
  }
}
