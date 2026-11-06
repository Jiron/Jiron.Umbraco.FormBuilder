// An action's sidebar (ROADMAP 10.2a, ADR-0188): a new action starts ready to email the team, another choice of what
// happens replaces the settings and the name until one is typed, and an action of the form keeps its type; each trigger
// says when it runs or the setting it needs; the type's settings as the inspector draws them, Essentials then More
// options, written back as the server keeps them; a value it can't keep refused at its box; the form check's problems in
// the action under their box, at once for those it had, others once left, Add or Update kept back by an error but not by
// a check that gives no answer, and kept once for two presses; the rule in the rules builder as a `run` rule; Fix it and
// Move to another trigger...; read-only, every setting as it is with Close only; and Delete action. An email (ADR-0189):
// its To box with every kind of recipient, each added and taken away again; Copy to's addresses; fields picked by
// kind; the layouts; Insert merge tag for mail; and its mail's preview, asked once the changes pause.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ActionTypeResponseModel } from '../../api';
import { must, registerEnglish, shadow, text } from '../../testing';
import type { CheckIssue } from '../check/form-check';
import { CHECK_PAUSE } from '../check/form-checker';
import type { DesignDefinition, DesignField, DesignRule } from '../definition';
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

/** A form with an email field, a choice of two options, a sensitive field and a file upload. */
const emailForm = (actions: readonly DesignAction[], more: readonly DesignField[] = []): DesignDefinition => ({
  ...definitionWith(actions),
  pages: [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            { key: 'mail', type: 'email', alias: 'email', label: { '': 'Email address' } },
            {
              key: 'topic',
              type: 'dropdown',
              alias: 'topic',
              label: { '': 'Topic' },
              options: {
                items: [
                  { value: 'sales', label: { '': 'Sales' } },
                  { value: 'press', label: { '': 'Press' } },
                ],
              },
            },
            { key: 'secret', type: 'text', alias: 'secret', label: { '': 'Secret' }, sensitive: true },
            { key: 'cv', type: 'file', alias: 'cv', label: { '': 'Your CV' } },
            ...more,
          ],
        },
      ],
    },
  ],
});

