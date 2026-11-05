// An action's sidebar (ROADMAP 10.2a, ADR-0188): a new action starts ready to email the team, another choice of what
// happens replaces the settings and the name until one is typed, and an action of the form keeps its type; each trigger
// says when it runs or the setting it needs; the type's settings as the inspector draws them, Essentials then More
// options, written back as the server keeps them; a value it can't keep refused at its box; the form check's problems in
// the action under their box, at once for those it had, others once left, Add or Update kept back by an error but not by
// a check that gives no answer, and kept once for two presses; the rule in the rules builder as a `run` rule; Fix it and
// Move to another trigger...; read-only, every setting as it is with Close only; and Delete action.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ActionTypeResponseModel } from '../../api';
import { must, registerEnglish, shadow, text } from '../../testing';
import type { CheckIssue } from '../check/form-check';
import { CHECK_PAUSE } from '../check/form-checker';
import type { DesignDefinition, DesignRule } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import { actionsOf, choicesOf, type DesignAction } from './action-model';
import { loadActionWords } from './action-words';
import type { ActionModalData, ActionModalValue } from './action.modal.token';

/** The built-in action types, as the snapshot holds what the server serves. */
const ACTION_TYPES = JSON.parse(
  readFileSync(
    join(__dirname, '..', '..', '..', '..', '..', '..', '..', '..', 'docs', 'api', 'action-types.json'),
    'utf8',
  ),
) as ActionTypeResponseModel[];

// The rules builder's answer, and what it was opened with.
const builder = vi.hoisted(() => ({ answer: undefined as unknown, data: [] as unknown[] }));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: { closed?: () => void } }) => {
    builder.data.push(args?.data);
    args?.data?.closed?.();
    return builder.answer;
  },
}));

const { ActionModalElement } = await import('./action.modal.element');

/** Lets the sidebar's renders and asks settle, the clock moved on by nothing: its timers are the test's. */
async function settle(element?: { updateComplete: Promise<unknown> }) {
  for (let round = 0; round < 5; round++) {
    await vi.advanceTimersByTimeAsync(0);
    await element?.updateComplete;
  }
}

const frame = () => new Promise((done) => requestAnimationFrame(() => done(null)));

const action = (key: string, more: Partial<DesignAction> = {}): DesignAction => ({
  key,
  type: 'email',
  name: `Action ${key}`,
  enabled: true,
  trigger: 'submitted',
  stopOnFailure: false,
  ...more,
});

const definitionWith = (actions: readonly DesignAction[]): DesignDefinition => ({
  pages: [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            { key: 'mail', type: 'email', alias: 'email', label: { '': 'Email address' } },
            { key: 'topic', type: 'text', alias: 'topic', label: { '': 'Topic' } },
          ],
        },
      ],
    },
  ],
  settings: { submission: { approval: 'automatic' } },
  ...(actions.length > 0 ? { actions } : {}),
});

const TEAM = action('a1', { settings: { to: ['sales@example.com'] } });

const RULE: DesignRule = {
  key: 'r1',
  effect: 'run',
  enabled: true,
  match: 'all',
  conditions: [{ field: 'topic', op: 'equals', value: 'Sales' }],
};

/** What the check answers: each ask's definition, and the problems it finds in it; undefined for no answer. */
const checking = {
  asked: [] as DesignDefinition[],
  answer: (() => []) as (definition: DesignDefinition) => CheckIssue[] | undefined,
};

