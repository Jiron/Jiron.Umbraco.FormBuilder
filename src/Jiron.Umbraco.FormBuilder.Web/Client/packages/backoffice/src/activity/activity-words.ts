// The activity screens' words (ROADMAP 12.4b, ADR-0219): loaded with Deliveries and the audit log, not with the
// backoffice's start, whose budget counts every word it loads (ADR-0183). What happened to a run keeps the Actions view's
// words, which load with them, and a settings save names its fields with the global settings' words.
import { loadActionWords, wordsLoader } from '../designer/actions/action-words';
import { loadSettingsWords } from '../settings/settings-words';

const loadActivityOnly = wordsLoader((code) =>
  code === 'de' ? import('../lang/activity.de') : import('../lang/activity.en'),
);

/** Loads Deliveries' words and the run words they share with the Actions view, once each. */
export const loadDeliveryWords = (lang: string): Promise<void> =>
  Promise.all([loadActivityOnly(lang), loadActionWords(lang)]).then(() => undefined);

/** Loads the audit log's words and the settings' names its sentences use, once each. */
export const loadAuditWords = (lang: string): Promise<void> =>
  Promise.all([loadActivityOnly(lang), loadSettingsWords(lang)]).then(() => undefined);

/** When something happened, as a list says it: the time today, "yesterday" and the time, else the date and the time. */
export function whenOf(at: Date, now: Date, culture: string): string {
  const time = new Intl.DateTimeFormat(culture, { timeStyle: 'short' }).format(at);
  const midnight = (day: Date) => new Date(day.getFullYear(), day.getMonth(), day.getDate()).getTime();
  const days = Math.round((midnight(now) - midnight(at)) / 86_400_000);
  if (days === 0) return time;
  if (days === 1) {
    const yesterday = new Intl.RelativeTimeFormat(culture, { numeric: 'auto' }).format(-1, 'day');
    return `${yesterday.charAt(0).toLocaleUpperCase(culture)}${yesterday.slice(1)}, ${time}`;
  }
  const date = new Intl.DateTimeFormat(culture, {
    day: 'numeric',
    month: 'short',
    ...(at.getFullYear() === now.getFullYear() ? {} : { year: 'numeric' }),
  }).format(at);
  return `${date}, ${time}`;
}

/** A moment in full, with its seconds, and the zone it is written in: the reader's. */
export function fullTimeOf(at: Date, culture: string): { readonly time: string; readonly zone: string } {
  const time = new Intl.DateTimeFormat(culture, { dateStyle: 'full', timeStyle: 'medium' }).format(at);
  return { time, zone: Intl.DateTimeFormat().resolvedOptions().timeZone };
}

/** A date the server sent, or null for none or one that isn't. */
export const dateOf = (text: string | null | undefined): Date | null => {
  if (!text) return null;
  const at = new Date(text);
  return Number.isNaN(at.getTime()) ? null : at;
};
