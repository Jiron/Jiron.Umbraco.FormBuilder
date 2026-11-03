// What the Overview's setup checklist shows (docs/ux/information-architecture.md, section 2): the logic without Umbraco, so the
// tests read it as it is.
import type { ChecklistItemResponseModel, HealthFindingResponseModel } from '../api';

/**
 * - `done`: its check and its name;
 * - `next`: the first open step the user can do now, with its action as a button;
 * - `open`: a later open step, its name a link, so steps can be done in any order;
 * - `waiting`: it can't work yet, as there is no form to preview;
 * - `needsSomeone`: the user can't do it, and it says who can.
 */
export type StepState = 'done' | 'next' | 'open' | 'waiting' | 'needsSomeone';

export interface StepView {
  step: string;
  state: StepState;
}

/** Each step's state, in the order of the steps. */
export function stepViews(items: readonly ChecklistItemResponseModel[]): StepView[] {
  const noFormYet = items.some((item) => item.step === 'form' && !item.done);
  let nextTaken = false;
  return items.map(({ step, done, canDo }) => {
    if (done) return { step, state: 'done' };
    if (step === 'testEntry' && noFormYet) return { step, state: 'waiting' };
    if (!canDo) return { step, state: 'needsSomeone' };
    if (nextTaken) return { step, state: 'open' };
    nextTaken = true;
    return { step, state: 'next' };
  });
}

/** How many steps are done. */
export const doneCount = (items: readonly ChecklistItemResponseModel[]): number =>
  items.filter((item) => item.done).length;

/**
 * The findings the Health line lists: while the checklist shows an open email step, that step already says what a finding about
 * email would (say it once).
 */
export function findingsToShow(
  findings: readonly HealthFindingResponseModel[],
  checklistShown: boolean,
  items: readonly ChecklistItemResponseModel[],
): HealthFindingResponseModel[] {
  const covered = checklistShown && items.some((item) => item.step === 'email' && !item.done);
  return findings.filter((finding) => !(covered && finding.coversEmail));
}

/** The time an entry arrived, as the list says it: minutes and hours for what is recent, then the day and time. */
export function receivedText(
  received: Date,
  now: Date,
  culture: string,
  words: {
    justNow: string;
    minutes: (count: number) => string;
    hours: (count: number) => string;
    today: (time: string) => string;
    yesterday: (time: string) => string;
  },
): string {
  const minutes = Math.floor((now.getTime() - received.getTime()) / 60_000);
  if (minutes < 1) return words.justNow;
  if (minutes < 60) return words.minutes(minutes);
  if (minutes < 6 * 60) return words.hours(Math.floor(minutes / 60));

  const time = new Intl.DateTimeFormat(culture, { hour: '2-digit', minute: '2-digit' }).format(received);
  const dayOf = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate()).getTime();
  const days = Math.round((dayOf(now) - dayOf(received)) / 86_400_000);
  if (days === 0) return words.today(time);
  if (days === 1) return words.yesterday(time);
  return new Intl.DateTimeFormat(culture, { dateStyle: 'medium' }).format(received);
}
