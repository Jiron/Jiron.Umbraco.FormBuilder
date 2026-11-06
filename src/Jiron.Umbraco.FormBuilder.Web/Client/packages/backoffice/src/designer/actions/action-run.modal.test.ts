// The Details of an action's last run (ROADMAP 10.2f, ADR-0195): the line again, what happened, the tries and what to do,
// each under its heading; Test for who may change actions, busy once it runs until it answers; who may retry the run said
// either way.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { all, deferred, must, registerEnglish, settle, shadow, text } from '../../testing';
import { ActionRunModalElement } from './action-run.modal.element';
import type { ActionRunModalData } from './action-run.modal.token';
import { loadActionWords } from './action-words';

const DETAILS: ActionRunModalData['details'] = {
  line: { text: 'Failed at 14:02. Next try at 14:30.', tone: 'danger', details: true },
  happened: 'The server answered 503.',
  tries: 'Tried 2 times, the first at 13:40.',
};

async function detailsOf(data: Partial<ActionRunModalData> = {}) {
  const element = new ActionRunModalElement();
  const reject = vi.fn();
  element.modalContext = {
    value: new UmbObjectState({}).asObservable(),
    getValue: () => ({}),
    setValue: () => undefined,
    updateValue: () => undefined,
    submit: vi.fn(),
    reject,
  } as never;
  element.data = { name: 'Send to the CRM', details: DETAILS, webhook: true, ...data };
  document.body.append(element);
  await settle(element);
  return { element, root: shadow(element), reject };
}

beforeAll(async () => {
  registerEnglish();
  await loadActionWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe("a last run's Details", () => {
  it('say the line, what happened, the tries and what to do, each under its heading, never the entry', async () => {
    const { element, root } = await detailsOf({ test: async () => undefined });
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Send to the CRM');
    expect(
      all(element, '.part').map((part) => [
        text(part.querySelector('h4')),
        text(part)
          .replace(text(part.querySelector('h4')), '')
          .trim(),
      ]),
    ).toEqual([
      ['Last run', 'Failed at 14:02. Next try at 14:30.'],
      ['What happened', 'The server answered 503.'],
      ['Tries', 'Tried 2 times, the first at 13:40.'],
      [
        'What can be done',
        "Test sends a sample entry, so you can see whether the server answers again. Someone who sees this form's entries and may change its actions finds the entry in Entries with the Actions filter, and can retry the run there. An administrator can also retry it in Activity \u203a Deliveries.",
      ],
    ]);
    expect(root.querySelector('.line')?.getAttribute('data-tone')).toBe('danger');
    // Headings under the sidebar's own, and no landmarks of their own: a short sidebar read top to bottom.
    expect(all(element, 'h4')).toHaveLength(4);
    expect(all(element, 'section, [role="region"]')).toHaveLength(0);
  });

  it("say another type's Test in its own words, and only who may retry where there is no Test", async () => {
    const own = await detailsOf({ webhook: false, test: async () => undefined });
    expect(text(all(own.element, '.part')[3])).toContain(
      'Test runs the action with a sample entry, so you can see whether it works now.',
    );
    document.body.replaceChildren();
    const reader = await detailsOf();
    expect(text(all(reader.element, '.part')[3])).toBe(
      "What can be done Someone who sees this form's entries and may change its actions finds the entry in Entries with the Actions filter, and can retry the run there. An administrator can also retry it in Activity \u203a Deliveries.",
    );
    expect(reader.root.querySelector('uui-button[slot="footer-info"]')).toBeNull();
  });

  it('test the action from its footer, busy once it runs and saying so, a second press waiting for the first', async () => {
    const asked = deferred<undefined>();
    const answer = deferred<undefined>();
    const test = vi.fn(async (started: () => void) => {
      await asked.promise;
      started();
      await answer.promise;
    });
    const { element, root, reject } = await detailsOf({ test });
    const button = must(root.querySelector<HTMLElement>('uui-button[slot="footer-info"]'), 'Test');
    expect(button.getAttribute('label')).toBe('Test');
    button.click();
    button.click();
    await settle(element);
    expect(test).toHaveBeenCalledTimes(1);
    // While a webhook's test asks first, nothing is busy: a No runs nothing.
    expect((button as unknown as { state?: string }).state).toBeUndefined();
    expect(text(root.querySelector('[role="status"]'))).toBe('');
    asked.resolve(undefined);
    await settle(element);
    await settle(element);
    expect((button as unknown as { state?: string }).state).toBe('waiting');
    expect(text(root.querySelector('[role="status"]'))).not.toBe('');
    answer.resolve(undefined);
    await settle(element);
    await settle(element);
    expect((button as unknown as { state?: string }).state).toBeUndefined();
    // Dispatched: the test DOM drops a second synthetic click() here; the journey presses Close after Test in Edge.
    must(root.querySelector<HTMLElement>('uui-button[slot="actions"]'), 'Close').dispatchEvent(new Event('click'));
    expect(reject).toHaveBeenCalled();
  });

  it('stay quiet after a test that was declined, and test again on the next press', async () => {
    const test = vi.fn(async () => undefined);
    const { element, root } = await detailsOf({ test });
    const button = must(root.querySelector<HTMLElement>('uui-button[slot="footer-info"]'), 'Test');
    button.click();
    await settle(element);
    await settle(element);
    expect((button as unknown as { state?: string }).state).toBeUndefined();
    expect(text(root.querySelector('[role="status"]'))).toBe('');
    button.dispatchEvent(new Event('click'));
    await settle(element);
    expect(test).toHaveBeenCalledTimes(2);
  });
});