function modalContext() {
  const state = new UmbObjectState<Partial<ActionModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: ActionModalValue) => state.setValue(next),
    updateValue: (partial: Partial<ActionModalValue>) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function sidebarOf(extra: Partial<ActionModalData> = {}) {
  const data: ActionModalData = {
    action: TEAM,
    definition: definitionWith(extra.action === null ? [] : [extra.action ?? TEAM]),
    form: 'form1',
    culture: 'en-US',
    languages: [{ culture: 'en-US', name: 'English', isDefault: true }],
    fieldTypes: builtInTypes,
    actionTypes: ACTION_TYPES,
    choices: choicesOf(ACTION_TYPES),
    needs: { submitted: null, approved: null, rejected: 'approval', verified: 'verification' },
    readOnly: false,
    check: async (definition) => {
      checking.asked.push(definition);
      return checking.answer(definition);
    },
    ...extra,
  };
  const modal = modalContext();
  const element = new ActionModalElement();
  element.modalContext = modal as never;
  element.data = data;
  document.body.append(element);
  await settle(element);
  // The first ask goes at once.
  await vi.advanceTimersByTimeAsync(0);
  await settle(element);
  const root = shadow(element);
  const box = <E extends HTMLElement = HTMLInputElement>(id: string) =>
    must(root.getElementById(id) as E | null, `#${id}`);
  const choose = async (name: string, value: string) => {
    const radio = must(root.querySelector<HTMLInputElement>(`input[name="${name}"][value="${value}"]`), value);
    radio.checked = true;
    radio.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const type = async (id: string, value: string, leave = true) => {
    const input = box<HTMLInputElement | HTMLTextAreaElement>(id);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    if (leave) input.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const pause = async () => {
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    await settle(element);
  };
  const keep = async () => {
    must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Add or Update').click();
    await vi.advanceTimersByTimeAsync(0);
    await settle(element);
  };
  const kept = () => (modal.getValue() as { action: DesignAction }).action;
  const why = (id: string) => [...root.querySelectorAll(`[id^="${id}-why-"]`)].map((one) => text(one));
  const found = (id: string) => [...root.querySelectorAll(`[id^="${id}-found-"]`)].map((one) => text(one));
  return { element, modal, root, box, choose, type, pause, keep, kept, why, found };
}

const problem = (path: string, code: string, message: string, severity: CheckIssue['severity'] = 'error') => ({
  severity,
  code,
  path,
  message,
});

beforeAll(async () => {
  registerEnglish();
  await loadActionWords('en');
});

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
  checking.asked = [];
  checking.answer = () => [];
  builder.answer = undefined;
  builder.data = [];
});

afterEach(() => {
  document.body.replaceChildren();
  vi.useRealTimers();
});

