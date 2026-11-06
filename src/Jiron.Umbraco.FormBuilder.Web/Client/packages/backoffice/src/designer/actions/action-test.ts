// Test of an action (ROADMAP 10.2c, ADR-0190): the action as a card's draft or its sidebar holds it, saved or not, run
// against the sample entry. A type that reaches what the action is set up to reach asks first; what the test came to is
// said in a toast in the backoffice's words, and a code the backoffice does not know in the type's own sentence.
import type { ActionTestResponseModel } from '../../api';

/** What the server answers for a test. */
export type TestAnswer =
  | { readonly kind: 'done'; readonly result: ActionTestResponseModel }
  | { readonly kind: 'needsConfirmation' }
  | { readonly kind: 'settingsInvalid' }
  | { readonly kind: 'noAnswer' };

/** The server's answer as the test takes it: the result, or why there is none by the problem's code. */
export function testAnswer(data: ActionTestResponseModel | undefined, code: string | undefined): TestAnswer {
  if (data) return { kind: 'done', result: data };
  if (code === 'test_needs_confirmation') return { kind: 'needsConfirmation' };
  if (code === 'action_settings_invalid') return { kind: 'settingsInvalid' };
  return { kind: 'noAnswer' };
}

/** What a toast says of a test: its look, its headline and what follows it. */
export interface TestToast {
  readonly color: 'positive' | 'danger';
  readonly headline: string;
  readonly message: string;
}

// The failures the backoffice says in its words; another's sentence is the type's.
const FAILURES = new Set(['no_address', 'mail_off', 'view_missing', 'failed']);

/** A test's answer in the backoffice's words. */
export function testToast(answer: TestAnswer, term: (key: string, ...args: unknown[]) => string): TestToast {
  const failed = (message: string): TestToast => ({ color: 'danger', headline: term('testFailed'), message });
  switch (answer.kind) {
    case 'settingsInvalid':
      return failed(term('testSettingsInvalid'));
    case 'needsConfirmation':
    case 'noAnswer':
      return failed(term('testNoAnswer'));
    case 'done': {
      const { outcome, code, message, recipient, preview } = answer.result;
      if (outcome === 'failed')
        return failed(code && FAILURES.has(code) ? term(`testFailed_${code}`) : (message ?? term('testFailed_failed')));
      if (code === 'sent_to_you' && recipient)
        return { color: 'positive', headline: term('testSentToYou', recipient), message: '' };
      if (outcome === 'dryRun') return { color: 'positive', headline: term('testDryRun'), message: preview.join(' ') };
      return { color: 'positive', headline: term('testSent'), message: message ?? '' };
    }
  }
}

/** What a test needs of the screen it runs from. */
export interface TestRun {
  /** Whether the type reaches what the action is set up to reach, so the user confirms first. */
  readonly asksFirst: boolean;
  /** Asks the user; true once they confirmed. */
  readonly confirm: () => Promise<boolean>;
  /** Asks the server, with whether the user confirmed. */
  readonly ask: (confirmed: boolean) => Promise<TestAnswer>;
  /** Says what it came to. */
  readonly say: (toast: TestToast) => void | Promise<void>;
  readonly term: (key: string, ...args: unknown[]) => string;
}

/** Runs a test: asks first where it must, or where the server asks for it, then says what it came to. */
export async function runTest(run: TestRun): Promise<void> {
  if (run.asksFirst && !(await run.confirm())) return;
  let answer = await run.ask(run.asksFirst);
  if (answer.kind === 'needsConfirmation' && !run.asksFirst) {
    if (!(await run.confirm())) return;
    answer = await run.ask(true);
  }
  await run.say(testToast(answer, run.term));
}
