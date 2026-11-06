// Test of an action (ROADMAP 10.2c, ADR-0190): the action as a card's draft or its sidebar holds it, saved or not, run
// against the sample entry. A type that reaches what the action is set up to reach asks first; what the test came to is
// said in a toast in the backoffice's words, and a code the backoffice does not know in the type's own sentence. A
// webhook's codes are its runs' (ADR-0191), with the status a host answered, or the key of a secret the site lacks; a dry
// run's lines have codes of their own, such as the content a run would save (ADR-0192).
import type { ActionTestResponseModel } from '../../api';

/** What the server answers for a test. */
export type TestAnswer =
  | { readonly kind: 'done'; readonly result: ActionTestResponseModel }
  | { readonly kind: 'needsConfirmation' }
  | { readonly kind: 'settingsInvalid' }
  /** No result: the server's problem by its code, or no code where the site did not answer at all. */
  | { readonly kind: 'noAnswer'; readonly code?: string | undefined };

/** The server's answer as the test takes it: the result, or why there is none by the problem's code. */
export function testAnswer(data: ActionTestResponseModel | undefined, code: string | undefined): TestAnswer {
  if (data) return { kind: 'done', result: data };
  if (code === 'test_needs_confirmation') return { kind: 'needsConfirmation' };
  if (code === 'action_settings_invalid') return { kind: 'settingsInvalid' };
  return { kind: 'noAnswer', code };
}

/** What a type's test does, said before it runs (the Test button's title); none for a kind the backoffice does not know. */
export function testHint(
  kind: string | undefined,
  term: (key: string, ...args: unknown[]) => string,
): string | undefined {
  return kind === 'dryRun' || kind === 'toCurrentUser' || kind === 'afterConfirmation'
    ? term(`testHint_${kind}`)
    : undefined;
}

/**
 * What a toast says of a test: its look, and its words as one message, which Umbraco's toast reads aloud (a headline it
 * does not); each sentence says on its own what happened.
 */
export interface TestToast {
  readonly color: 'positive' | 'warning' | 'danger';
  readonly message: string;
  /** Whether it stays until it is closed: a dry run with something to act on, which takes longer to read. */
  readonly stays?: boolean;
}

// The failures the backoffice says in its words; another's sentence is the type's. A webhook's are its own, as one of
// its codes means another thing for an email.
/** The lines of a dry run that say what a run would leave out (ADR-0192). */
const LEAVES_OUT = new Set([
  'content_property_gone',
  'content_field_gone',
  'content_sensitive',
  'content_cannot_hold',
  'content_cannot_hold_value',
]);

const FAILURES = new Set(['no_address', 'mail_off', 'paused', 'view_missing', 'failed']);
const WEBHOOK_FAILURES = new Set([
  'no_address',
  'not_allowed',
  'secret_missing',
  'secret_literal',
  'header_invalid',
  'timed_out',
  'no_connection',
  'too_long',
]);

/** The lines of a dry run the backoffice says in its words (ADR-0192); another's sentence is the type's. */
const LINES = new Set([
  'content_under',
  'content_root',
  'content_page',
  'content_site',
  'content_update',
  'content_sets',
  'content_property_gone',
  'content_field_gone',
  'content_sensitive',
  'content_cannot_hold',
  'content_cannot_hold_value',
  'state_move',
]);

/** A line of what a run would do, in the backoffice's words where it knows its code. */
export function lineWords(
  line: { readonly text: string; readonly code?: string | null; readonly args: readonly string[] },
  term: (key: string, ...args: unknown[]) => string,
): string {
  if (!line.code || !LINES.has(line.code)) return line.text;
  // The properties a run sets are one list, said as the language joins a list.
  return line.code === 'content_sets'
    ? term('testLine_content_sets', line.args)
    : term(`testLine_${line.code}`, ...line.args);
}

