// What the History list says of each version (ROADMAP 10.3a, IA 5.6, ADR-0196): its name, when it was made and by whom,
// and what changed against the one before in a few words ("4 fields, After sending, 1 rule"). Pure.
import type { DefinitionChangeSummaryResponseModel, FormVersionResponseModel } from '../../api';
import { dayText } from '../../library/rows';
import { groupTerm } from './change-words';

type Term = (key: string, ...args: unknown[]) => string;

/**
 * A moment as a row says it: "Today, 09:12", "3 Oct, 14:02", with the year where it is another; within a sentence
 * "today at 09:12", so no comma splits the sentence it is in.
 */
export function momentText(when: Date, now: Date, locale: string, term: Term, inSentence = false): string {
  const small = (word: string) => (inSentence ? word.toLocaleLowerCase(locale) : word);
  const day = dayText(when, now, locale, {
    today: small(term('jfbHistory_today')),
    yesterday: small(term('jfbHistory_yesterday')),
  });
  const time = new Intl.DateTimeFormat(locale, { timeStyle: 'short' }).format(when);
  return inSentence ? term('jfbHistory_momentAt', day, time) : `${day}, ${time}`;
}

/** When a version was made: a publish when it was published, a draft save when it was saved. */
export const madeAt = (version: FormVersionResponseModel): Date =>
  new Date(version.kind === 'published' ? (version.publishedUtc ?? version.createdUtc) : version.createdUtc);

/** A version as its row names it: "Version 14", "Draft save", or "Current draft"; a publish is a version first. */
export function rowName(version: FormVersionResponseModel, term: Term): string {
  if (version.kind === 'published') return term('jfbHistory_version', version.number);
  return term(version.isDraft ? 'jfbHistory_currentDraft' : 'jfbHistory_draftSave');
}

/** A version as a sentence names it: "version 14", "the draft save of today, 09:12", "the current draft". */
export function versionName(version: FormVersionResponseModel, now: Date, locale: string, term: Term): string {
  if (version.kind === 'published') return term('jfbHistory_versionInSentence', version.number);
  return version.isDraft
    ? term('jfbHistory_compare_draft')
    : term('jfbHistory_nameDraftSave', momentText(madeAt(version), now, locale, term, true));
}

/** A name that starts a heading: its first letter large. */
export const headingCase = (name: string): string => name.charAt(0).toLocaleUpperCase() + name.slice(1);

/** The settings groups in the order the Settings view shows them. */
const GROUPS = ['submission', 'availability', 'protection', 'privacy', 'appearance', 'developer'];

/**
 * What changed against the version before, in a few words: an element kind with one change by its name, with more by
 * their number; settings groups by their names, outcomes as After sending, rules by their number.
 */
export function summaryWords(changes: readonly DefinitionChangeSummaryResponseModel[], term: Term): string {
  if (changes.length === 0) return term('jfbHistory_changes_none');
  return summaryParts(changes, term).join(', ');
}

/**
 * The parts of a summary, each a kind of element or a settings group, for a sentence to join as its language does; in a
 * sentence, After sending is set off as the language sets off a name.
 */
export function summaryParts(
  changes: readonly DefinitionChangeSummaryResponseModel[],
  term: Term,
  inSentence = false,
): string[] {
  const of = (kinds: readonly string[]) => changes.filter((change) => kinds.includes(change.kind));
  const named = (kind: string, countTerm: string): string[] => {
    const these = of([kind]);
    if (these.length === 0) return [];
    const only = these[0];
    return these.length === 1 && only?.label ? [only.label] : [term(countTerm, these.length)];
  };
  const groups = GROUPS.filter((group) =>
    changes.some((change) => change.kind === 'settings' && change.settingsGroup === group),
  ).map((group) => term(groupTerm(group)));
  const rules = of(['rule', 'validationRule']).length;
  return [
    ...named('page', 'jfbHistory_changes_pages'),
    ...named('group', 'jfbHistory_changes_groups'),
    ...named('field', 'jfbHistory_changes_fields'),
    ...groups,
    ...(of(['outcome']).length > 0
      ? [
          inSentence
            ? term('jfbHistory_afterInSentence', term('jfbDesigner_afterSending'))
            : term('jfbDesigner_afterSending'),
        ]
      : []),
    ...named('action', 'jfbHistory_changes_actions'),
    ...(rules > 0 ? [term('jfbHistory_changes_rules', rules)] : []),
  ];
}
