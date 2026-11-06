// An action's last run in words (ROADMAP 10.2f, ADR-0195): the card's line in each state, its times relative or at the
// time of day, what happened in the backoffice's words, the tries, and Details only for a run that failed or is uncertain.
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import { beforeAll, describe, expect, it } from 'vitest';
import { registerEnglish } from '../../testing';
import { loadActionWords } from './action-words';
import { happenedOf, runDetails, runLine, triesOf, type LastRun } from './run-words';

type Term = (key: string, ...args: unknown[]) => string;

const termIn = (lang: string): Term => {
  const words = umbLocalizationManager.localizations.get(lang) as unknown as Record<string, unknown>;
  return (key, ...args) => {
    const word = words[`jfbActions_${key}`];
    return typeof word === 'function' ? String(word(...args)) : String(word ?? key);
  };
};

let term: Term;
let german: Term;

beforeAll(async () => {
  registerEnglish();
  await loadActionWords('en');
  await loadActionWords('de');
  term = termIn('en');
  german = termIn('de');
});

// 6 November 2026, 14:30 where the reader is.
const now = new Date(2026, 10, 6, 14, 30);
const at = (hour: number, minute: number, day = 6, month = 10, year = 2026, second = 0) =>
  new Date(year, month, day, hour, minute, second).toISOString();
const run = (more: Partial<LastRun>): LastRun => ({ actionKey: 'a', state: 'succeeded', attempts: 1, ...more });
const line = (more: Partial<LastRun>, type = 'webhook') => runLine(run(more), type, now, 'en-GB', term);

