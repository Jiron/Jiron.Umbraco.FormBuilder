// An action's last run in words (ROADMAP 10.2f, ADR-0195): the line its card says under its name, and what its Details
// say of a failed or uncertain one: what happened, in the backoffice's words where it knows the code, and the tries.
// Times are the reader's: a success lately is said relative to now, a failure at its time of day, with its date where it
// was another day. A run cut off is "uncertain", copy.md's word for it.
import type { ActionLastRunResponseModel } from '../../api';

/** An action's last run as the server answers it: no entry and no answer (ADR-0195). */
export type LastRun = ActionLastRunResponseModel;

type Term = (key: string, ...args: unknown[]) => string;

/** How a line reads: muted, a failure in the danger colour, or an uncertain run in the warning colour. */
export type RunTone = 'muted' | 'danger' | 'warning';

/** An action's last run on its card. */
export interface RunLine {
  readonly text: string;
  readonly tone: RunTone;
  /** Whether it offers Details: a run that failed, or that is uncertain. */
  readonly details: boolean;
}

/** What Details say of a run. */
export interface RunDetails {
  /** The card's line again. */
  readonly line: RunLine;
  /** What happened: the failure in the backoffice's words, or the type's own message. */
  readonly happened: string;
  /** The tries: how many, and when. */
  readonly tries: string;
}

/** The failure codes the backoffice says in its own words; another code says the type's message. */
const WORDED = new Set([
  'timed_out',
  'no_connection',
  'not_allowed',
  'no_address',
  'secret_missing',
  'secret_literal',
  'header_invalid',
  'too_long',
  'view_missing',
  'target_missing',
  'no_name',
  'content_refused',
  'scan_pending',
  'cannot_move',
  'attempt_timed_out',
  'host_stopped',
  'lease_expired',
  'exception',
  'unknown_type',
  'unknown_action',
]);

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

const dateOf = (text: string | null | undefined): Date | null => {
  if (!text) return null;
  const at = new Date(text);
  return Number.isNaN(at.getTime()) ? null : at;
};

const sameDay = (a: Date, b: Date) =>
  a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();

/** A day as a line names it: "5 Nov", with the year where it is another. */
const dayOf = (at: Date, now: Date, culture: string) =>
  new Intl.DateTimeFormat(culture, {
    day: 'numeric',
    month: 'short',
    ...(at.getFullYear() === now.getFullYear() ? {} : { year: 'numeric' }),
  }).format(at);

/** A time of day as the reader's language writes it: "14:02", "2:02 PM". */
const timeOf = (at: Date, culture: string) => new Intl.DateTimeFormat(culture, { timeStyle: 'short' }).format(at);

/** When something happened or happens, as a line says it: "at 14:02" today, else "on 5 Nov at 14:02". */
export function atTime(at: Date, now: Date, culture: string, term: Term): string {
  return sameDay(at, now)
    ? term('runAt', timeOf(at, culture))
    : term('runOn', dayOf(at, now, culture), timeOf(at, culture));
}

/** How long ago, as a success says it: "just now", "2 hours ago", "yesterday", then its date. */
export function agoOf(at: Date, now: Date, culture: string, term: Term): string {
  const past = now.getTime() - at.getTime();
  if (past < MINUTE) return term('runJustNow');
  const relative = new Intl.RelativeTimeFormat(culture, { numeric: 'auto' });
  if (past < HOUR) return relative.format(-Math.floor(past / MINUTE), 'minute');
  if (sameDay(at, now)) return relative.format(-Math.floor(past / HOUR), 'hour');
  // By calendar days, so a run at 23:00 yesterday is "yesterday" at 08:00.
  const midnight = (day: Date) => new Date(day.getFullYear(), day.getMonth(), day.getDate()).getTime();
  const days = Math.round((midnight(now) - midnight(at)) / DAY);
  if (days < 7) return relative.format(-days, 'day');
  return term('runOnDay', dayOf(at, now, culture));
}

/** What a success of the type is called: a webhook (Slack and Teams too) delivers, an email queues its mail. */
const succeeded = (type: string) => (type === 'webhook' ? 'runDelivered' : type === 'email' ? 'runQueued' : 'runRan');

/**
 * An action's last run as its card says it; null for a state the backoffice does not know. A run that waits for its
 * first try never comes: the server answers the one before (ADR-0195).
 */
export function runLine(run: LastRun, type: string, now: Date, culture: string, term: Term): RunLine | null {
  // A run tried before the site kept its latest try's time knows only its first's, which is the latest's only where it
  // was tried once; another says no time rather than a wrong one.
  const last = dateOf(run.lastAttemptUtc) ?? (run.attempts <= 1 ? dateOf(run.firstAttemptUtc) : null);
  const ended = dateOf(run.finishedUtc) ?? last;
  const muted = (text: string): RunLine => ({ text, tone: 'muted', details: false });
  switch (run.state) {
    case 'running':
      return muted(term('runRunning'));
    case 'succeeded':
      return muted(ended ? term(succeeded(type), agoOf(ended, now, culture, term)) : term(`${succeeded(type)}Now`));
    case 'skipped':
      return muted(ended ? term('runSkipped', agoOf(ended, now, culture, term)) : term('runSkippedNow'));
    case 'pending': {
      // Tried, failed, and tried again at its next time; one that is due already waits for the queue, as while sending
      // is paused.
      const next = dateOf(run.nextAttemptUtc);
      const failed = last ? term('runFailed', atTime(last, now, culture, term)) : term('runFailedNow');
      if (!next) return { text: failed, tone: 'danger', details: true };
      const then = next > now ? term('runNextTry', atTime(next, now, culture, term)) : term('runNextTrySoon');
      return { text: `${failed} ${then}`, tone: 'danger', details: true };
    }
    case 'failed':
      return {
        text: ended ? term('runFailed', atTime(ended, now, culture, term)) : term('runFailedNow'),
        tone: 'danger',
        details: true,
      };
    case 'uncertain':
      return {
        text: last ? term('runUncertain', atTime(last, now, culture, term)) : term('runUncertainNow'),
        tone: 'warning',
        details: true,
      };
    default:
      return null;
  }
}

/** What happened to a run: its code in the backoffice's words, else the type's own message, else that it failed. */
export function happenedOf(run: Pick<LastRun, 'errorCode' | 'errorMessage'>, term: Term): string {
  const code = run.errorCode ?? '';
  const status = /^http_(\d{3})$/.exec(code);
  if (status) return term('runError_http', status[1]);
  if (WORDED.has(code)) return term(`runError_${code}`);
  return run.errorMessage?.trim() || term('runError_unknown');
}

/**
 * The tries of a run, under its line, which says the latest's time already: "Tried once." or "Tried 3 times, the first
 * at 13:40."
 */
export function triesOf(
  run: Pick<LastRun, 'attempts' | 'firstAttemptUtc'>,
  now: Date,
  culture: string,
  term: Term,
): string {
  if (run.attempts <= 1) return term('runTriedOnce');
  const first = dateOf(run.firstAttemptUtc);
  return first
    ? term('runTried', run.attempts, atTime(first, now, culture, term))
    : term('runTriedCount', run.attempts);
}

/** What a failed or uncertain run's Details say; null for a run that has none. */
export function runDetails(run: LastRun, type: string, now: Date, culture: string, term: Term): RunDetails | null {
  const line = runLine(run, type, now, culture, term);
  if (!line?.details) return null;
  return { line, happened: happenedOf(run, term), tries: triesOf(run, now, culture, term) };
}