describe("an action's sidebar", () => {
  it('opens a new action ready to email the team, its name following the choice of what happens until it is typed', async () => {
    const { root, box, choose, type, keep, kept, modal } = await sidebarOf({ action: null });
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('New action');
    const choices = [...root.querySelectorAll('#what-happens .choice-name')].map((one) => text(one));
    expect(choices).toEqual([
      'Email the team',
      'Confirm to the visitor',
      'Webhook',
      'Slack',
      'Microsoft Teams',
      "Change the entry's state",
      'Create or update content',
    ]);
    expect(root.querySelector<HTMLInputElement>('input[name="what-happens"]:checked')?.value).toBe('email:0');
    expect([box('name').value, box('setting-subject').value]).toEqual([
      'Email the team',
      'New entry in {{ form.name }}',
    ]);
    // Another choice replaces the settings below it, and the name with them.
    await choose('what-happens', 'email:1');
    expect([box('name').value, box('setting-subject').value]).toEqual([
      'Confirm to the visitor',
      'We have your answers',
    ]);
    // A name typed stays; another type draws its own settings.
    await type('name', 'Tell the CRM');
    await choose('what-happens', 'webhook:0');
    expect([box('name').value, root.getElementById('setting-subject'), box('setting-url').localName]).toEqual([
      'Tell the CRM',
      null,
      'input',
    ]);
    // A new action has nothing to delete.
    expect(root.querySelector('uui-button[slot="footer-info"]')).toBeNull();
    expect(root.querySelector('uui-button[look="primary"]')?.getAttribute('label')).toBe('Add');
    await keep();
    expect(kept()).toEqual({
      key: kept().key,
      type: 'webhook',
      name: 'Tell the CRM',
      enabled: true,
      trigger: 'submitted',
      stopOnFailure: false,
    });
    expect(modal.submit).toHaveBeenCalled();
  });

  it('says that an action of the form keeps what it does, and updates it', async () => {
    const { root, keep, kept } = await sidebarOf();
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Action a1');
    expect(root.querySelector('input[name="what-happens"]')).toBeNull();
    expect(text(root.querySelector('#what-happens'))).toBe('Email');
    expect(text(root.querySelector('#what-happens + .help'))).toBe(
      'An action keeps what it does. To do something else, add another action.',
    );
    expect(root.querySelector('uui-button[look="primary"]')?.getAttribute('label')).toBe('Update');
    await keep();
    expect(kept()).toEqual(TEAM);
  });

  it('says when each trigger runs, or the setting it needs, and starts a new action on Submitted', async () => {
    const { root, choose, keep, kept } = await sidebarOf({ action: null });
    expect(root.querySelector<HTMLInputElement>('input[name="trigger"]:checked')?.value).toBe('submitted');
    expect([...root.querySelectorAll('#trigger .choice-line')].map((one) => text(one))).toEqual([
      'when an entry comes in',
      'as each entry comes in, because this form has no manual approval',
      'It runs once the form has manual approval.',
      'It runs once the form has email verification.',
    ]);
    // Each choice's line describes it.
    expect(root.querySelector('input[name="trigger"][value="rejected"]')?.getAttribute('aria-describedby')).toBe(
      'trigger-needs-rejected',
    );
    await choose('trigger', 'approved');
    await keep();
    expect(kept().trigger).toBe('approved');
    // With manual approval, Approved runs when someone approves.
    document.body.replaceChildren();
    const approving = await sidebarOf({
      needs: { submitted: null, approved: null, rejected: null, verified: null },
    });
    expect(text(approving.root.querySelector('#trigger-needs-approved'))).toBe('when someone approves an entry');
  });

  it("draws the type's settings, Essentials first and the rest under More options, and writes them back as the server keeps them", async () => {
    const { root, box, type, keep, kept } = await sidebarOf();
    const drawn = () =>
      [...root.querySelectorAll('.settings [id^="setting-"]')]
        .map((one) => one.id)
        .filter((id) => !/-(lines|label)$/.test(id));
    // The settings whose editors come with 10.2b (a field's address, recipients by a choice) are not drawn yet.
    expect(drawn()).toEqual([
      'setting-to',
      'setting-toCreator',
      'setting-cc',
      'setting-bcc',
      'setting-subject',
      'setting-body',
      'setting-withoutUnanswered',
    ]);
    expect(box('setting-to').value).toBe('sales@example.com');
    const more = must(root.querySelector<HTMLButtonElement>('.more'), 'More options');
    expect([text(more), more.getAttribute('aria-expanded')]).toEqual(['More options (3)', 'false']);
    more.click();
    await settle();
    expect(drawn().slice(7)).toEqual(['setting-plainText', 'setting-from', 'setting-view']);
    expect(text(root.querySelector('.more'))).toBe('Fewer options');
    await type('setting-to', 'sales@example.com\nlead@example.com');
    await type('setting-from', 'forms@example.com');
    must(root.querySelector<HTMLInputElement>('#stop-on-failure'), 'Stop the rest').click();
    await settle();
    await keep();
    expect(kept()).toEqual({
      ...TEAM,
      stopOnFailure: true,
      settings: { to: ['sales@example.com', 'lead@example.com'], from: 'forms@example.com' },
    });
  });

  it("refuses at its box what the server can't keep, once the box is left, and keeps back Update with the focus there", async () => {
    const hook = action('a1', { type: 'webhook', name: 'CRM', settings: { url: 'https://crm.example.com' } });
    const { element, root, box, type, keep, why, modal } = await sidebarOf({ action: hook });
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    await type('name', 'x'.repeat(201), false);
    expect(why('name')).toEqual([]);
    box('name').dispatchEvent(new Event('change'));
    await settle(element);
    expect(why('name')).toEqual(['Use 200 characters at most.']);
    expect(box('name').getAttribute('aria-invalid')).toBe('true');
    await type('setting-url', `https://${'x'.repeat(2000)}`);
    expect(text(root.getElementById('setting-url-why'))).toBe('Use 2000 characters at most.');
    await keep();
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'name']);
    // Once the name can be kept, the address is the first box that says why.
    await type('name', 'CRM');
    expect(why('name')).toEqual([]);
    await keep();
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'setting-url']);
  });

  it("shows the check's problems in the action under their box, at once for those it had, others once left", async () => {
    const hook = action('a1', { type: 'webhook', name: 'CRM', settings: { url: 'ftp://crm.example.com' } });
    checking.answer = (definition) => {
      const actions = actionsOf(definition);
      const at = actions.findIndex((one) => one.key === 'a1');
      const url = String(actions[at]?.settings?.['url'] ?? '');
      const name = actions[at]?.name;
      return [
        // Another action's problem is not this one's.
        problem('$.actions[0].name', 'other', 'Not this one.'),
        ...(url.startsWith('ftp')
          ? [problem(`$.actions[${at}].settings.url`, 'invalid_url', 'Use a web address.')]
          : []),
        ...(url === 'https://slow.example'
          ? [problem(`$.actions[${at}].settings.url`, 'slow', 'It answers slowly.', 'warning')]
          : []),
        ...(name === 'Same' ? [problem(`$.actions[${at}].name`, 'same_name', 'Another action has this name.')] : []),
      ];
    };
    const { element, root, box, type, pause, keep, kept, why, found, modal } = await sidebarOf({
      action: hook,
      definition: definitionWith([action('a0'), hook]),
      focus: { kind: 'setting', path: 'url' },
    });
    await frame();
    expect(root.activeElement?.id).toBe('setting-url');
    expect(found('setting-url')).toEqual(['Use a web address.']);
    expect(box('setting-url').getAttribute('aria-invalid')).toBe('true');
    expect(why('name')).toEqual([]);
    // Asked about the draft as Update would make it: the action in its own place.
    expect(actionsOf(must(checking.asked[0], 'the ask')).map((one) => one.key)).toEqual(['a0', 'a1']);
    // A problem found later shows once its box is left.
    await type('name', 'Same', false);
    await pause();
    expect(why('name')).toEqual([]);
    box('name').dispatchEvent(new Event('change'));
    await settle(element);
    expect(why('name')).toEqual(['Another action has this name.']);
    await type('name', 'CRM');
    await pause();
    expect(why('name')).toEqual([]);
    // An error keeps Update back; a warning doesn't.
    await keep();
    expect(modal.submit).not.toHaveBeenCalled();
    await type('setting-url', 'https://slow.example');
    await pause();
    expect(found('setting-url')).toEqual(['Warning: It answers slowly.']);
    await keep();
    expect(kept().settings).toEqual({ url: 'https://slow.example' });
  });

  it('opens More options for a problem the check found there, and for a Fix it there', async () => {
    checking.answer = () => [problem('$.actions[0].settings.view', 'missing_view', 'No such view.')];
    const flagged = await sidebarOf();
    expect(flagged.found('setting-view')).toEqual(['No such view.']);
    // The problem is said where it is: no button hides it.
    expect(flagged.root.querySelector('.more')).toBeNull();
    document.body.replaceChildren();
    checking.answer = () => [];
    const fixing = await sidebarOf({ focus: { kind: 'setting', path: 'from' } });
    await frame();
    expect(fixing.root.activeElement?.id).toBe('setting-from');
    expect(fixing.root.querySelector('.more')?.getAttribute('aria-expanded')).toBe('true');
  });

  it('puts the focus on the trigger chosen for Move to another trigger\u2026', async () => {
    const { root } = await sidebarOf({ action: action('a1', { trigger: 'approved' }), focus: { kind: 'trigger' } });
    await frame();
    expect([root.activeElement?.getAttribute('name'), (root.activeElement as HTMLInputElement).value]).toEqual([
      'trigger',
      'approved',
    ]);
  });

  it("chooses its place among its trigger's actions without dragging, and goes after another trigger's", async () => {
    const second = action('a2', { name: 'Second' });
    const { element, root, choose, pause, keep, modal } = await sidebarOf({
      action: second,
      definition: definitionWith([TEAM, action('x', { trigger: 'approved' }), second]),
    });
    const place = () => must(root.querySelector<HTMLSelectElement>('#place'), 'the place');
    expect([[...place().options].map((one) => text(one)), place().value]).toEqual([['1 of 2', '2 of 2'], '1']);
    place().value = '0';
    place().dispatchEvent(new Event('change'));
    await settle(element);
    await pause();
    // Asked about the draft as Update would make it: the action at its new place.
    expect(actionsOf(must(checking.asked.at(-1), 'the ask')).map((one) => one.key)).toEqual(['a2', 'x', 'a1']);
    // Another trigger: after that trigger's actions until a place is chosen there.
    await choose('trigger', 'approved');
    expect([[...place().options].map((one) => text(one)), place().value]).toEqual([['1 of 2', '2 of 2'], '1']);
    await keep();
    expect(modal.getValue()).toEqual({ action: { ...second, trigger: 'approved' }, at: null });
    // A new action goes after the others; an action alone in its trigger has no other place.
    document.body.replaceChildren();
    const fresh = await sidebarOf({ action: null, definition: definitionWith([TEAM]) });
    expect(must(fresh.root.querySelector<HTMLSelectElement>('#place'), 'the place').value).toBe('1');
    document.body.replaceChildren();
    const alone = await sidebarOf();
    expect(alone.root.querySelector('#place')).toBeNull();
  });

  it('holds nothing back with a value typed in a setting that another choice of what happens took away', async () => {
    const { choose, type, keep, modal, root } = await sidebarOf({ action: null });
    await choose('what-happens', 'webhook:0');
    await type('setting-url', `https://${'x'.repeat(2000)}`);
    expect(root.getElementById('setting-url-why')).not.toBeNull();
    await choose('what-happens', 'email:0');
    await type('setting-to', 'sales@example.com');
    await keep();
    expect(modal.submit).toHaveBeenCalled();
    // What was typed went with the settings: the webhook's address is fresh again.
    await choose('what-happens', 'webhook:0');
    expect(root.getElementById('setting-url-why')).toBeNull();
  });

  it('keeps More options open over a value typed there that cannot be kept', async () => {
    const { root, type } = await sidebarOf();
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle();
    await type('setting-from', 'x'.repeat(2001));
    expect(root.getElementById('setting-from-why')).not.toBeNull();
    // No button to close them over it: the problem stays in sight.
    expect(root.querySelector('.more')).toBeNull();
  });

  it("says a problem of a setting without its editor yet under what happens, by the setting's name, and leads there", async () => {
    checking.answer = () => [
      problem('$.actions[0].settings.toField', 'setting_invalid', 'Choose an email field of this form.'),
    ];
    const visitor = action('a1', { settings: { toField: 'gone' } });
    const { root, keep, why, modal } = await sidebarOf({ action: visitor });
    expect(why('what-happens')).toEqual(['To the address in a field: Choose an email field of this form.']);
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    await keep();
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'what-happens']);
    // Its Fix it leads there too.
    document.body.replaceChildren();
    const fixing = await sidebarOf({ action: visitor, focus: { kind: 'setting', path: 'toField' } });
    await frame();
    expect(fixing.root.activeElement?.id).toBe('what-happens');
  });

  it("shows a new action's problems once their box is left or Add is pressed, not as it opens", async () => {
    checking.answer = () => [problem('$.actions[0].settings.to', 'setting_invalid', 'A mail needs someone to go to.')];
    const { found, keep, modal } = await sidebarOf({ action: null });
    expect(found('setting-to')).toEqual([]);
    await keep();
    expect([found('setting-to'), modal.submit.mock.calls.length]).toEqual([['A mail needs someone to go to.'], 0]);
  });

  it('keeps an action when the check gives no answer, and once for two presses', async () => {
    checking.answer = () => undefined;
    const { element, root, modal } = await sidebarOf();
    const update = must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Update');
    update.click();
    update.click();
    await vi.advanceTimersByTimeAsync(0);
    await settle(element);
    expect(modal.submit).toHaveBeenCalledTimes(1);
  });

  it('builds its rule in the rules builder as one that runs it, named for the action', async () => {
    builder.answer = { target: 'a1', rule: RULE };
    const { root, keep, kept } = await sidebarOf();
    const add = must(root.querySelector<HTMLElement>('#rule'), 'Add a rule');
    expect([text(add), text(root.querySelector('#rule-words'))]).toEqual([
      'Add a rule',
      'Runs for every entry its trigger brings.',
    ]);
    expect(add.getAttribute('aria-describedby')).toBe('rule-words');
    add.click();
    await vi.advanceTimersByTimeAsync(0);
    await settle();
    expect(builder.data[0]).toMatchObject({
      origin: 'a1',
      action: { name: 'Action a1' },
      effect: 'run',
      rule: null,
      form: 'form1',
    });
    expect([text(root.querySelector('#rule-words')), text(root.querySelector('#rule'))]).toEqual([
      'Runs when Topic is "Sales"',
      'Change rule',
    ]);
    await keep();
    expect(kept().rules).toEqual([RULE]);
    // A new action is the new action in the builder.
    document.body.replaceChildren();
    builder.data = [];
    const fresh = await sidebarOf({ action: null });
    must(fresh.root.querySelector<HTMLElement>('#rule'), 'Add a rule').click();
    await vi.advanceTimersByTimeAsync(0);
    await settle();
    expect(builder.data[0]).toMatchObject({ action: { name: null }, effect: 'run' });
  });

  it('shows every setting as it is without manageActions, with Close only, and asks the check nothing', async () => {
    const team = action('a1', {
      settings: { to: ['sales@example.com'], subject: { '': 'New entry' } },
      rules: [{ ...RULE, enabled: false }],
    });
    const { root } = await sidebarOf({ action: team, readOnly: true });
    expect(root.querySelectorAll('input, textarea, select, .rule-button')).toHaveLength(0);
    expect(
      ['what-happens', 'name', 'trigger', 'setting-to', 'setting-cc', 'setting-subject', 'stop-on-failure', 'rule'].map(
        (id) => text(root.getElementById(id)),
      ),
    ).toEqual([
      'Email',
      'Action a1',
      'Submitted',
      'sales@example.com',
      'none',
      'New entry',
      'Off',
      'Runs when Topic is "Sales". The rule is off, so the action runs as if it had none.',
    ]);
    // It says nothing about keeping what it does: nothing here can be changed.
    expect(root.querySelector('#what-happens + .help')).toBeNull();
    // The same settings show as for who may change them.
    expect(root.getElementById('setting-toField')).toBeNull();
    expect(root.querySelectorAll('uui-button')).toHaveLength(1);
    expect(root.querySelector('uui-button[look="primary"]')).toBeNull();
    expect(checking.asked).toHaveLength(0);
  });

  it('deletes the action, and says once it is gone', async () => {
    const closed = vi.fn();
    const { element, root, modal } = await sidebarOf({ closed });
    must(root.querySelector<HTMLElement>('uui-button[slot="footer-info"]'), 'Delete action').click();
    await settle(element);
    expect(modal.getValue()).toEqual({ deleted: true });
    expect(modal.submit).toHaveBeenCalled();
    element.remove();
    expect(closed).toHaveBeenCalledTimes(1);
  });
});
