// When the designer asks the server to check the draft (ROADMAP 9.5f, ADR-0181): at once when the form is read, after
// a change once the changes pause, once for the same form, the last ask's answer only, and the last answer shown while
// the next is awaited.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { deferred } from '../../testing';
import type { DesignDefinition } from '../definition';
import type { CheckIssue } from './form-check';
import { CHECK_PAUSE, FormChecker, type CheckState } from './form-checker';

const form = (key: string): DesignDefinition => ({ pages: [{ key, groups: [] }] });

const ISSUE: CheckIssue = { severity: 'error', code: 'fields_missing', path: '$.pages[0].groups', message: 'M' };

function checker() {
  const asked: {
    definition: DesignDefinition;
    answer: ReturnType<typeof deferred<readonly CheckIssue[] | undefined>>;
  }[] = [];
  const shown: CheckState[] = [];
  const subject = new FormChecker(
    (definition) => {
      const answer = deferred<readonly CheckIssue[] | undefined>();
      asked.push({ definition, answer });
      return answer.promise;
    },
    (state) => shown.push(state),
  );
  return { subject, asked, shown };
}

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe('the form check', () => {
  it('asks at once about the form read, and shows its answer', async () => {
    const { subject, asked, shown } = checker();
    const read = form('a');
    subject.changed(read);
    await vi.advanceTimersByTimeAsync(0);
    expect(asked.map((one) => one.definition)).toEqual([read]);
    expect(shown.at(-1)).toEqual({ status: 'checking', issues: [], definition: null });
    asked[0]?.answer.resolve([ISSUE]);
    await vi.advanceTimersByTimeAsync(0);
    expect(subject.state).toEqual({ status: 'checked', issues: [ISSUE], definition: read });
  });

  it('asks once after a run of changes, once they pause, keeping the last answer meanwhile', async () => {
    const { subject, asked } = checker();
    subject.changed(form('a'));
    await vi.advanceTimersByTimeAsync(0);
    asked[0]?.answer.resolve([ISSUE]);
    await vi.advanceTimersByTimeAsync(0);
    const typed = ['b', 'bo', 'box'].map(form);
    for (const each of typed) {
      subject.changed(each);
      await vi.advanceTimersByTimeAsync(CHECK_PAUSE - 100);
    }
    expect(asked).toHaveLength(1);
    await vi.advanceTimersByTimeAsync(100);
    expect(asked.map((one) => one.definition)).toEqual([form('a'), typed[2]]);
    expect(subject.state).toEqual({ status: 'checking', issues: [ISSUE], definition: form('a') });
  });

  it('asks nothing when a change is undone within the pause, the answer about the form back holding', async () => {
    const { subject, asked } = checker();
    const checkedForm = form('a');
    subject.changed(checkedForm);
    await vi.advanceTimersByTimeAsync(0);
    asked[0]?.answer.resolve([]);
    await vi.advanceTimersByTimeAsync(0);
    subject.changed(form('b'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE - 100);
    // Undo gives the form back as it was, the same object.
    subject.changed(checkedForm);
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    expect(asked).toHaveLength(1);
    expect(subject.state).toEqual({ status: 'checked', issues: [], definition: checkedForm });
  });

  it('asks again about a form an undo gives back when its check gave no answer', async () => {
    const { subject, asked } = checker();
    const unanswered = form('a');
    subject.changed(unanswered);
    await vi.advanceTimersByTimeAsync(0);
    asked[0]?.answer.resolve(undefined);
    await vi.advanceTimersByTimeAsync(0);
    subject.changed(form('b'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE - 100);
    subject.changed(unanswered);
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    expect(asked.map((one) => one.definition)).toEqual([unanswered, unanswered]);
  });

  it('keeps counting the pause when the same form comes again, as a change of the selection gives it', async () => {
    const { subject, asked } = checker();
    subject.changed(form('a'));
    await vi.advanceTimersByTimeAsync(0);
    asked[0]?.answer.resolve([]);
    await vi.advanceTimersByTimeAsync(0);
    const typed = form('b');
    subject.changed(typed);
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE - 100);
    subject.changed(typed);
    await vi.advanceTimersByTimeAsync(100);
    expect(asked.map((one) => one.definition)).toEqual([form('a'), typed]);
  });

  it("shows only the last ask's answer when an earlier one comes later", async () => {
    const { subject, asked } = checker();
    const first = form('a');
    const second = form('b');
    void subject.now(first);
    const done = subject.now(second);
    asked[1]?.answer.resolve([]);
    await done;
    asked[0]?.answer.resolve([ISSUE]);
    await vi.advanceTimersByTimeAsync(0);
    expect(subject.state).toEqual({ status: 'checked', issues: [], definition: second });
  });

  it('asks nothing again about the same form, as a save gives it back, and settles with the answer it awaits', async () => {
    const { subject, asked } = checker();
    const first = subject.now(form('a'));
    const again = subject.now(form('a'));
    expect(asked).toHaveLength(1);
    asked[0]?.answer.resolve([ISSUE]);
    await again;
    await first;
    expect(subject.state.status).toBe('checked');
    subject.changed(form('a'));
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    expect(asked).toHaveLength(1);
  });

  it('says when the check gave no answer, and asks again about the same form after that', async () => {
    const { subject, asked } = checker();
    const done = subject.now(form('a'));
    asked[0]?.answer.resolve(undefined);
    await done;
    expect(subject.state).toEqual({ status: 'failed', issues: [], definition: null });
    void subject.now(form('a'));
    expect(asked).toHaveLength(2);
  });

  it('takes an ask that fails as no answer', async () => {
    const subject = new FormChecker(
      () => Promise.reject(new Error('offline')),
      () => undefined,
    );
    await subject.now(form('a'));
    expect(subject.state.status).toBe('failed');
  });

  it('forgets every answer when another form is read, and drops an answer awaited after a stop', async () => {
    const { subject, asked } = checker();
    const done = subject.now(form('a'));
    asked[0]?.answer.resolve([ISSUE]);
    await done;
    subject.reset();
    expect(subject.state).toEqual({ status: 'idle', issues: [], definition: null });
    subject.changed(form('a'));
    await vi.advanceTimersByTimeAsync(0);
    expect(asked).toHaveLength(2);
    subject.stop();
    asked[1]?.answer.resolve([]);
    await vi.advanceTimersByTimeAsync(0);
    expect(subject.state.status).toBe('checking');
    subject.changed(form('b'));
    subject.stop();
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    expect(asked).toHaveLength(2);
  });
});