/** A failure's words: a webhook's own, a host's status, or the backoffice's for any type; none for another code. */
function failureWords(
  type: string,
  code: string | null | undefined,
  args: readonly string[],
  term: (key: string, ...args: unknown[]) => string,
): string | undefined {
  if (!code) return undefined;
  if (type === 'content') {
    // What is missing: the document type, the item picked as the parent, or the item it must stay within.
    if (code === 'target_missing')
      return term(`testFailed_content_${args[0] === 'parent' || args[0] === 'within' ? args[0] : 'type'}`);
    if (code === 'no_name') return term('testFailed_content_no_name');
  }
  if (type === 'webhook') {
    if (/^http_\d{3}$/.test(code)) return term('testFailed_webhook_http', code.slice(5));
    // Nowhere to go: its own address is missing, or the site has none for the chat.
    if (code === 'no_address' && args[0] === 'url') return term('testFailed_webhook_no_address_url');
    if (WEBHOOK_FAILURES.has(code)) return term(`testFailed_webhook_${code}`, args[0] ?? '');
  }
  return FAILURES.has(code) ? term(`testFailed_${code}`) : undefined;
}

/**
 * A test's answer in the backoffice's words, led by the action's name where it has one, so toasts of several tests tell
 * their actions apart; the action's type picks the words of its own codes.
 */
export function testToast(
  answer: TestAnswer,
  term: (key: string, ...args: unknown[]) => string,
  name = '',
  type = '',
): TestToast {
  const said = answerToast(answer, term, type);
  return name ? { ...said, message: term('testOf', name, said.message) } : said;
}

function answerToast(answer: TestAnswer, term: (key: string, ...args: unknown[]) => string, type: string): TestToast {
  const failed = (message: string): TestToast => ({ color: 'danger', message });
  switch (answer.kind) {
    case 'settingsInvalid':
      return failed(term('testSettingsInvalid'));
    case 'needsConfirmation':
      return failed(term('testNoAnswer'));
    case 'noAnswer':
      // Without a problem's code the site did not answer at all, which trying again may mend.
      return failed(term(answer.code ? 'testNoAnswer' : 'testNoReply'));
    case 'done': {
      const { outcome, code, message, recipient, preview, args } = answer.result;
      if (outcome === 'failed')
        return failed(failureWords(type, code, args, term) ?? message ?? term('testFailed_failed'));
      if (code === 'sent_to_you' && recipient) return { color: 'positive', message: term('testSentToYou', recipient) };
      // A host's status, which says that it took the test; the type's sentence says it in English only.
      if (code === 'sent' && args[0]) return { color: 'positive', message: term('testSentStatus', args[0]) };
      // A dry run's lines, each on its own line under what it was; the toast keeps the breaks. One that says what a run
      // would leave out is a warning, which stays to be read.
      if (outcome === 'dryRun') {
        const leaves = preview.some((line) => LEAVES_OUT.has(line.code ?? ''));
        return {
          color: leaves ? 'warning' : 'positive',
          message: [term('testDryRun'), ...preview.map((line) => lineWords(line, term))].join('\n'),
          ...(leaves ? { stays: true } : {}),
        };
      }
      return { color: 'positive', message: message ? `${term('testSent')} ${message}` : term('testSent') };
    }
  }
}

/** What a test needs of the screen it runs from. */
export interface TestRun {
  /** The action's name, which its toast leads with. */
  readonly name: string;
  /** The action's type, whose codes its toast says in their own words. */
  readonly type: string;
  /** Whether the type reaches what the action is set up to reach, so the user confirms first. */
  readonly asksFirst: boolean;
  /** Asks the user; true once they confirmed. */
  readonly confirm: () => Promise<boolean>;
  /** Says the test has started: once the user said yes where it asks first, never before. */
  readonly started?: (() => void) | undefined;
  /** Asks the server, with whether the user confirmed. */
  readonly ask: (confirmed: boolean) => Promise<TestAnswer>;
  /** Says what it came to. */
  readonly say: (toast: TestToast) => void | Promise<void>;
  readonly term: (key: string, ...args: unknown[]) => string;
}

/**
 * Runs a test: asks first where it must, says it started only then, asks the user again where the server asks for it,
 * then says what it came to.
 */
export async function runTest(run: TestRun): Promise<void> {
  if (run.asksFirst && !(await run.confirm())) return;
  run.started?.();
  let answer = await run.ask(run.asksFirst);
  if (answer.kind === 'needsConfirmation' && !run.asksFirst) {
    if (!(await run.confirm())) return;
    answer = await run.ask(true);
  }
  await run.say(testToast(answer, run.term, run.name, run.type));
}