/** The mail the preview shows. */
const MAIL = {
  from: 'Example <forms@example.com>',
  to: ['sales@example.com'],
  cc: [],
  bcc: [],
  subject: 'New entry',
  html: '<!DOCTYPE html><html><head></head><body><p>Hi.</p></body></html>',
  text: 'Hi.',
  direction: 'ltr',
};

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
    const { root, type, keep, kept } = await sidebarOf();
    const drawn = () =>
      [...root.querySelectorAll('.settings [id^="setting-"]')]
        .map((one) => one.id)
        .filter((id) => !/-(lines|label|several|address|remove-\d+)$/.test(id));
    // The recipients within the To box (a field's answer, the form's creator, the map) are drawn by it, not on their own.
    // Who it goes to, what it says, then the reply-to and the copies; a blind copy under More options.
    expect(drawn()).toEqual([
      'setting-to',
      'setting-subject',
      'setting-body',
      'setting-withoutUnanswered',
      'setting-replyToField',
      'setting-cc',
    ]);
    expect([...root.querySelectorAll('.recipients .chip-text')].map((one) => text(one))).toEqual(['sales@example.com']);
    const more = must(root.querySelector<HTMLButtonElement>('.more'), 'More options');
    expect([text(more), more.getAttribute('aria-expanded')]).toEqual(['More options (7)', 'false']);
    more.click();
    await settle();
    expect(drawn().slice(6)).toEqual([
      'setting-plainText',
      'setting-layout',
      'setting-bcc',
      'setting-from',
      'setting-view',
      'setting-includeSensitive',
      'setting-attachments',
    ]);
    expect(text(root.querySelector('.more'))).toBe('Fewer options');
    await type('setting-to-address', 'lead@example.com');
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
    // Update hands the place back with the action.
    await keep();
    expect(modal.getValue()).toEqual({ action: second, at: 0 });
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
    checking.answer = () => [problem('$.actions[0].settings.headers', 'setting_invalid', 'Each header has a name.')];
    const hook = action('a1', {
      type: 'webhook',
      name: 'CRM',
      settings: { url: 'https://crm.example.com', headers: [{}] },
    });
    const { root, keep, why, modal } = await sidebarOf({ action: hook });
    expect(why('what-happens')).toEqual(['More headers: Each header has a name.']);
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    await keep();
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'what-happens']);
    // Its Fix it leads there too.
    document.body.replaceChildren();
    const fixing = await sidebarOf({ action: hook, focus: { kind: 'setting', path: 'headers' } });
    await frame();
    expect(fixing.root.activeElement?.id).toBe('what-happens');
  });

  it('says a problem of a recipient within the To box at its row, where its Fix it leads', async () => {
    checking.answer = () => [
      problem('$.actions[0].settings.toField', 'setting_invalid', 'Choose an email field of this form.'),
    ];
    const visitor = action('a1', { settings: { toField: 'gone' } });
    const { root, box, keep, found, why, modal } = await sidebarOf({ action: visitor });
    expect(why('what-happens')).toEqual([]);
    expect(found('setting-toField')).toEqual(['Choose an email field of this form.']);
    const field = box<HTMLSelectElement>('setting-toField');
    expect([field.value, text(field.selectedOptions[0]), field.getAttribute('aria-invalid')]).toEqual([
      'gone',
      'A field that is gone',
      'true',
    ]);
    expect(field.getAttribute('aria-describedby')).toBe('setting-toField-found-0');
    await frame();
    await keep();
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'setting-toField']);
    document.body.replaceChildren();
    const fixing = await sidebarOf({ action: visitor, focus: { kind: 'setting', path: 'toField' } });
    await frame();
    expect(fixing.root.activeElement?.id).toBe('setting-toField');
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

  it('tests the action as the sidebar holds it, saved or not, its button busy until it answers (ADR-0190)', async () => {
    let finish: () => void = () => undefined;
    const tests: [DesignDefinition, string][] = [];
    let start: () => void = () => undefined;
    const test = (definition: DesignDefinition, key: string, started: () => void) => {
      tests.push([definition, key]);
      start = started;
      return new Promise<void>((done) => (finish = done));
    };
    const { element, root, type } = await sidebarOf({ test });
    await type('name', 'Tell the team');
    const button = must(root.querySelector<HTMLElement>('uui-button.test'), 'Test');
    // Outlined, as on its card, unlike Delete action beside it; its title says what an email's test does.
    expect(['slot', 'label', 'look', 'title'].map((name) => button.getAttribute(name))).toEqual([
      'footer-info',
      'Test',
      'outline',
      'Sends a test to you alone.',
    ]);
    button.click();
    await settle(element);
    // Not busy while a question may come first: only once the test starts. A second press meanwhile waits for the
    // first (a busy button takes no press of its own).
    expect(button.hasAttribute('state')).toBe(false);
    button.click();
    expect(tests).toHaveLength(1);
    start();
    await settle(element);
    expect(button.getAttribute('state')).toBe('waiting');
    // Said in the sidebar's own live region: the view's is behind the modal.
    const status = () => text(root.querySelector('.visually-hidden[role="status"]'));
    expect(status()).toBe('Testing\u2026');
    button.click();
    expect(tests).toHaveLength(1);
    const [definition, key] = must(tests[0], 'the test');
    expect([key, actionsOf(definition).find((one) => one.key === key)?.name]).toEqual([TEAM.key, 'Tell the team']);
    finish();
    await settle(element);
    expect(button.hasAttribute('state')).toBe(false);
    expect(status()).toBe('');
    // A new action is tested before it is added.
    document.body.replaceChildren();
    const fresh = await sidebarOf({ action: null, test });
    must(fresh.root.querySelector<HTMLElement>('uui-button.test'), 'Test').click();
    await settle(fresh.element);
    const [added, newKey] = must(tests[1], "the new action's test");
    expect(actionsOf(added).some((one) => one.key === newKey)).toBe(true);
    finish();
  });

  it('offers no Test to who may only read the action, nor without a way to test', async () => {
    const reader = await sidebarOf({ readOnly: true, test: async () => undefined });
    expect(reader.root.querySelector('uui-button.test')).toBeNull();
    document.body.replaceChildren();
    const without = await sidebarOf();
    expect(without.root.querySelector('uui-button.test')).toBeNull();
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

describe("an email's sidebar", () => {
  const chips = (root: ShadowRoot, box: string) =>
    [...root.querySelectorAll(`#${box} ~ * .chip-text, .item:has(#${box}) .chip-text`)].map((one) => text(one));
  const toChips = (root: ShadowRoot) => [...root.querySelectorAll('.recipients .chip-text')].map((one) => text(one));
  const openAdd = (root: ShadowRoot) => {
    must(root.querySelector('details.add'), 'Add recipient').setAttribute('open', '');
  };
  const kinds = (root: ShadowRoot) => [...root.querySelectorAll('.add-kind')].map((one) => text(one));
  const press = (root: ShadowRoot, label: string) =>
    must(
      [...root.querySelectorAll<HTMLButtonElement>('button')].find(
        (one) => text(one) === label || one.getAttribute('aria-label') === label,
      ),
      label,
    ).click();

  it('builds the To box from every kind of recipient, as the server keeps them, each kind offered once', async () => {
    const { element, root, box, type, keep, kept } = await sidebarOf({ definition: emailForm([TEAM]) });
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    expect(toChips(root)).toEqual(['sales@example.com']);
    expect(text(root.querySelector('.recipients > legend'))).toBe('To');
    expect(text(root.getElementById('setting-to'))).toBe('Add recipient');
    openAdd(root);
    expect(kinds(root)).toEqual([
      'The answer of a field',
      'The person who created the form',
      'Addresses chosen by an answer',
    ]);
    // Several at once, apart by commas; one already there is kept once.
    await type('setting-to-address', 'lead@example.com, SALES@example.com ops@example.com');
    expect(toChips(root)).toEqual(['sales@example.com', 'lead@example.com', 'ops@example.com']);
    expect(box('setting-to-address').value).toBe('');
    press(root, 'The answer of a field');
    await settle(element);
    expect(root.querySelector('details.add')?.hasAttribute('open')).toBe(false);
    const field = box<HTMLSelectElement>('setting-toField');
    expect([field.value, [...field.options].map((option) => text(option))]).toEqual(['mail', ['Email address']]);
    expect(text(root.querySelector('label[for="setting-toField"]'))).toBe('The answer of');
    expect(root.activeElement?.id).toBe('setting-toField');
    openAdd(root);
    press(root, 'The person who created the form');
    await settle(element);
    expect(root.activeElement?.getAttribute('aria-label')).toBe('Remove the person who created the form');
    openAdd(root);
    press(root, 'Addresses chosen by an answer');
    await settle(element);
    const map = box<HTMLSelectElement>('setting-recipientMap');
    expect([map.value, [...map.options].map((option) => text(option))]).toEqual(['topic', ['Topic']]);
    expect(root.activeElement?.id).toBe('setting-recipientMap');
    expect([...root.querySelectorAll('.routes label')].map((one) => text(one))).toEqual(['Sales', 'Press']);
    await type('setting-recipientMap-route-1', 'press@example.com, pr@example.com');
    // Each kind is there once: Add recipient offers addresses alone now.
    openAdd(root);
    expect(kinds(root)).toEqual([]);
    await keep();
    expect(kept().settings).toEqual({
      to: ['sales@example.com', 'lead@example.com', 'ops@example.com'],
      toField: 'mail',
      toCreator: true,
      recipientMap: { field: 'topic', routes: [{ value: 'press', to: ['press@example.com', 'pr@example.com'] }] },
    });
  });

  it('keeps the addresses of a choice field while another is chosen, and brings them back with it', async () => {
    const routed = action('a1', {
      settings: { recipientMap: { field: 'topic', routes: [{ value: 'press', to: ['press@example.com'] }] } },
    });
    const region: DesignField = {
      key: 'region',
      type: 'dropdown',
      alias: 'region',
      label: { '': 'Region' },
      options: { items: [{ value: 'north', label: { '': 'North' } }] },
    };
    const { element, root, box, keep, kept } = await sidebarOf({
      action: routed,
      definition: emailForm([routed], [region]),
    });
    await frame();
    const choose = async (field: string) => {
      const map = box<HTMLSelectElement>('setting-recipientMap');
      map.value = field;
      map.dispatchEvent(new Event('change'));
      await settle(element);
    };
    await choose('region');
    expect([
      [...root.querySelectorAll('.routes label')].map((one) => text(one)),
      box('setting-recipientMap-route-0').value,
    ]).toEqual([['North'], '']);
    await choose('topic');
    expect(box('setting-recipientMap-route-1').value).toBe('press@example.com');
    await keep();
    expect(kept().settings?.['recipientMap']).toEqual({
      field: 'topic',
      routes: [{ value: 'press', to: ['press@example.com'] }],
    });
  });

  it("lets what was typed under an option's box go with its boxes, so Update keeps nothing back for it", async () => {
    const routed = action('a1', {
      settings: { to: ['sales@example.com'], recipientMap: { field: 'topic', routes: [] } },
    });
    const region: DesignField = {
      key: 'region',
      type: 'dropdown',
      alias: 'region',
      label: { '': 'Region' },
      options: { items: [{ value: 'north', label: { '': 'North' } }] },
    };
    const { element, root, box, type, keep, modal } = await sidebarOf({
      action: routed,
      definition: emailForm([routed], [region]),
    });
    await frame();
    await type('setting-recipientMap-route-0', 'sales');
    expect(root.getElementById('setting-recipientMap-route-0-why')).not.toBeNull();
    // Another field: its first option's box starts empty, nothing said under it.
    const map = box<HTMLSelectElement>('setting-recipientMap');
    map.value = 'region';
    map.dispatchEvent(new Event('change'));
    await settle(element);
    expect([
      box('setting-recipientMap-route-0').value,
      root.getElementById('setting-recipientMap-route-0-why'),
    ]).toEqual(['', null]);
    await type('setting-recipientMap-route-0', 'north');
    // The map goes with its boxes, and what was typed in them.
    press(root, 'Remove the addresses chosen by Region');
    await settle(element);
    await keep();
    expect(modal.submit).toHaveBeenCalled();
  });

  it('draws a map that is no map as one of a field that is gone, to be removed', async () => {
    const odd = action('a1', { settings: { to: ['sales@example.com'], recipientMap: { routes: [] } } });
    const { element, root, box, keep, kept } = await sidebarOf({ action: odd, definition: emailForm([odd]) });
    await frame();
    expect(text(box<HTMLSelectElement>('setting-recipientMap').selectedOptions[0])).toBe('A field that is gone');
    openAdd(root);
    expect(kinds(root)).not.toContain('Addresses chosen by an answer');
    press(root, 'Remove the addresses chosen by A field that is gone');
    await settle(element);
    await keep();
    expect(kept().settings).toEqual({ to: ['sales@example.com'] });
  });

  it('draws each kind of recipient on its own where a host locks the To box', async () => {
    const locked = ACTION_TYPES.map((type) =>
      type.alias !== 'email'
        ? type
        : {
            ...type,
            settings: type.settings.map((setting) =>
              setting.name === 'to'
                ? {
                    ...setting,
                    lock: { hidden: false, configurationKey: 'JironFormBuilder:Lock', value: ['ops@example.com'] },
                  }
                : setting,
            ),
          },
    );
    const routed = action('a1', { settings: { toField: 'mail', recipientMap: { field: 'topic', routes: [] } } });
    const { root, box } = await sidebarOf({
      action: routed,
      definition: emailForm([routed]),
      actionTypes: locked,
      choices: choicesOf(locked),
    });
    await frame();
    expect(root.querySelector('.recipients')).toBeNull();
    expect(root.querySelector('[data-locked="to"]')).not.toBeNull();
    expect([
      box<HTMLSelectElement>('setting-toField').value,
      box<HTMLSelectElement>('setting-recipientMap').value,
      box('setting-toCreator').localName,
    ]).toEqual(['mail', 'topic', 'input']);
  });

  it('takes each recipient away with its remove button, the focus on the next chip or on Add recipient', async () => {
    const everyone = action('a1', {
      settings: {
        to: ['a@example.com', 'b@example.com'],
        toField: 'mail',
        toCreator: true,
        recipientMap: { field: 'topic', routes: [{ value: 'sales', to: ['s@example.com'] }] },
      },
    });
    const { element, root, keep, kept } = await sidebarOf({ action: everyone, definition: emailForm([everyone]) });
    await frame();
    expect(must(root.getElementById('setting-recipientMap-route-0') as HTMLInputElement | null, 'Sales').value).toBe(
      's@example.com',
    );
    press(root, 'Remove a@example.com');
    await settle(element);
    expect([toChips(root), root.activeElement?.getAttribute('aria-label')]).toEqual([
      ['b@example.com'],
      'Remove b@example.com',
    ]);
    press(root, 'Remove the answer of Email address');
    await settle(element);
    expect([root.getElementById('setting-toField'), root.activeElement?.id]).toEqual([null, 'setting-to']);
    press(root, 'Remove the person who created the form');
    press(root, 'Remove the addresses chosen by Topic');
    await settle(element);
    expect(root.querySelectorAll('.recipient')).toHaveLength(0);
    await keep();
    expect(kept().settings).toEqual({ to: ['b@example.com'] });
  });

  it('refuses at the To box an address that is none, keeping what was typed until it is one', async () => {
    const { root, box, type, keep, modal } = await sidebarOf({ definition: emailForm([TEAM]) });
    openAdd(root);
    await type('setting-to-address', 'lead@example.com, sales');
    // Said under the box it was typed in, which it describes.
    expect(text(root.getElementById('setting-to-address-why'))).toBe(
      'Enter addresses such as hello@example.com: "sales" isn\'t one.',
    );
    const address = box('setting-to-address');
    expect([address.value, address.getAttribute('aria-invalid'), address.getAttribute('aria-describedby')]).toEqual([
      'lead@example.com, sales',
      'true',
      'setting-to-address-why setting-to-address-several',
    ]);
    expect(toChips(root)).toEqual(['sales@example.com']);
    await type('setting-to-address', 'lead@example.com');
    expect([root.getElementById('setting-to-address-why'), toChips(root)]).toEqual([
      null,
      ['sales@example.com', 'lead@example.com'],
    ]);
    await keep();
    expect(modal.submit).toHaveBeenCalled();
  });

  it('opens Add recipient to show what it keeps back, its Add named apart from the footer', async () => {
    const { root, type, keep, modal } = await sidebarOf({ definition: emailForm([TEAM]) });
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    openAdd(root);
    const add = must(root.querySelector('details.add uui-button'), 'Add address');
    expect([add.getAttribute('label'), text(add)]).toEqual(['Add address', 'Add']);
    await type('setting-to-address', 'sales');
    must(root.querySelector('details.add'), 'Add recipient').removeAttribute('open');
    await keep();
    expect(modal.submit).not.toHaveBeenCalled();
    expect([root.querySelector('details.add')?.hasAttribute('open'), root.activeElement?.id]).toEqual([
      true,
      'setting-to-address',
    ]);
  });

  it("says under an option's box why the addresses typed in it can't be kept", async () => {
    const routed = action('a1', {
      settings: { to: ['sales@example.com'], recipientMap: { field: 'topic', routes: [] } },
    });
    const { root, box, type } = await sidebarOf({ action: routed, definition: emailForm([routed]) });
    await frame();
    await type('setting-recipientMap-route-1', 'press@example.com, press');
    const press = box('setting-recipientMap-route-1');
    expect([
      text(root.getElementById('setting-recipientMap-route-1-why')),
      press.getAttribute('aria-invalid'),
      press.getAttribute('aria-describedby'),
    ]).toEqual([
      'Enter addresses such as hello@example.com: "press" isn\'t one.',
      'true',
      'setting-recipientMap-routes-help setting-recipientMap-route-1-why',
    ]);
    expect([
      root.getElementById('setting-recipientMap-route-0-why'),
      box('setting-recipientMap-route-0').getAttribute('aria-invalid'),
    ]).toEqual([null, 'false']);
    await type('setting-recipientMap-route-1', 'press@example.com');
    expect([root.getElementById('setting-recipientMap-route-1-why'), press.getAttribute('aria-invalid')]).toEqual([
      null,
      'false',
    ]);
  });

  it('adds Copy to and Blind copy to as chips, each with its remove button', async () => {
    const { element, root, type, keep, kept } = await sidebarOf({ definition: emailForm([TEAM]) });
    await frame();
    expect(text(root.querySelector('label[for="setting-cc"]'))).toBe('Copy to');
    // Each box's Add names what it adds to, apart from the To box's and the footer's.
    expect(must(root.querySelector('.item:has(#setting-cc) uui-button'), 'Add').getAttribute('label')).toBe(
      'Add to Copy to',
    );
    // That several go at once is said once, under the To box's address.
    expect(root.getElementById('setting-cc-several')).toBeNull();
    expect(text(root.getElementById('setting-to-address-several'))).toBe('Several at once: separate them with commas.');
    await type('setting-cc', 'boss@example.com; deputy@example.com');
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    await type('setting-bcc', 'audit@example.com');
    expect(chips(root, 'setting-cc')).toEqual(['boss@example.com', 'deputy@example.com']);
    press(root, 'Remove deputy@example.com');
    await settle(element);
    expect(root.activeElement?.getAttribute('aria-label')).toBe('Remove boss@example.com');
    await keep();
    expect(kept().settings).toEqual({
      to: ['sales@example.com'],
      cc: ['boss@example.com'],
      bcc: ['audit@example.com'],
    });
  });

  it("offers the fields of each picker's kind, one that is gone said so, and says where the form has none", async () => {
    const odd = action('a1', { settings: { to: ['sales@example.com'], replyToField: 'gone' } });
    const { element, root, box, keep, kept } = await sidebarOf({ action: odd, definition: emailForm([odd]) });
    const reply = box<HTMLSelectElement>('setting-replyToField');
    expect([...reply.options].map((option) => text(option))).toEqual(['None', 'Email address', 'A field that is gone']);
    expect(reply.value).toBe('gone');
    reply.value = 'mail';
    reply.dispatchEvent(new Event('change'));
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    const ticks = (id: string) => [...root.querySelectorAll(`#${id} label`)].map((one) => text(one));
    expect(ticks('setting-includeSensitive')).toEqual(['Secret']);
    expect(ticks('setting-attachments')).toEqual(['Your CV']);
    must(root.querySelector<HTMLInputElement>('#setting-attachments-0'), 'Your CV').click();
    await settle(element);
    await keep();
    expect(kept().settings).toEqual({ to: ['sales@example.com'], replyToField: 'mail', attachments: ['cv'] });
    // A form without such a field says so.
    document.body.replaceChildren();
    const plain = await sidebarOf();
    must(plain.root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(plain.element);
    expect(text(plain.root.querySelector('#setting-attachments .help'))).toBe('This form has no file upload yet.');
    // One field, none to pick: the label is no label of a box, and the line takes Fix it's focus.
    document.body.replaceChildren();
    const named = await sidebarOf({
      definition: {
        ...definitionWith([TEAM]),
        pages: [
          {
            key: 'p1',
            groups: [{ key: 'g1', fields: [{ key: 'n', type: 'text', alias: 'name', label: { '': 'Name' } }] }],
          },
        ],
      },
    });
    expect([
      named.root.querySelector('label[for="setting-replyToField"]'),
      named.root.getElementById('setting-replyToField')?.getAttribute('tabindex'),
      text(named.root.getElementById('setting-replyToField')),
    ]).toEqual([null, '-1', 'This form has no email field yet.']);
    expect(text(plain.root.querySelector('#setting-includeSensitive .help'))).toBe(
      'This form has no field marked sensitive.',
    );
  });

  it("offers the site's default layout by its name, the built-in ones and the library's, and one that is gone", async () => {
    const layouts = [
      { key: 'card', name: '#jfbEmailLayouts_card' },
      { key: 'letter', name: '#jfbEmailLayouts_letter' },
      { key: 'brand', name: 'Brand mail' },
    ];
    const gone = action('a1', { settings: { to: ['sales@example.com'], layout: 'old' } });
    const { element, root, box, keep, kept } = await sidebarOf({ action: gone, layouts, defaultLayout: 'brand' });
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    const layout = box<HTMLSelectElement>('setting-layout');
    expect([...layout.options].map((option) => text(option))).toEqual([
      'Site default (Brand mail)',
      'A layout that is gone',
      'Card',
      'Letter',
      'Brand mail',
    ]);
    expect(layout.value).toBe('old');
    layout.value = 'letter';
    layout.dispatchEvent(new Event('change'));
    await settle(element);
    await keep();
    expect(kept().settings).toEqual({ to: ['sales@example.com'], layout: 'letter' });
  });

  it("writes a typed subject into the texts of the language shown, keeping the others' as the server reads them", async () => {
    const both = action('a1', {
      settings: { to: ['sales@example.com'], subject: { '': 'New entry', 'de-DE': 'Neuer Eintrag' } },
    });
    const languages = [
      { culture: 'en-US', name: 'English', isDefault: true },
      { culture: 'de-DE', name: 'Deutsch', isDefault: false },
    ];
    const english = await sidebarOf({ action: both, languages });
    await english.type('setting-subject', 'New enquiry');
    await english.keep();
    expect(english.kept().settings?.['subject']).toEqual({ '': 'New enquiry', 'de-DE': 'Neuer Eintrag' });
    document.body.replaceChildren();
    const german = await sidebarOf({ action: both, languages, culture: 'de-DE' });
    await german.type('setting-subject', 'Neue Anfrage');
    await german.keep();
    expect(german.kept().settings?.['subject']).toEqual({ '': 'New entry', 'de-DE': 'Neue Anfrage' });
  });

  it('offers Insert merge tag for mail under the subject and the body, and none for another type', async () => {
    const { root } = await sidebarOf();
    const pickers = [...root.querySelectorAll('jfb-merge-tag-picker')] as unknown as { place: string }[];
    expect(pickers.map((picker) => picker.place)).toEqual(['mail', 'mail']);
    expect(root.querySelector('.item:has(#setting-subject) jfb-merge-tag-picker')).not.toBeNull();
    document.body.replaceChildren();
    const hook = action('a1', { type: 'webhook', name: 'CRM', settings: { url: 'https://crm.example.com' } });
    const webhook = await sidebarOf({ action: hook });
    expect(webhook.root.querySelectorAll('jfb-merge-tag-picker')).toHaveLength(0);
  });

  it("shows the mail beside an email's settings, asked at once and again once the changes pause, and none for another type", async () => {
    const asked: [DesignDefinition, string, string][] = [];
    const preview = async (definition: DesignDefinition, key: string, culture: string) => {
      asked.push([definition, key, culture]);
      return MAIL;
    };
    const { element, root, type, pause } = await sidebarOf({ preview });
    expect(asked.map(([, key, culture]) => [key, culture])).toEqual([['a1', 'en-US']]);
    expect(root.querySelector('.layout')?.classList.contains('with-preview')).toBe(true);
    const shown = must(root.querySelector('jfb-email-preview'), 'the preview') as unknown as {
      state: { status: string; preview: unknown };
    };
    expect(shown.state).toEqual({ status: 'shown', preview: MAIL });
    await type('setting-subject', 'Hello {{ field.email }}');
    expect(asked).toHaveLength(1);
    await pause();
    await settle(element);
    expect(asked).toHaveLength(2);
    expect(actionsOf(asked[1]?.[0] ?? { pages: [] })[0]?.settings).toEqual({
      to: ['sales@example.com'],
      subject: { '': 'Hello {{ field.email }}' },
    });
    // Who may only read the actions sees the mail too.
    document.body.replaceChildren();
    const reader = await sidebarOf({ preview, readOnly: true });
    expect(reader.root.querySelector('jfb-email-preview')).not.toBeNull();
    // Another type sends no mail of its own: nothing is asked, and the settings have the room.
    document.body.replaceChildren();
    asked.length = 0;
    const hook = action('a1', { type: 'webhook', name: 'CRM', settings: { url: 'https://crm.example.com' } });
    const webhook = await sidebarOf({ action: hook, preview });
    expect([webhook.root.querySelector('jfb-email-preview'), asked]).toEqual([null, []]);
  });

  it("describes Add recipient with the To box's problem, where Fix it lands", async () => {
    const nobody = action('a1', { settings: { subject: { '': 'Hello' }, body: { '': '<p>Hi.</p>' } } });
    checking.answer = () => [problem('$.actions[0].settings.to', 'invalid', 'A mail needs someone to go to.')];
    const { root } = await sidebarOf({ action: nobody, definition: emailForm([nobody]) });
    await frame();
    const summary = must(root.querySelector('details.add summary'), 'Add recipient');
    expect(summary.getAttribute('aria-describedby')).toBe('setting-to-found-0');
    expect(text(root.getElementById('setting-to-found-0'))).toBe('A mail needs someone to go to.');
  });

  it('says the To box and the layout in words for who may only read them', async () => {
    const everyone = action('a1', {
      settings: {
        to: ['a@example.com'],
        toField: 'mail',
        toCreator: true,
        recipientMap: { field: 'topic', routes: [] },
        cc: ['boss@example.com'],
        layout: 'letter',
      },
    });
    const { element, root } = await sidebarOf({
      action: everyone,
      definition: emailForm([everyone]),
      readOnly: true,
      layouts: [{ key: 'letter', name: '#jfbEmailLayouts_letter' }],
    });
    // One recipient a line: each kind is a phrase of its own.
    expect(root.getElementById('setting-to')?.textContent).toBe(
      'a@example.com\nThe answer of Email address\nThe person who created the form\nAddresses chosen by Topic',
    );
    expect(text(root.getElementById('setting-cc'))).toBe('boss@example.com');
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    expect(text(root.getElementById('setting-layout'))).toBe('Letter');
  });
});
