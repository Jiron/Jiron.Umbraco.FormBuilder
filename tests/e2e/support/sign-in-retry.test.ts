import assert from 'node:assert/strict';
import { test } from 'node:test';
import { isTimeout, lengthenTest, SECOND_SIGN_IN_MS, SIGN_IN_STALL_MS, signInOnceMore } from './sign-in-retry.ts';

// A sign-in stalled by Umbraco on SQLite is signed in once more, the test gets back the time the stall took, and the run
// says so (ADR-0250); a second stall, or any other failure, fails the test.

/** A Playwright timeout as a locator or a request throws it. */
function timeout(message = 'locator.waitFor: Timeout 20000ms exceeded.'): Error {
  const error = new Error(message);
  error.name = 'TimeoutError';
  return error;
}

test('a sign-in that finishes signs in once, says nothing and takes no more time', async () => {
  const times: number[] = [];
  const warnings: string[] = [];
  const lengthened: number[] = [];

  const result = await signInOnceMore({
    who: 'designer',
    attempt: async (timeoutMs) => {
      times.push(timeoutMs);
      return 'signed in';
    },
    warn: (message) => warnings.push(message),
    lengthen: (ms) => lengthened.push(ms),
  });

  assert.equal(result, 'signed in');
  assert.deepEqual(times, [SIGN_IN_STALL_MS]);
  assert.deepEqual(warnings, []);
  assert.deepEqual(lengthened, []);
});

test('a stalled sign-in gives the test its time back and is signed in once more, after its page is opened again, and the run says so', async () => {
  const steps: string[] = [];
  const warnings: string[] = [];

  const result = await signInOnceMore({
    who: 'e2e-designer@jfb.test',
    attempt: async (timeoutMs) => {
      steps.push(`attempt ${timeoutMs}`);
      if (steps.length === 1) throw timeout();
      return 'signed in';
    },
    again: async () => {
      steps.push('again');
    },
    warn: (message) => warnings.push(message),
    lengthen: (ms) => steps.push(`lengthen ${ms}`),
  });

  assert.equal(result, 'signed in');
  assert.deepEqual(steps, [
    `attempt ${SIGN_IN_STALL_MS}`,
    `lengthen ${SIGN_IN_STALL_MS}`,
    'again',
    `attempt ${SECOND_SIGN_IN_MS}`,
  ]);
  assert.equal(warnings.length, 1);
  assert.match(warnings[0] ?? '', /e2e-designer@jfb\.test did not finish within 20 s.*ADR-0250/);
});

test('a second stall fails the sign-in, with the time given back once', async () => {
  let attempts = 0;
  const lengthened: number[] = [];
  await assert.rejects(
    signInOnceMore({
      who: 'designer',
      attempt: async () => {
        attempts++;
        throw timeout();
      },
      warn: () => {},
      lengthen: (ms) => lengthened.push(ms),
    }),
    /Timeout 20000ms exceeded/,
  );
  assert.equal(attempts, 2);
  assert.deepEqual(lengthened, [SIGN_IN_STALL_MS]);
});

test('any other failure fails at once, with no second sign-in', async () => {
  let attempts = 0;
  await assert.rejects(
    signInOnceMore({
      who: 'designer',
      attempt: async () => {
        attempts++;
        throw new Error('The API login of designer failed with HTTP 400.');
      },
      warn: () => assert.fail('no stall to report'),
      lengthen: () => assert.fail('no stall to give time back for'),
    }),
    /HTTP 400/,
  );
  assert.equal(attempts, 1);
});

test("a timeout is Playwright's, by its name or by a request's message", () => {
  assert.equal(isTimeout(timeout()), true);
  assert.equal(isTimeout(new Error('apiRequestContext.post: Timeout 20000ms exceeded.')), true);
  assert.equal(isTimeout(new Error('apiRequestContext.post: Request context disposed.')), false);
  assert.equal(isTimeout('Timeout'), false);
});

test('outside a test, as in the global set-up, giving time back does nothing', () => {
  assert.doesNotThrow(() => lengthenTest(SIGN_IN_STALL_MS));
});
