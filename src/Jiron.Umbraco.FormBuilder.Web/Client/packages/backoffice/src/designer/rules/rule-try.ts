// Try it, the designer's live rule preview (ROADMAP 9.5a, ADR-0175, D10): a box for each field and part of the visit a
// rule looks at, and what the element then does. The answer is the engine's (`evaluateRule` of `@jfb/logic`), reached
// only through tryRule, which the shared vectors hold to the website's and the server's answers.
import { evaluateRule, LogicError, type Context, type Kind, type Rule } from '@jfb/logic';
import { conditionsOf, otherFieldOf, relativeOf, subjectIdOf, type DraftRule, type Effect } from './rule-draft';

/** What the engine says of a rule with some answers: whether it matches (null while it is off), or why it can't say. */
export type TryOutcome = { readonly matches: boolean | null } | { readonly error: string };

/** A rule evaluated against answers and a visit, as both runtimes evaluate it; the engine's error code where it stops. */
export function tryRule(
  rule: Rule,
  fields: Readonly<Record<string, Kind>>,
  values: Readonly<Record<string, readonly string[]>>,
  context: Context,
): TryOutcome {
  try {
    return { matches: evaluateRule(rule, { fields, values, context }) };
  } catch (error) {
    if (error instanceof LogicError) return { error: error.code };
    throw error;
  }
}

/** What the visit's boxes hold. */
export interface TryVisit {
  readonly culture: string;
  readonly signedIn: boolean;
  readonly groups: readonly string[];
  readonly query: Readonly<Record<string, string>>;
  /** The day to try as today, `YYYY-MM-DD`. */
  readonly today: string;
}

/**
 * The context of a try. The day given is today: noon in UTC is that day, so the engine's "today", which it reads in the
 * site's time zone from the instant, is the day the editor chose. Rules see no hidden fields here: Try it tries one rule.
 */
export function tryContext(visit: TryVisit): Context {
  return {
    now: `${visit.today}T12:00:00Z`,
    timeZone: 'UTC',
    culture: visit.culture,
    member: { signedIn: visit.signedIn, groups: [...visit.groups] },
    query: { ...visit.query },
    hidden: [],
  };
}

/** What a field's box holds: a text, the options ticked, a checkbox's state, or how many files. */
export type TryBox = string | readonly string[] | boolean | number;

/** A field's answers from its box, as a post gives them: a ticked checkbox is "true", files are as many names. */
export function answersOf(kind: Kind, box: TryBox | undefined): string[] {
  if (box === undefined) return [];
  if (kind === 'checkbox') return box === true ? ['true'] : [];
  if (kind === 'files')
    return Array.from(
      { length: typeof box === 'number' ? Math.max(0, Math.min(box, 99)) : 0 },
      (_, i) => `file${i + 1}`,
    );
  if (Array.isArray(box)) return box.filter((each): each is string => typeof each === 'string');
  return typeof box === 'string' && box !== '' ? [box] : [];
}

/**
 * What Try it asks for: each field and part of the visit a rule's conditions look at or compare with, in the order of
 * their first mention, as `field:<key>` and `context:<name>`; `context:today` too where a value is counted from today.
 */
export function triedSubjects(draft: DraftRule): string[] {
  const ids: string[] = [];
  const add = (id: string) => {
    if (id !== '' && !ids.includes(id)) ids.push(id);
  };
  for (const condition of conditionsOf(draft)) {
    add(subjectIdOf(condition));
    const operands = Array.isArray(condition.value) ? condition.value : [condition.value];
    for (const operand of operands) {
      const other = otherFieldOf(operand);
      if (other !== null) add(`field:${other}`);
      if (relativeOf(operand) !== null) add('context:today');
    }
  }
  return ids;
}

/**
 * The words of what the element does with the answers tried, by the rule's effect and what the engine says. A rule that
 * is off does nothing: the element shows, and is not required (the switch's help says why).
 */
export function verdictTerm(effect: Effect, matches: boolean | null): string {
  if (matches === null) return effect === 'require' ? 'jfbRules_tryNotRequired' : 'jfbRules_tryShows';
  if (effect === 'require') return matches ? 'jfbRules_tryRequired' : 'jfbRules_tryNotRequired';
  const shown = effect === 'show' ? matches : !matches;
  return shown ? 'jfbRules_tryShows' : 'jfbRules_tryHidden';
}