describe("an action's last run in words", () => {
  it('says a success by what its type does: lately relative to now, else by its date', () => {
    expect(line({ finishedUtc: at(14, 29, 6, 10, 2026, 30) })).toEqual({
      text: 'Delivered just now',
      tone: 'muted',
      details: false,
    });
    expect(
      [at(14, 2), at(12, 30), at(23, 10, 5), at(9, 0, 3), at(9, 0, 29, 9)].map(
        (finishedUtc) => line({ finishedUtc })?.text,
      ),
    ).toEqual([
      'Delivered 28 minutes ago',
      'Delivered 2 hours ago',
      // By calendar day: last night is yesterday.
      'Delivered yesterday',
      'Delivered 3 days ago',
      'Delivered on 29 Oct',
    ]);
    // An email hands its mail to the outbox; another type runs.
    expect(line({ finishedUtc: at(12, 30) }, 'email')?.text).toBe('Mail queued 2 hours ago');
    expect(line({ finishedUtc: at(12, 30) }, 'content')?.text).toBe('Ran 2 hours ago');
    expect(line({ finishedUtc: at(12, 30) }, 'crm')?.text).toBe('Ran 2 hours ago');
    expect(line({})?.text).toBe('Delivered');
  });

  it('says a failure at its time of day, with the next try while it is tried again, and offers Details', () => {
    expect(
      line({
        state: 'pending',
        attempts: 2,
        lastAttemptUtc: at(14, 2),
        nextAttemptUtc: at(14, 45),
        errorCode: 'http_503',
      }),
    ).toEqual({ text: 'Failed at 14:02. Next try at 14:45.', tone: 'danger', details: true });
    expect(line({ state: 'failed', attempts: 5, lastAttemptUtc: at(14, 1), finishedUtc: at(14, 2) })).toEqual({
      text: 'Failed at 14:02.',
      tone: 'danger',
      details: true,
    });
    // Another day says its date, another year its year too.
    expect(line({ state: 'failed', finishedUtc: at(9, 15, 5) })?.text).toBe('Failed on 5 Nov at 09:15.');
    expect(line({ state: 'failed', finishedUtc: at(9, 15, 5, 10, 2025) })?.text).toBe('Failed on 5 Nov 2025 at 09:15.');
    // A next try tomorrow.
    expect(line({ state: 'pending', lastAttemptUtc: at(14, 2), nextAttemptUtc: at(8, 0, 7) })?.text).toBe(
      'Failed at 14:02. Next try on 7 Nov at 08:00.',
    );
    // The latest try's time, not the first's.
    expect(
      line({
        state: 'pending',
        attempts: 3,
        firstAttemptUtc: at(13, 40),
        lastAttemptUtc: at(14, 2),
        nextAttemptUtc: at(14, 45),
      })?.text,
    ).toBe('Failed at 14:02. Next try at 14:45.');
    // A next try that is due already waits for the queue.
    expect(line({ state: 'pending', lastAttemptUtc: at(14, 2), nextAttemptUtc: at(14, 30) })?.text).toBe(
      'Failed at 14:02. Next try soon.',
    );
    // A run tried before the site kept its latest try says its first.
    expect(line({ state: 'pending', firstAttemptUtc: at(13, 40), nextAttemptUtc: at(14, 45) })?.text).toBe(
      'Failed at 13:40. Next try at 14:45.',
    );
    // One tried several times then knows no latest time, and says none rather than its first's.
    expect(line({ state: 'pending', attempts: 3, firstAttemptUtc: at(13, 40), nextAttemptUtc: at(14, 45) })?.text).toBe(
      'Failed. Next try at 14:45.',
    );
  });

  it('says an uncertain run in the warning colour, that it may have gone through, and offers Details', () => {
    expect(line({ state: 'uncertain', lastAttemptUtc: at(14, 2), errorCode: 'timed_out' })).toEqual({
      text: 'Uncertain at 14:02. It may have gone through.',
      tone: 'warning',
      details: true,
    });
    expect(line({ state: 'uncertain', attempts: 2, firstAttemptUtc: at(13, 40) })?.text).toBe(
      'Uncertain. It may have gone through.',
    );
  });

  it('says a run that is running, or was skipped, muted and without Details; nothing for a state it does not know', () => {
    expect(line({ state: 'running', lastAttemptUtc: at(14, 29) })).toEqual({
      text: 'Running now',
      tone: 'muted',
      details: false,
    });
    expect(line({ state: 'skipped', finishedUtc: at(12, 30) })).toEqual({
      text: 'Skipped 2 hours ago',
      tone: 'muted',
      details: false,
    });
    expect(line({ state: 'later' })).toBeNull();
  });

  it("says what happened in the backoffice's words, else the type's own message, else that it failed", () => {
    const happened = (errorCode: string | null, errorMessage: string | null = null) =>
      happenedOf(run({ state: 'failed', errorCode, errorMessage }), term);
    expect(happened('http_503')).toBe('The server answered 503.');
    expect(happened('timed_out')).toBe("The server didn't answer in time.");
    expect(happened('crm_refused', 'The CRM turned the record down.')).toBe('The CRM turned the record down.');
    expect(happened('crm_refused')).toBe("It failed without saying why. A developer finds why in the site's log.");
    expect(happened(null)).toBe("It failed without saying why. A developer finds why in the site's log.");
    // Every code it knows has words of its own, which name no code.
    for (const code of [
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
    ]) {
      expect(happened(code, 'English'), code).not.toMatch(/runError_|English|_/);
      expect(happenedOf(run({ errorCode: code }), german), code).not.toMatch(/runError_/);
    }
  });

  it("says the tries, the first one's time where there were several, as the line says the latest's", () => {
    const tries = (more: Partial<LastRun>) => triesOf(run(more), now, 'en-GB', term);
    expect(tries({ attempts: 1, firstAttemptUtc: at(14, 2), lastAttemptUtc: at(14, 2) })).toBe('Tried once.');
    expect(tries({ attempts: 3, firstAttemptUtc: at(13, 40), lastAttemptUtc: at(14, 2) })).toBe(
      'Tried 3 times, the first at 13:40.',
    );
    expect(tries({ attempts: 3, firstAttemptUtc: at(23, 40, 5), lastAttemptUtc: at(14, 2) })).toBe(
      'Tried 3 times, the first on 5 Nov at 23:40.',
    );
    expect(tries({ attempts: 2 })).toBe('Tried 2 times.');
    // Tried several times before the site kept the latest try's time: the first's is still the first's.
    expect(tries({ attempts: 3, firstAttemptUtc: at(13, 40) })).toBe('Tried 3 times, the first at 13:40.');
  });

  it('gives Details for a run that failed or is uncertain, and none for another', () => {
    expect(runDetails(run({ finishedUtc: at(12, 30) }), 'webhook', now, 'en-GB', term)).toBeNull();
    expect(
      runDetails(
        run({
          state: 'failed',
          attempts: 1,
          firstAttemptUtc: at(14, 2),
          lastAttemptUtc: at(14, 2),
          finishedUtc: at(14, 2),
          errorCode: 'http_400',
        }),
        'webhook',
        now,
        'en-GB',
        term,
      ),
    ).toEqual({
      line: { text: 'Failed at 14:02.', tone: 'danger', details: true },
      happened: 'The server answered 400.',
      tries: 'Tried once.',
    });
  });

  it('says the line in German, its time first where German puts it', () => {
    const inGerman = (more: Partial<LastRun>, type = 'webhook') => runLine(run(more), type, now, 'de', german)?.text;
    expect(inGerman({ finishedUtc: at(12, 30) })).toBe('Vor 2 Stunden zugestellt');
    expect(inGerman({ finishedUtc: at(23, 10, 5) })).toBe('Gestern zugestellt');
    expect(inGerman({ finishedUtc: at(14, 29, 6, 10, 2026, 30) }, 'email')).toBe(
      'Gerade eben zum Versand \u00fcbergeben',
    );
    expect(inGerman({ state: 'pending', lastAttemptUtc: at(14, 2), nextAttemptUtc: at(14, 45) })).toBe(
      'Fehlgeschlagen um 14:02. N\u00e4chster Versuch um 14:45.',
    );
  });
});
