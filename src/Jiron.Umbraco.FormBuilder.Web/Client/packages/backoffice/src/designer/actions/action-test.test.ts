// Test of an action (ROADMAP 10.2c, ADR-0190): the server's answer as the test takes it, what a toast says of it in the
// backoffice's words or the type's own sentence, and a run that asks first where it must or where the server asks.
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import type { ActionTestResponseModel } from '../../api';
import { registerEnglish } from '../../testing';
import { runTest, testAnswer, testHint, testToast, type TestAnswer } from './action-test';
import { loadActionWords } from './action-words';

const result = (more: Partial<ActionTestResponseModel>): TestAnswer => ({
  kind: 'done',
  result: { outcome: 'sent', preview: [], args: [], ...more },
});

let term: (key: string, ...args: unknown[]) => string;

beforeAll(async () => {
  registerEnglish();
  await loadActionWords('en');
  const words = umbLocalizationManager.localizations.get('en') as unknown as Record<string, unknown>;
  term = (key, ...args) => {
    const word = words[`jfbActions_${key}`];
    return typeof word === 'function' ? String(word(...args)) : String(word ?? key);
  };
});

describe("an action's test", () => {
  it("takes the server's answer, or why there is none by the problem's code", () => {
    const shown: ActionTestResponseModel = { outcome: 'sent', preview: [], args: [] };
    expect(testAnswer(shown, undefined)).toEqual({ kind: 'done', result: shown });
    expect(testAnswer(undefined, 'test_needs_confirmation')).toEqual({ kind: 'needsConfirmation' });
    expect(testAnswer(undefined, 'action_settings_invalid')).toEqual({ kind: 'settingsInvalid' });
    expect(testAnswer(undefined, 'action_not_found')).toEqual({ kind: 'noAnswer', code: 'action_not_found' });
    expect(testAnswer(undefined, undefined)).toEqual({ kind: 'noAnswer' });
  });

  it('says before it runs what each kind of test does, and nothing for a kind it does not know', () => {
    expect(
      ['dryRun', 'toCurrentUser', 'afterConfirmation', 'other', undefined].map((kind) => testHint(kind, term)),
    ).toEqual([
      'Shows what a run would do. Nothing is sent or changed.',
      'Sends a test to you alone.',
      'Sends a sample entry where this action sends, after asking you.',
      undefined,
      undefined,
    ]);
  });

  it("says what it came to in its words, and a code it does not know in the type's sentence, all in its message", () => {
    // Umbraco's toast reads its message aloud, not a headline: each says on its own what happened.
    expect(testToast(result({ code: 'sent_to_you', recipient: 'ana@example.com' }), term)).toEqual({
      color: 'positive',
      message: 'Test email sent to you (ana@example.com).',
    });
    expect(testToast(result({ code: 'sent', message: 'The host answered 200.' }), term)).toEqual({
      color: 'positive',
      message: 'Test sent. The host answered 200.',
    });
    expect(testToast(result({ code: 'sent' }), term).message).toBe('Test sent.');
    // Led by the action's name, so toasts of several tests tell their actions apart.
    expect(
      testToast(result({ code: 'sent_to_you', recipient: 'ana@example.com' }), term, 'Email the team').message,
    ).toBe('Email the team: Test email sent to you (ana@example.com).');
    expect(
      testToast(result({ outcome: 'dryRun', preview: ['It would save a page.', 'It would publish it.'] }), term),
    ).toEqual({
      color: 'positive',
      message: 'Nothing was sent or changed.\nIt would save a page.\nIt would publish it.',
    });
    const failed = (code: string | null, message: string | null) =>
      testToast(result({ outcome: 'failed', code, message }), term);
    expect(failed('no_address', 'English sentence')).toEqual({
      color: 'danger',
      message: 'Your account has no email address, so a test email has nowhere to go.',
    });
    expect(failed('paused', null).message).toBe("Actions and email are paused on this site, so the test can't go out.");
    expect(failed('mail_off', null).message).toBe(
      "This site can't send email yet: it has no mail server or pickup folder. A developer sets up sending in the site's Umbraco:CMS:Global:Smtp settings.",
    );
    expect(failed('view_missing', null).message).toBe(
      'This site has no Razor view the email names, and the email has no body of its own.',
    );
    expect(failed('failed', null).message).toBe("The test couldn't finish. A developer finds why in the site's log.");
    // Another type's code the backoffice does not know: its sentence.
    expect(failed('http_503', 'The webhook was not delivered (http_503).').message).toBe(
      'The webhook was not delivered (http_503).',
    );
    expect(failed(null, null).message).toBe("The test couldn't finish. A developer finds why in the site's log.");
    expect(testToast({ kind: 'settingsInvalid' }, term).message).toBe(
      "This action's settings have errors. Open the action, fix them, then test again.",
    );
    // A problem the server named, which trying again does not mend; no answer at all, which it may.
    expect(testToast({ kind: 'noAnswer', code: 'action_type_unknown' }, term).message).toBe(
      "The test couldn't be run.",
    );
    expect(testToast({ kind: 'noAnswer' }, term).message).toBe("The site didn't answer. Try again in a moment.");
  });

  it("says a webhook's codes in its own words, with the status a host answered or the key a site lacks (ADR-0191)", () => {
    const hook = (code: string, args: string[] = [], outcome = 'failed') =>
      testToast(result({ outcome, code, args, message: 'English sentence' }), term, '', 'webhook').message;
    expect(hook('sent', ['201'], 'sent')).toBe('Test sent. The server answered 201.');
    expect(hook('http_404')).toBe("The server answered 404, so the test wasn't delivered.");
    expect(hook('http_503')).toBe("The server answered 503, so the test wasn't delivered.");
    expect(hook('no_address', ['slack'])).toBe(
      'Nowhere to send it: the site has no address for this chat yet, and the action names none of its own.',
    );
    expect(hook('no_address', ['url'])).toBe('This webhook has no address yet. Give it one under Where it goes.');
    expect(hook('not_allowed')).toBe(
      "The site doesn't call this address. Webhooks go to https addresses on the hosts in JironFormBuilder:Webhooks:AllowedHosts, never into the site's own network.",
    );
    expect(hook('secret_missing', ['JironFormBuilder:Secrets:CrmToken'])).toBe(
      "The site's configuration has no JironFormBuilder:Secrets:CrmToken yet, so the test can't go out. A developer adds it.",
    );
    expect(hook('secret_literal')).toBe(
      'This action holds a secret as itself. Give it as a reference, then test again.',
    );
    expect(hook('header_invalid')).toBe(
      "A header from the site's configuration holds characters a header can't carry.",
    );
    expect(hook('timed_out')).toBe("The server didn't answer in time. The test may have arrived anyway.");
    expect(hook('no_connection')).toBe("The server couldn't be reached.");
    expect(hook('too_long')).toBe('The body is too long to send.');
    expect(hook('paused')).toBe("Actions and email are paused on this site, so the test can't go out.");
    expect(hook('failed')).toBe("The test couldn't finish. A developer finds why in the site's log.");
    // A code no word knows: the type's sentence; an email's no_address is its own.
    expect(hook('http_5x')).toBe('English sentence');
    expect(testToast(result({ outcome: 'failed', code: 'no_address' }), term, '', 'email').message).toBe(
      'Your account has no email address, so a test email has nowhere to go.',
    );
  });

  it('asks first where the type reaches what the action is set up to reach, starting only after a yes', async () => {
    const steps: string[] = [];
    const ask = vi.fn(async () => {
      steps.push('ask');
      return result({ code: 'sent' });
    });
    const say = vi.fn();
    const started = () => steps.push('started');
    await runTest({
      name: 'Hook',
      type: 'webhook',
      asksFirst: true,
      confirm: async () => false,
      started,
      ask,
      say,
      term,
    });
    expect([steps, say.mock.calls.length]).toEqual([[], 0]);
    await runTest({
      name: 'Hook',
      type: 'webhook',
      asksFirst: true,
      confirm: async () => true,
      started,
      ask,
      say,
      term,
    });
    expect([steps, ask.mock.calls, say.mock.calls[0]?.[0]?.message]).toEqual([
      ['started', 'ask'],
      [[true]],
      'Hook: Test sent.',
    ]);
  });

  it('asks the user where the server asks for a confirmation the type did not declare, then tries again', async () => {
    const answers: TestAnswer[] = [{ kind: 'needsConfirmation' }, result({ code: 'sent' })];
    const ask = vi.fn(async () => answers.shift() ?? { kind: 'noAnswer' as const });
    const say = vi.fn();
    const confirm = vi.fn(async () => true);
    await runTest({ name: 'Hook', type: 'webhook', asksFirst: false, confirm, ask, say, term });
    expect([ask.mock.calls, confirm.mock.calls.length, say.mock.calls[0]?.[0]?.message]).toEqual([
      [[false], [true]],
      1,
      'Hook: Test sent.',
    ]);
  });
});
