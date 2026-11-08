// An action's sidebar (ROADMAP 10.2a, ADR-0188): a new action starts ready to email the team, another choice of what
// happens replaces the settings and the name until one is typed, and an action of the form keeps its type; each trigger
// says when it runs or the setting it needs; the type's settings as the inspector draws them, Essentials then More
// options, written back as the server keeps them; a value it can't keep refused at its box; the form check's problems in
// the action under their box, at once for those it had, others once left, Add or Update kept back by an error but not by
// a check that gives no answer, and kept once for two presses; the rule in the rules builder as a `run` rule; Fix it and
// Move to another trigger...; read-only, every setting as it is with Close only; and Delete action. An email (ADR-0189):
// its To box with every kind of recipient, each added and taken away again; Copy to's addresses; fields picked by
// kind; the layouts; Insert merge tag for mail; and its mail's preview, asked once the changes pause. A webhook
// (ADR-0191): where it goes and its address, each target's kept while another is chosen; the ways of sending, only the
// chosen one in the draft; the table's rows, the headers and the secret, a credential never kept as itself; the check's
// problems at their box; and its boxes in words for who may only read them.
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
import type { ContentType } from './content-model';

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
    checking.answer = () => [problem('$.actions[0].settings.mapping', 'setting_invalid', 'Each property has a value.')];
    // A newer server may serve an editor this backoffice does not know yet.
    const types = ACTION_TYPES.map((type) =>
      type.alias === 'content'
        ? {
            ...type,
            settings: type.settings.map((setting) =>
              setting.name === 'mapping' ? { ...setting, editor: 'laterEditor' } : setting,
            ),
          }
        : type,
    );
    const hook = action('a1', { type: 'content', name: 'Page', settings: { mapping: [{}] } });
    const { root, keep, why, modal } = await sidebarOf({ action: hook, actionTypes: types });
    expect(why('what-happens')).toEqual(['Properties: Each property has a value.']);
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    await keep();
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'what-happens']);
    // Its Fix it leads there too.
    document.body.replaceChildren();
    const fixing = await sidebarOf({ action: hook, actionTypes: types, focus: { kind: 'setting', path: 'mapping' } });
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
    // A value keeps its line breaks, so no line stands empty before or after its words.
    expect(
      ['setting-to', 'setting-subject', 'stop-on-failure'].map((id) => root.getElementById(id)?.textContent),
    ).toEqual(['sales@example.com', 'New entry', 'Off']);
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
      'Everyone who may use the privacy tools',
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
    press(root, 'Everyone who may use the privacy tools');
    await settle(element);
    expect(root.activeElement?.getAttribute('aria-label')).toBe('Remove everyone who may use the privacy tools');
    expect([...root.querySelectorAll('.recipient .grow')].map((one) => text(one))).toEqual([
      'The person who created the form',
      'Everyone who may use the privacy tools',
    ]);
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
      toPrivacyTeam: true,
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

  it("offers Insert merge tag for mail under the subject and the body, an action's under a content item's name", async () => {
    const { root } = await sidebarOf();
    const pickers = [...root.querySelectorAll('jfb-merge-tag-picker')] as unknown as { place: string }[];
    expect(pickers.map((picker) => picker.place)).toEqual(['mail', 'mail']);
    expect(root.querySelector('.item:has(#setting-subject) jfb-merge-tag-picker')).not.toBeNull();
    document.body.replaceChildren();
    const page = action('a1', { type: 'content', name: 'Page', settings: {} });
    const content = await sidebarOf({ action: page });
    const named = [...content.root.querySelectorAll('.item:has(#setting-name) jfb-merge-tag-picker')] as unknown as {
      place: string;
    }[];
    expect(named.map((picker) => picker.place)).toEqual(['action']);
    // The webhook's editors place their own, where they belong.
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

describe("a webhook's sidebar", () => {
  const press = (root: ShadowRoot, label: string) =>
    must(
      [...root.querySelectorAll<HTMLButtonElement>('button')].find(
        (one) => text(one) === label || one.getAttribute('aria-label') === label,
      ),
      label,
    ).click();
  const select = async (element: { updateComplete: Promise<unknown> }, box: HTMLSelectElement, value: string) => {
    box.value = value;
    box.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const names = (root: ShadowRoot) =>
    [...root.querySelectorAll<HTMLInputElement>('.pairs input.name')].map((one) => one.value);
  const values = (root: ShadowRoot) =>
    [...root.querySelectorAll<HTMLInputElement>('.pairs input.value')].map((one) => one.value);
  const hook = (settings: Record<string, unknown>) => action('a1', { type: 'webhook', name: 'CRM', settings });

  it("says where it goes in one box, the address's help for its target, and keeps each target's while another is chosen", async () => {
    const { element, root, box, type, keep, kept, pause } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in' }),
    });
    const target = box<HTMLSelectElement>('setting-target');
    expect([text(root.querySelector('label[for="setting-target"]')), target.value]).toEqual(['Where it goes', 'url']);
    expect([...target.options].map((option) => text(option))).toEqual([
      'An address of its own',
      'Slack',
      'Microsoft Teams',
    ]);
    expect(text(root.getElementById('setting-url-help'))).toBe(
      'An https address on a host the site allows. Where the address holds a key, name it as a reference, such as $JironFormBuilder:Secrets:CrmHook.',
    );
    await select(element, target, 'slack');
    // A chat: empty for the site's address, the preset's message to start from, the format and the answers gone.
    expect(box('setting-url').value).toBe('');
    expect(text(root.getElementById('setting-url-help'))).toContain("Empty sends to the site's Slack address.");
    expect(box<HTMLTextAreaElement>('setting-message').value).toBe(
      '*New entry in {{ form.name }}*\n{{ entry.answers }}',
    );
    expect(root.getElementById('setting-format')).toBeNull();
    // A Slack address holds its key: one typed as itself is said under the box and never kept.
    await type('setting-url', 'https://hooks.slack.com/services/T0/B0/xyz');
    await pause();
    expect(JSON.stringify(checking.asked)).not.toContain('hooks.slack.com');
    expect(text(root.getElementById('setting-url-why'))).toBe(
      "A Slack address holds its key, so it stays in the site's configuration: name it as a reference.",
    );
    await type('setting-url', '$JironFormBuilder:Secrets:SalesSlack');
    expect(root.getElementById('setting-url-why')).toBeNull();
    // Back to its own address: its address comes back, and the chat's stays for when it is chosen again.
    await select(element, box<HTMLSelectElement>('setting-target'), 'url');
    expect(box('setting-url').value).toBe('https://crm.example.com/in');
    await select(element, box<HTMLSelectElement>('setting-target'), 'slack');
    expect(box('setting-url').value).toBe('$JironFormBuilder:Secrets:SalesSlack');
    await keep();
    expect(kept().settings).toEqual({
      target: 'slack',
      url: '$JironFormBuilder:Secrets:SalesSlack',
      message: '*New entry in {{ form.name }}*\n{{ entry.answers }}',
    });
  });

  it('keeps the signing secret of its own address while a chat, which checks no signature, is chosen', async () => {
    const { element, root, box, keep, kept } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', secret: '$JironFormBuilder:Secrets:CrmSigning' }),
    });
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    expect(box('setting-secret').value).toBe('$JironFormBuilder:Secrets:CrmSigning');
    await select(element, box<HTMLSelectElement>('setting-target'), 'teams');
    expect(root.getElementById('setting-secret')).toBeNull();
    await select(element, box<HTMLSelectElement>('setting-target'), 'url');
    expect(box('setting-secret').value).toBe('$JironFormBuilder:Secrets:CrmSigning');
    await select(element, box<HTMLSelectElement>('setting-target'), 'teams');
    await keep();
    expect(kept().settings).toEqual({
      target: 'teams',
      message: '**New entry in {{ form.name }}**\n{{ entry.answers }}',
    });
  });

  it('refuses a reference that is none, under the address, for its own address too', async () => {
    const { root, type } = await sidebarOf({ action: hook({ url: 'https://crm.example.com/in' }) });
    await type('setting-url', '$JironFormBuilder:Secrets:crm hook');
    expect(text(root.getElementById('setting-url-why'))).toBe(
      'After $JironFormBuilder:Secrets:, a reference has 1 to 64 letters, digits and _.',
    );
  });

  it('offers the ways of sending with what each means, and keeps only the chosen way in the draft', async () => {
    const { element, root, box, choose, keep, kept } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', metadata: false }),
    });
    expect(text(root.querySelector('fieldset:has(input[name="webhook-body"]) > legend'))).toBe('What it sends');
    expect(box('setting-metadata').checked).toBe(false);
    expect(
      [...root.querySelectorAll('fieldset:has(input[name="webhook-body"]) .choice')].map((one) => text(one)),
    ).toEqual([
      "Every answer, by its field's alias Fields added later go too.",
      'The names and values you choose The list starts with every field. Each value can hold merge tags.',
      'A body you write Merge tags are escaped for the format above.',
      "Only that something happened No answers: what happened, the form and the entry's key.",
    ]);
    expect(root.querySelector<HTMLInputElement>('input[name="webhook-body"]:checked')?.value).toBe('answers');
    expect(text(root.querySelector('label[for="setting-metadata"]'))).toBe('Send metadata too');
    // The table starts with every field, by its alias, under its own choice, which names it.
    await choose('webhook-body', 'mapping');
    expect(root.querySelector('label.choice:has(input[value="mapping"]) + .mode #setting-fields')).not.toBeNull();
    expect(root.getElementById('setting-fields')?.getAttribute('aria-labelledby')).toBe('webhook-body-mapping-name');
    expect([names(root), values(root)]).toEqual([
      ['email', 'topic'],
      ['{{ field.email }}', '{{ field.topic }}'],
    ]);
    expect([...root.querySelectorAll('.pairs-head span')].map((one) => text(one))).toEqual(['Name', 'Value']);
    // A body to write starts from them in the format's language; the table comes back as it was.
    await choose('webhook-body', 'body');
    expect(box<HTMLTextAreaElement>('setting-template').value).toBe(
      '{\n  "email": "{{ field.email }}",\n  "topic": "{{ field.topic }}"\n}',
    );
    expect(box<HTMLTextAreaElement>('setting-template').getAttribute('aria-labelledby')).toBe('webhook-body-body-name');
    expect(root.getElementById('setting-metadata')).toBeNull();
    const pickers = [...root.querySelectorAll('jfb-merge-tag-picker')] as unknown as { place: string }[];
    expect(pickers.map((picker) => picker.place)).toEqual(['action']);
    await choose('webhook-body', 'mapping');
    expect(names(root)).toEqual(['email', 'topic']);
    // Only that something happened: no answers, the metadata with it.
    await choose('webhook-body', 'none');
    expect(root.getElementById('setting-metadata')).toBeNull();
    await keep();
    expect(kept().settings).toEqual({ url: 'https://crm.example.com/in', withoutAnswers: true });
    void element;
  });

  it("edits the table's names and values, says what can't be kept as it is typed, and keeps the rows that can", async () => {
    const { element, root, box, type, keep, kept } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', fields: { email: '{{ field.email }}' } }),
    });
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    await type('setting-fields-name-0', 'e mail', false);
    expect(text(root.getElementById('setting-fields-name-0-why'))).toBe('A name has letters, digits, _, - and . only.');
    await type('setting-fields-name-0', 'mail');
    press(root, 'Add a name');
    await settle(element);
    expect(root.activeElement?.id).toBe('setting-fields-name-1');
    // A value without its name is said once its box is left.
    await type('setting-fields-value-1', 'Contact form', false);
    expect(root.getElementById('setting-fields-name-1-why')).toBeNull();
    // A row without its name is named by its place.
    expect(box('setting-fields-value-1').getAttribute('aria-label')).toBe('Value of row 2');
    await type('setting-fields-value-1', 'Contact form');
    expect(text(root.getElementById('setting-fields-name-1-why'))).toBe('Give this value a name.');
    await type('setting-fields-name-1', 'mail');
    expect(text(root.getElementById('setting-fields-name-1-why'))).toBe('mail is there already.');
    await type('setting-fields-name-1', 'source');
    expect(box('setting-fields-value-1').getAttribute('aria-label')).toBe('Value of source');
    // Remove puts the focus on the row in its place, or Add once none is left.
    press(root, 'Remove mail');
    await settle(element);
    expect([names(root), root.activeElement?.id]).toEqual([['source'], 'setting-fields-name-0']);
    await keep();
    expect(kept().settings).toEqual({ url: 'https://crm.example.com/in', fields: { source: 'Contact form' } });
  });

  it("puts a merge tag into the table's value last in focus", async () => {
    const { element, root, box } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', fields: { a: '', b: '' } }),
    });
    const picker = must(root.querySelector('.pairs jfb-merge-tag-picker'), 'the picker') as unknown as {
      insert: (tag: string) => void;
    };
    picker.insert('{{ field.email }}');
    await settle(element);
    expect(values(root)).toEqual(['', '{{ field.email }}']);
    box('setting-fields-value-0').dispatchEvent(new Event('focus'));
    picker.insert('{{ form.name }}');
    await settle(element);
    expect(values(root)).toEqual(['{{ form.name }}', '{{ field.email }}']);
  });

  it("says a chat's message, or a body of its own in JSON as the chat reads it", async () => {
    const { root, box, choose, keep, kept } = await sidebarOf({ action: hook({ target: 'teams' }) });
    expect(
      [...root.querySelectorAll('fieldset:has(input[name="webhook-body"]) .choice-name')].map((one) => text(one)),
    ).toEqual(['A message', 'A body you write']);
    const message = box<HTMLTextAreaElement>('setting-message');
    // Without a message of its own, the default goes; the box says so.
    expect([message.value, message.getAttribute('aria-labelledby'), message.placeholder]).toEqual([
      '',
      'webhook-body-message-name',
      '**New entry in {{ form.name }}**\n{{ entry.answers }}',
    ]);
    expect(text(root.getElementById('setting-message-help'))).toBe("Empty sends the form's name and the answers.");
    expect(text(root.getElementById('webhook-body-message'))).toBe(
      "In Teams' formatting, such as **bold**, with merge tags.",
    );
    await choose('webhook-body', 'body');
    const body = JSON.parse(box<HTMLTextAreaElement>('setting-template').value) as {
      attachments: { content: { body: { text: string }[] } }[];
    };
    // A text block a line, as a card breaks lines only between blocks.
    expect(body.attachments[0]?.content.body.map((block) => block.text)).toEqual([
      '**New entry in {{ form.name }}**',
      '{{ entry.answers }}',
    ]);
    await keep();
    expect(Object.keys(kept().settings ?? {})).toEqual(['target', 'template']);
  });

  it('refuses a cleared body under its box, and keeps the last one', async () => {
    const { root, type, choose, keep, kept } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', template: '{"a":1}' }),
    });
    await type('setting-template', '  ');
    expect(text(root.getElementById('setting-template-why'))).toBe(
      'Write the body, or choose another way of sending above.',
    );
    // What it said goes with the box: another way keeps nothing back.
    await choose('webhook-body', 'answers');
    expect(root.getElementById('setting-template-why')).toBeNull();
    await choose('webhook-body', 'body');
    expect(text(root.getElementById('setting-template'))).toBe('');
    expect((root.getElementById('setting-template') as HTMLTextAreaElement).value).toBe('{"a":1}');
    await keep();
    expect(kept().settings).toEqual({ url: 'https://crm.example.com/in', template: '{"a":1}' });
  });

  it("lets a message's problem go with its box, so Update keeps nothing back for it", async () => {
    const { type, choose, keep, modal } = await sidebarOf({ action: hook({ target: 'slack', message: 'Hi' }) });
    await type('setting-message', 'x'.repeat(50_001));
    await choose('webhook-body', 'body');
    await keep();
    expect(modal.submit).toHaveBeenCalled();
  });

  it('brings back the metadata switch after Only that something happened, and keeps a mapping the draft holds', async () => {
    const { box, choose, keep, kept } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', metadata: false, template: '{}', fields: { kept: 'yes' } }),
    });
    // A body won over the mapping the draft holds; choosing the table brings that mapping, not every field.
    await choose('webhook-body', 'mapping');
    expect(box('setting-fields-name-0').value).toBe('kept');
    await choose('webhook-body', 'none');
    await choose('webhook-body', 'mapping');
    expect([box('setting-fields-name-0').value, box('setting-metadata').checked]).toEqual(['kept', false]);
    await keep();
    expect(kept().settings).toEqual({ url: 'https://crm.example.com/in', metadata: false, fields: { kept: 'yes' } });
  });

  it('starts the table from what every answer sends: a field in a group, a sensitive one only where it is named', async () => {
    const repeat: DesignField = {
      key: 'kids',
      type: 'repeatableGroup',
      alias: 'kids',
      label: { '': 'Children' },
      fields: [{ key: 'kid', type: 'text', alias: 'kid', label: { '': 'Child' } }],
    } as unknown as DesignField;
    const named = hook({ url: 'https://crm.example.com/in', includeSensitive: ['secret'] });
    const { root, choose } = await sidebarOf({ action: named, definition: emailForm([named], [repeat]) });
    await choose('webhook-body', 'mapping');
    expect([...root.querySelectorAll<HTMLInputElement>('.pairs input.name')].map((one) => one.value)).toEqual([
      'email',
      'topic',
      'secret',
      'cv',
    ]);
  });

  it('takes headers as rows, a credential and the signing secret only as references, never as themselves', async () => {
    const { element, root, box, type, keep, kept, pause } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', headers: { 'X-Source': 'forms' } }),
    });
    await frame();
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    expect(text(root.getElementById('setting-headers-help'))).toBe(
      'Sent with each request. A header whose name has auth, key, token, secret, password or cookie in it takes a reference.',
    );
    expect(text(root.getElementById('setting-secret-help'))).toContain('X-Jfb-Signature');
    press(root, 'Add header');
    await settle(element);
    await type('setting-headers-name-1', 'Authorization');
    await type('setting-headers-value-1', 'Bearer abc');
    const asked = () => JSON.stringify(checking.asked);
    expect(text(root.getElementById('setting-headers-value-1-why'))).toBe(
      'Authorization carries a credential: give it as a reference, such as $JironFormBuilder:Secrets:CrmToken.',
    );
    expect(box('setting-headers-value-1').getAttribute('aria-invalid')).toBe('true');
    await type('setting-headers-value-1', '$JironFormBuilder:Secrets:CrmToken');
    press(root, 'Add header');
    await settle(element);
    await type('setting-headers-name-2', 'Host');
    expect(text(root.getElementById('setting-headers-name-2-why'))).toBe('The request sets Host itself.');
    press(root, 'Remove Host');
    await settle(element);
    await type('setting-secret', 'my-signing-secret');
    await pause();
    // Neither reached the draft the check was asked about, which is what Update keeps.
    expect([asked().includes('Bearer abc'), asked().includes('my-signing-secret')]).toEqual([false, false]);
    expect(text(root.getElementById('setting-secret-why'))).toBe(
      'Give the secret as a reference, such as $JironFormBuilder:Secrets:CrmSigning.',
    );
    await type('setting-secret', '$JironFormBuilder:Secrets:CrmSigning');
    await keep();
    expect(kept().settings).toEqual({
      url: 'https://crm.example.com/in',
      headers: { 'X-Source': 'forms', Authorization: '$JironFormBuilder:Secrets:CrmToken' },
      secret: '$JironFormBuilder:Secrets:CrmSigning',
    });
  });

  it('keeps a row out of the draft until its box is left, so no value sits there under a name half typed', async () => {
    const { element, root, type, pause } = await sidebarOf({ action: hook({ url: 'https://crm.example.com/in' }) });
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    press(root, 'Add header');
    await settle(element);
    await type('setting-headers-value-0', 'Bearer abc', false);
    await type('setting-headers-name-0', 'Au', false);
    await pause();
    expect(JSON.stringify(checking.asked)).not.toContain('Bearer abc');
  });

  it("keeps Update back while a row can't be kept, the focus on it", async () => {
    const { element, root, type, keep, modal } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', fields: { a: 'b' } }),
    });
    await frame();
    await type('setting-fields-name-0', 'a b');
    await keep();
    expect([modal.submit.mock.calls.length, root.activeElement?.id]).toEqual([0, 'setting-fields-name-0']);
    void element;
  });

  it("says the check's problems at the box they name, where Fix it lands, and those of a way not drawn under the choices", async () => {
    checking.answer = () => [
      problem('$.actions[0].settings.url', 'invalid', 'The address must be one the host configuration allows.'),
      problem('$.actions[0].settings.fields', 'invalid', 'A merge tag at 0 cannot be used here.'),
      problem('$.actions[0].settings.template', 'invalid', 'A merge tag at 3 cannot be used here.'),
    ];
    const settings = { url: 'http://crm', fields: { a: '{{ nope }}' } };
    const { root, box } = await sidebarOf({ action: hook(settings), definition: definitionWith([hook(settings)]) });
    await frame();
    expect(box('setting-url').getAttribute('aria-describedby')).toBe('setting-url-help setting-url-found-0');
    expect(text(root.getElementById('setting-url-found-0'))).toBe(
      'The address must be one the host configuration allows.',
    );
    expect(root.getElementById('setting-fields')?.getAttribute('aria-describedby')).toBe('setting-fields-found-0');
    // The body is no way this action sends, so its problem is said under the choices.
    expect(text(root.getElementById('setting-template-found-0'))).toBe('A merge tag at 3 cannot be used here.');
  });

  it("lands Fix it on the box a problem names, and on the way's choice for a setting its way does not draw", async () => {
    const settings = { url: 'http://crm', fields: { a: 'b' } };
    const at = async (path: string) => {
      document.body.replaceChildren();
      const { root } = await sidebarOf({ action: hook(settings), focus: { kind: 'setting', path } });
      await frame();
      await settle();
      return root.activeElement;
    };
    expect((await at('url'))?.id).toBe('setting-url');
    expect((await at('format'))?.id).toBe('setting-format');
    expect((await at('fields'))?.id).toBe('setting-fields-name-0');
    // A body is no way this one sends: the choice of the way it does.
    const chosen = await at('template');
    expect([chosen?.getAttribute('name'), (chosen as HTMLInputElement | null)?.value]).toEqual([
      'webhook-body',
      'mapping',
    ]);
  });

  it('names each way by its name and describes it by its line, each once', async () => {
    const { root } = await sidebarOf({ action: hook({ url: 'https://crm.example.com/in' }) });
    const radio = must(root.querySelector('input[name="webhook-body"][value="none"]'), 'the radio');
    expect([radio.getAttribute('aria-labelledby'), radio.getAttribute('aria-describedby')]).toEqual([
      'webhook-body-none-name',
      'webhook-body-none',
    ]);
  });

  it("keeps the address's guard where a host locks the target, and draws what the host's target sends", async () => {
    const types = ACTION_TYPES.map((type) =>
      type.alias === 'webhook'
        ? {
            ...type,
            settings: type.settings.map((setting) =>
              setting.name === 'target'
                ? {
                    ...setting,
                    lock: { hidden: false, configurationKey: 'JironFormBuilder:Locks:Target', value: 'slack' },
                  }
                : setting,
            ),
          }
        : type,
    );
    const { root, box, type } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in' }),
      actionTypes: types,
    });
    expect(root.querySelector('[data-locked="target"]')).not.toBeNull();
    // The draft says its own address; the host's target is Slack, whose message the box draws.
    expect(root.getElementById('setting-message')).not.toBeNull();
    await type('setting-url', 'https://hooks.slack.com/services/T0/B0/xyz');
    expect(text(root.getElementById('setting-url-why'))).toBe(
      "A Slack address holds its key, so it stays in the site's configuration: name it as a reference.",
    );
    void box;
  });

  it('draws the table, and each setting as it is, where a host locks a way of sending', async () => {
    const types = ACTION_TYPES.map((type) =>
      type.alias === 'webhook'
        ? {
            ...type,
            settings: type.settings.map((setting) =>
              setting.name === 'message'
                ? {
                    ...setting,
                    lock: { hidden: false, configurationKey: 'JironFormBuilder:Locks:Message', value: null },
                  }
                : setting,
            ),
          }
        : type,
    );
    const { root } = await sidebarOf({
      action: hook({ url: 'https://crm.example.com/in', fields: { a: 'b' } }),
      actionTypes: types,
    });
    expect(root.getElementById('setting-fields-name-0')).not.toBeNull();
    // A chat's message is no setting of its own address.
    expect(root.querySelector('[data-locked="message"]')).toBeNull();
    expect(root.getElementById('setting-template')).not.toBeNull();
  });

  it('says where it goes and what it sends in words for who may only read them', async () => {
    const reader = async (settings: Record<string, unknown>) => {
      document.body.replaceChildren();
      const { element, root } = await sidebarOf({ action: hook(settings), readOnly: true });
      const more = root.querySelector<HTMLButtonElement>('.more');
      if (more) {
        more.click();
        await settle(element);
      }
      return (id: string) => root.getElementById(id)?.textContent ?? '';
    };
    let said = await reader({
      url: 'https://crm.example.com/in',
      headers: { Authorization: '$JironFormBuilder:Secrets:CrmToken' },
    });
    expect([said('setting-target'), said('setting-fields'), said('setting-headers')]).toEqual([
      'An address of its own\nhttps://crm.example.com/in',
      "JSON: every answer, by its field's alias, with metadata",
      'Authorization: $JironFormBuilder:Secrets:CrmToken',
    ]);
    said = await reader({
      url: 'https://crm.example.com/in',
      format: 'xml',
      fields: { email: 'x', topic: 'y' },
      metadata: false,
    });
    expect(said('setting-fields')).toBe('XML: email, topic');
    said = await reader({ url: 'https://crm.example.com/in', withoutAnswers: true });
    expect(said('setting-fields')).toBe('JSON: only that something happened');
    said = await reader({ target: 'slack', message: 'Hi {{ field.email }}' });
    expect([said('setting-target'), said('setting-fields')]).toEqual(['Slack', 'Hi {{ field.email }}']);
    // A secret written as itself, before this step or over the API, is never shown.
    said = await reader({
      target: 'slack',
      url: 'https://hooks.slack.com/services/T0/B0/xyz',
      secret: 'plain-secret',
      headers: { Authorization: 'Bearer abc', 'X-Source': 'forms' },
    });
    const hidden = 'A secret written as itself, not shown. Replace it with a reference.';
    expect([said('setting-target'), said('setting-headers')]).toEqual([
      `Slack\n${hidden}`,
      `Authorization: ${hidden}\nX-Source: forms`,
    ]);
    said = await reader({ url: 'https://crm.example.com/in', secret: 'plain-secret' });
    expect(said('setting-secret')).toBe(hidden);
    said = await reader({ target: 'teams', template: '{}' });
    expect(said('setting-fields')).toBe('A body you write');
    said = await reader({ url: 'https://crm.example.com/in', format: 'xml', template: '<a/>' });
    expect(said('setting-fields')).toBe('XML: a body you write');
  });
});

describe("a content action's sidebar", () => {
  const STORY: ContentType = {
    alias: 'story',
    name: 'Story',
    properties: [
      { alias: 'title', name: 'Title', group: 'Content', fieldTypes: ['text', 'email'], value: 'text' },
      { alias: 'count', name: 'Count', group: 'Content', fieldTypes: ['number'], value: 'wholeNumber' },
      { alias: 'featured', name: 'Featured', group: 'Content', fieldTypes: [], value: 'boolean' },
      { alias: 'photo', name: 'Photo', group: null, fieldTypes: ['file'], value: null },
    ],
  };
  const NOTE: ContentType = {
    alias: 'note',
    name: 'Note',
    properties: [
      { alias: 'title', name: 'Title', group: null, fieldTypes: ['text', 'email'], value: 'text' },
      { alias: 'email', name: 'Email', group: null, fieldTypes: ['email', 'text'], value: 'text' },
    ],
  };
  // The site's document types, served with the content type as its document type's choices.
  const TYPES = ACTION_TYPES.map((type) =>
    type.alias !== 'content'
      ? type
      : {
          ...type,
          settings: type.settings.map((setting) =>
            setting.name === 'documentType'
              ? {
                  ...setting,
                  choices: [
                    { value: 'story', label: 'Story' },
                    { value: 'note', label: 'Note' },
                  ],
                }
              : setting,
          ),
        },
  );
  const form = (actions: readonly DesignAction[]): DesignDefinition => ({
    ...definitionWith(actions),
    pages: [
      {
        key: 'p1',
        groups: [
          {
            key: 'g1',
            fields: [
              { key: 'f-title', type: 'text', alias: 'title', label: { '': 'Headline' } },
              { key: 'f-email', type: 'email', alias: 'email', label: { '': 'Email address' } },
              { key: 'f-age', type: 'number', alias: 'age', label: { '': 'Age' } },
            ],
          },
        ],
      },
    ],
  });
  const content = (settings: Record<string, unknown>) =>
    action('a1', { type: 'content', name: 'Save the story', settings });
  const read: string[] = [];
  const open = (settings: Record<string, unknown>, extra: Partial<ActionModalData> = {}) => {
    const one = content(settings);
    return sidebarOf({
      action: one,
      definition: form([one]),
      actionTypes: TYPES,
      choices: choicesOf(TYPES),
      documentType: async (alias) => {
        read.push(alias);
        return alias === 'story' ? STORY : alias === 'note' ? NOTE : null;
      },
      ...extra,
    });
  };
  const select = async (element: { updateComplete: Promise<unknown> }, box: HTMLSelectElement, value: string) => {
    box.value = value;
    box.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const options = (box: HTMLSelectElement) =>
    [...box.querySelectorAll('option')].filter((one) => !one.disabled).map((one) => text(one));
  const rowIds = (root: ShadowRoot) =>
    [...root.querySelectorAll<HTMLSelectElement>('.map-row select.property')].map((one) => one.id);
  const add = async (root: ShadowRoot, element: { updateComplete: Promise<unknown> }) => {
    must(root.querySelector<HTMLButtonElement>('#setting-mapping-add'), 'Add property').click();
    await settle(element);
  };

  beforeEach(() => {
    read.length = 0;
  });

  it('says where it goes as three choices, each with its line, the item picked under the chosen one', async () => {
    const { root, choose, keep, kept } = await open({ documentType: 'story' });
    const place = must(root.getElementById('setting-parentSource'), 'Where it goes');
    expect([place.localName, text(place.querySelector('legend'))]).toEqual(['fieldset', 'Where it goes']);
    expect([...place.querySelectorAll('.choice-name')].map((one) => text(one))).toEqual([
      'Under an item you pick',
      'Under the page the form is sent from',
      "Under the root of the visitor's site",
    ]);
    expect(text(root.getElementById('content-parent-picked'))).toBe('Without one, at the root of the content tree.');
    expect(place.querySelector<HTMLInputElement>('input[value="picked"]')?.checked).toBe(true);
    // The item picked under the chosen choice, named for it to who hears it.
    expect(text(root.getElementById('setting-parent-label'))).toBe('Under this item');
    expect(root.querySelector('label:has(input[value="picked"]) + .parent #setting-parent-label')).not.toBeNull();
    await choose('content-parent', 'page');
    expect(text(root.getElementById('setting-parent-label'))).toBe('Only within this item');
    expect(root.querySelector('label:has(input[value="page"]) + .parent')).not.toBeNull();
    expect(root.querySelectorAll('.parent')).toHaveLength(1);
    await keep();
    expect(kept().settings).toEqual({ documentType: 'story', parentSource: 'page' });
  });

  it("keeps the item's name as typed while the table, the place and the check change around it", async () => {
    const { element, root, box, type, choose, keep, kept } = await open({
      documentType: 'story',
      mapping: [{ property: 'title', field: 'f-title' }],
    });
    await frame();
    await type('setting-name', 'Story of {{ field.title }}', false);
    await add(root, element);
    await select(element, box<HTMLSelectElement>('setting-mapping-blank-1-property'), 'count');
    await select(element, box<HTMLSelectElement>('setting-mapping-blank-1-field'), '=value');
    await type('setting-mapping-1--value', '12');
    await choose('content-parent', 'page');
    await vi.advanceTimersByTimeAsync(CHECK_PAUSE);
    await settle(element);
    expect(box('setting-name').value).toBe('Story of {{ field.title }}');
    await keep();
    expect(kept().settings).toMatchObject({ name: 'Story of {{ field.title }}', parentSource: 'page' });
  });

  it("offers the site's document types, and says the table waits for one", async () => {
    const { root, box } = await open({});
    // None chosen stands first while nothing is; the form check asks for a type.
    expect(options(box<HTMLSelectElement>('setting-documentType'))).toEqual(['None chosen', 'Story', 'Note']);
    expect(text(root.getElementById('setting-documentType-help'))).toBe('Each entry becomes an item of this type.');
    expect(text(root.getElementById('setting-mapping'))).toBe('Choose a document type, and its properties come here.');
    expect(read).toEqual([]);
  });

  it('sets each property from the answer of a field it can hold, or a value written for it in the form it stores', async () => {
    const { element, root, box, type, keep, kept } = await open({
      documentType: 'story',
      mapping: [{ property: 'title', field: 'f-title' }],
    });
    expect(read).toEqual(['story']);
    expect(rowIds(root)).toEqual(['setting-mapping-0--property']);
    const from = box<HTMLSelectElement>('setting-mapping-0--field');
    expect([from.value, from.getAttribute('aria-label')]).toEqual(['f-title', 'What Title takes']);
    // Only the fields a property can hold the answer of, and a value where it takes one.
    expect(options(from)).toEqual(['The answer to Headline', 'The answer to Email address', 'A value you write']);
    await add(root, element);
    expect(rowIds(root)).toEqual(['setting-mapping-0--property', 'setting-mapping-blank-1-property']);
    const added = box<HTMLSelectElement>('setting-mapping-blank-1-property');
    // A property already set is not offered again; the document type's groups are said.
    expect(options(added)).toEqual(['Count', 'Featured']);
    // One group left is no group to name.
    expect(added.querySelector('optgroup')).toBeNull();
    await select(element, added, 'count');
    expect(options(box<HTMLSelectElement>('setting-mapping-blank-1-field'))).toEqual([
      'The answer to Age',
      'A value you write',
    ]);
    await select(element, box<HTMLSelectElement>('setting-mapping-blank-1-field'), '=value');
    const count = box('setting-mapping-1--value');
    expect([count.getAttribute('inputmode'), count.placeholder, count.getAttribute('aria-label')]).toEqual([
      'numeric',
      'A whole number',
      'Value of Count',
    ]);
    await type('setting-mapping-1--value', '12');
    // Yes or no for a property that stores it.
    await add(root, element);
    await select(element, box<HTMLSelectElement>('setting-mapping-blank-2-property'), 'featured');
    expect(options(box<HTMLSelectElement>('setting-mapping-blank-2-field'))).toEqual(['A value you write']);
    await select(element, box<HTMLSelectElement>('setting-mapping-blank-2-field'), '=value');
    await select(element, box<HTMLSelectElement>('setting-mapping-2--value'), 'false');
    await keep();
    expect(kept().settings).toEqual({
      documentType: 'story',
      mapping: [
        { property: 'title', field: 'f-title' },
        { property: 'count', value: '12' },
        { property: 'featured', value: 'false' },
      ],
    });
  });

  it('takes a row away, and keeps a row without what it takes out of the draft until it has it', async () => {
    const { element, root, box, keep, kept } = await open({
      documentType: 'story',
      mapping: [
        { property: 'title', field: 'f-title' },
        { property: 'count', field: 'f-age' },
      ],
    });
    // The sidebar's own first focus, on its name, comes first.
    await frame();
    // Another property whose source does not fit: the row waits for what it takes.
    await select(element, box<HTMLSelectElement>('setting-mapping-0--property'), 'photo');
    expect(rowIds(root)).toEqual(['setting-mapping-blank-0-property', 'setting-mapping-0--property']);
    expect(box<HTMLSelectElement>('setting-mapping-blank-0-field').value).toBe('');
    must(
      [...root.querySelectorAll<HTMLButtonElement>('button.remove')].find(
        (one) => one.getAttribute('aria-label') === 'Remove Count',
      ),
      'Remove Count',
    ).click();
    await settle(element);
    await frame();
    expect(rowIds(root)).toEqual(['setting-mapping-blank-0-property']);
    expect(root.activeElement?.id).toBe('setting-mapping-blank-0-property');
    await keep();
    expect(kept().settings).toEqual({ documentType: 'story' });
  });

  it('keeps the table of a document type while another is chosen, and starts a new one with the fields of its aliases', async () => {
    const { element, box, keep, kept } = await open({
      documentType: 'story',
      mapping: [{ property: 'count', field: 'f-age' }],
    });
    await select(element, box<HTMLSelectElement>('setting-documentType'), 'note');
    await settle(element);
    expect(box<HTMLSelectElement>('setting-mapping-0--field').value).toBe('f-title');
    expect(box<HTMLSelectElement>('setting-mapping-1--property').value).toBe('email');
    await select(element, box<HTMLSelectElement>('setting-documentType'), 'story');
    await settle(element);
    expect(box<HTMLSelectElement>('setting-mapping-0--property').value).toBe('count');
    await select(element, box<HTMLSelectElement>('setting-documentType'), 'note');
    await settle(element);
    await keep();
    expect(kept().settings).toEqual({
      documentType: 'note',
      mapping: [
        { property: 'title', field: 'f-title' },
        { property: 'email', field: 'f-email' },
      ],
    });
  });

  it('keeps a table emptied by hand empty, and leaves the rows not finished with the type they name', async () => {
    const { element, root, box, keep, kept } = await open({
      documentType: 'story',
      mapping: [{ property: 'count', field: 'f-age' }],
    });
    // Story's only row removed, a row begun and not finished, then Note and Story again.
    must(root.querySelector<HTMLButtonElement>('button.remove'), 'Remove Count').click();
    await settle(element);
    await add(root, element);
    await select(element, box<HTMLSelectElement>('setting-mapping-blank-0-property'), 'count');
    await select(element, box<HTMLSelectElement>('setting-documentType'), 'note');
    await settle(element);
    expect(rowIds(root)).toEqual(['setting-mapping-0--property', 'setting-mapping-1--property']);
    await select(element, box<HTMLSelectElement>('setting-documentType'), 'story');
    await settle(element);
    expect(rowIds(root)).toEqual([]);
    await keep();
    expect(kept().settings).toEqual({ documentType: 'story' });
  });

  it("leaves a row not finished with its type where the next type's table is the same", async () => {
    const { element, root, box, keep, kept } = await open({
      documentType: 'story',
      mapping: [{ property: 'count', field: 'f-age' }],
    });
    // Story's table emptied, then Note's emptied too, and a row begun on Note's Email.
    must(root.querySelector<HTMLButtonElement>('button.remove'), 'Remove Count').click();
    await settle(element);
    await select(element, box<HTMLSelectElement>('setting-documentType'), 'note');
    await settle(element);
    while (root.querySelector('button.remove')) {
      must(root.querySelector<HTMLButtonElement>('button.remove'), 'Remove').click();
      await settle(element);
    }
    await add(root, element);
    await select(element, box<HTMLSelectElement>('setting-mapping-blank-0-property'), 'email');
    // Story, whose table is as empty: the row names a property Story lacks, and stays with Note.
    await select(element, box<HTMLSelectElement>('setting-documentType'), 'story');
    await settle(element);
    expect(rowIds(root)).toEqual([]);
    await keep();
    expect(kept().settings).toEqual({ documentType: 'story' });
  });

  it('writes a decimal comma typed for a decimal property as a point', async () => {
    const PRICED: ContentType = {
      ...STORY,
      properties: [
        ...STORY.properties,
        { alias: 'price', name: 'Price', group: 'Content', fieldTypes: ['number'], value: 'decimal' },
      ],
    };
    const { box, type, keep, kept } = await open(
      { documentType: 'story', mapping: [{ property: 'price', value: '' }] },
      { documentType: async () => PRICED },
    );
    expect(box('setting-mapping-0--value').getAttribute('inputmode')).toBe('decimal');
    await type('setting-mapping-0--value', '1,5');
    await keep();
    expect(kept().settings).toEqual({ documentType: 'story', mapping: [{ property: 'price', value: '1.5' }] });
  });

  it('says under Publish it and Update an item of the same name what each does, and describes each switch by it', async () => {
    const { element, root, box } = await open({ documentType: 'story' });
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    const helpOf = (id: string) => {
      const toggle = box(id);
      return [toggle.getAttribute('role'), text(root.getElementById(toggle.getAttribute('aria-describedby') ?? ''))];
    };
    expect(helpOf('setting-publish')).toEqual([
      'switch',
      'Off, the item is saved for an editor to look at and publish.',
    ]);
    expect(helpOf('setting-updateExisting')).toEqual([
      'switch',
      "Where the name comes from an answer, a visitor's answer chooses which item is overwritten. Turn it on only where that's what you want.",
    ]);
  });

  it('shows the media folder for files where a row takes a file upload, or where it holds a folder', async () => {
    const more = async (root: ShadowRoot, element: { updateComplete: Promise<unknown> }) => {
      must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
      await settle(element);
    };
    // No row takes a file: no folder to choose.
    const plain = await open({ documentType: 'story', mapping: [{ property: 'title', field: 'f-title' }] });
    await more(plain.root, plain.element);
    expect(plain.root.getElementById('setting-updateExisting')).not.toBeNull();
    expect(plain.root.getElementById('setting-mediaFolder')).toBeNull();
    document.body.replaceChildren();
    // A row that takes a file upload's answer.
    const one = content({ documentType: 'story', mapping: [{ property: 'photo', field: 'f-upload' }] });
    const withUpload = form([one]);
    const upload = { key: 'f-upload', type: 'file', alias: 'upload', label: { '': 'Your photo' } };
    const files = await sidebarOf({
      action: one,
      definition: {
        ...withUpload,
        pages: withUpload.pages.map((page) => ({
          ...page,
          groups: page.groups.map((group) => ({ ...group, fields: [...group.fields, upload] })),
        })),
      },
      actionTypes: TYPES,
      choices: choicesOf(TYPES),
      documentType: async () => STORY,
    });
    await more(files.root, files.element);
    expect(files.root.getElementById('setting-mediaFolder')).not.toBeNull();
    document.body.replaceChildren();
    // A folder held, though no row takes a file any more, stays to be cleared.
    const held = await open({ documentType: 'story', mediaFolder: '0a0b0c0d-0000-4000-8000-000000000001' });
    await more(held.root, held.element);
    expect(held.root.getElementById('setting-mediaFolder')).not.toBeNull();
  });

  it('keeps a value written where the next property stores it the same way, else starts its box again', async () => {
    const { element, box, keep, kept } = await open({
      documentType: 'story',
      mapping: [{ property: 'title', value: 'Hello' }],
    });
    const property = () => box<HTMLSelectElement>('setting-mapping-0--property');
    await select(element, property(), 'featured');
    expect(box<HTMLSelectElement>('setting-mapping-0--value').value).toBe('true');
    await select(element, property(), 'count');
    expect(box('setting-mapping-0--value').value).toBe('');
    await select(element, property(), 'title');
    await keep();
    expect(kept().settings).toEqual({ documentType: 'story', mapping: [{ property: 'title', value: '' }] });
  });

  it('offers only the properties the form can fill, and says a field that is gone and a property nothing fills', async () => {
    const offered = await open({ documentType: 'story', mapping: [{ property: 'title', field: 'f-title' }] });
    await add(offered.root, offered.element);
    // Photo holds files, and the form has no file upload.
    expect(options(offered.box<HTMLSelectElement>('setting-mapping-blank-1-property'))).toEqual(['Count', 'Featured']);
    document.body.replaceChildren();
    // With one, Photo is offered too, and the document type's groups are named.
    const plain = form([content({ documentType: 'story' })]);
    const withFile: DesignDefinition = {
      ...plain,
      pages: plain.pages.map((page, at) =>
        at > 0
          ? page
          : {
              ...page,
              groups: page.groups.map((group, inPage) =>
                inPage > 0
                  ? group
                  : {
                      ...group,
                      fields: [...group.fields, { key: 'f-cv', type: 'file', alias: 'cv', label: { '': 'CV' } }],
                    },
              ),
            },
      ),
    };
    const filed = await open({ documentType: 'story' }, { definition: withFile });
    await add(filed.root, filed.element);
    const property = filed.box<HTMLSelectElement>('setting-mapping-blank-0-property');
    expect(options(property)).toEqual(['Title', 'Count', 'Featured', 'Photo']);
    expect(property.querySelector('optgroup')?.getAttribute('label')).toBe('Content');
    document.body.replaceChildren();
    const { root, box } = await open({
      documentType: 'story',
      mapping: [{ property: 'title', field: 'f-gone' }, { property: 'photo' }],
    });
    const gone = box<HTMLSelectElement>('setting-mapping-0--field');
    expect([gone.value, text(gone.selectedOptions[0])]).toEqual(['f-gone', 'A field that is gone']);
    expect(text(root.getElementById('setting-mapping-blank-1-field'))).toBe(
      'This form has no field whose answer Photo can hold, and Photo takes no value written for it.',
    );
    document.body.replaceChildren();
    const reader = await open(
      { documentType: 'story', mapping: [{ property: 'title', field: 'f-gone' }] },
      { readOnly: true },
    );
    expect(text(reader.root.getElementById('setting-mapping'))).toBe('Title: a field that is gone');
  });

  it("puts a merge tag into the text value last in focus, else the last row's text value", async () => {
    const { element, root, box } = await open({
      documentType: 'story',
      mapping: [
        { property: 'title', value: 'A' },
        { property: 'count', value: '1' },
      ],
    });
    const picker = must(root.querySelector('.map jfb-merge-tag-picker'), 'the picker') as unknown as {
      insert: (tag: string) => void;
    };
    picker.insert('{{ form.name }}');
    await settle(element);
    expect(box('setting-mapping-0--value').value).toBe('A{{ form.name }}');
  });

  it("says a row's problems under its own boxes, and a Fix it lands there", async () => {
    checking.answer = () => [
      problem('$.actions[0].settings.mapping[1].field', 'setting_invalid', "The property 'count' cannot hold this."),
      problem('$.actions[0].settings.mapping', 'setting_invalid', 'The mapping is a list.'),
    ];
    const { root, box, found } = await open(
      {
        documentType: 'story',
        mapping: [
          { property: 'title', field: 'f-title' },
          { property: 'count', field: 'f-email' },
        ],
      },
      { focus: { kind: 'setting', path: 'mapping', row: { index: 1, part: 'field' } } },
    );
    await frame();
    expect(root.activeElement?.id).toBe('setting-mapping-1--field');
    expect(found('setting-mapping-1--field')).toEqual(["The property 'count' cannot hold this."]);
    expect(box('setting-mapping-1--field').getAttribute('aria-invalid')).toBe('true');
    // A field it can't hold stays shown while it is chosen, said so.
    expect(options(box<HTMLSelectElement>('setting-mapping-1--field'))).toEqual([
      'The answer to Age',
      "The answer to Email address (Count can't hold it)",
      'A value you write',
    ]);
    expect(found('setting-mapping')).toEqual(['The mapping is a list.']);
  });

  it("says a row's problem once the table is written, in the backoffice's words, without waiting for Update", async () => {
    checking.answer = () => [];
    const { root, type, pause, found } = await open({
      documentType: 'story',
      mapping: [
        { property: 'title', field: 'f-title' },
        { property: 'count', value: '12' },
      ],
    });
    await pause();
    expect(found('setting-mapping-1--value')).toEqual([]);
    checking.answer = () => [
      problem(
        '$.actions[0].settings.mapping[1].value',
        'content_value_cannot_hold',
        "The property 'count' cannot hold this value.",
      ),
    ];
    await type('setting-mapping-1--value', 'many');
    await pause();
    expect(found('setting-mapping-1--value')).toEqual([
      "The property can't hold this value. Write it as the box asks.",
    ]);
    expect(root.querySelector('#setting-mapping-1--value')?.getAttribute('aria-invalid')).toBe('true');
  });

  it('lands a Fix it on its row once the document type is read, unless the focus moved on meanwhile', async () => {
    checking.answer = () => [
      problem('$.actions[0].settings.mapping[1].field', 'setting_invalid', "The property 'count' cannot hold this."),
    ];
    const settings = {
      documentType: 'story',
      mapping: [
        { property: 'title', field: 'f-title' },
        { property: 'count', field: 'f-email' },
      ],
    };
    const landed: (string | undefined)[] = [];
    for (const moved of [false, true]) {
      document.body.replaceChildren();
      let answer: (type: ContentType) => void = () => undefined;
      const { element, root, box } = await open(settings, {
        focus: { kind: 'setting', path: 'mapping', row: { index: 1, part: 'field' } },
        documentType: () => new Promise<ContentType>((done) => (answer = done)),
      });
      await frame();
      // The table waits for its type, so the sidebar's name has the focus meanwhile.
      expect(root.activeElement?.id).toBe('name');
      if (moved) box<HTMLSelectElement>('setting-documentType').focus();
      answer(STORY);
      await settle(element);
      landed.push(root.activeElement?.id);
    }
    expect(landed).toEqual(['setting-mapping-1--field', 'setting-documentType']);
  });

  it('says while it reads the properties, and a document type the site no longer has or could not be read', async () => {
    let answer: (type: ContentType | null | undefined) => void = () => undefined;
    const reading = await open(
      { documentType: 'story' },
      { documentType: () => new Promise((done) => (answer = done)) },
    );
    expect(text(reading.root.getElementById('setting-mapping'))).toBe('Reading the properties\u2026');
    expect(reading.root.getElementById('setting-mapping')?.getAttribute('aria-busy')).toBe('true');
    answer(null);
    await settle(reading.element);
    expect(text(reading.root.getElementById('setting-mapping'))).toBe(
      'This site no longer has that document type. Choose another.',
    );
    document.body.replaceChildren();
    // A read that fails says so, and Try again reads the type again.
    let reads = 0;
    const failed = await open(
      { documentType: 'story' },
      { documentType: async () => (++reads === 1 ? undefined : STORY) },
    );
    expect(text(failed.root.getElementById('setting-mapping'))).toBe(
      "The document type's properties couldn't be read.",
    );
    must(
      [...failed.root.querySelectorAll<HTMLButtonElement>('button')].find((one) => text(one) === 'Try again'),
      'Try again',
    ).click();
    await settle(failed.element);
    expect([reads, failed.root.querySelectorAll('.map-row').length]).toEqual([2, 0]);
    expect(failed.root.getElementById('setting-mapping-add')).not.toBeNull();
  });

  it('says each setting in words for who may only read them', async () => {
    const first = await open(
      {
        documentType: 'story',
        mapping: [
          { property: 'title', field: 'f-title' },
          { property: 'featured', value: 'true' },
          { property: 'gone', value: 'x' },
        ],
      },
      { readOnly: true },
    );
    // The document type by its name, as the site's choices give it.
    expect(first.root.getElementById('setting-documentType')?.textContent).toBe('Story');
    // The item picked, as Umbraco's input names it, named for where it goes.
    document.body.replaceChildren();
    const within = await open(
      { documentType: 'story', parentSource: 'page', parent: '7bbb6953-972d-499e-a05a-c7cf1d45b1db' },
      { readOnly: true },
    );
    const group = must(within.root.querySelector('[role="group"]:has(umb-input-document)'), 'the item picked');
    expect([
      text(within.root.getElementById(group.getAttribute('aria-labelledby') ?? '')),
      text(within.root.getElementById('setting-parentSource')),
    ]).toEqual(['Only within this item', 'Under the page the form is sent from']);
    document.body.replaceChildren();
    const { root } = await open(
      {
        documentType: 'story',
        mapping: [
          { property: 'title', field: 'f-title' },
          { property: 'featured', value: 'true' },
          { property: 'gone', value: 'x' },
        ],
      },
      { readOnly: true },
    );
    expect(text(root.getElementById('setting-parentSource'))).toBe('At the root of the content tree');
    expect(
      (root.getElementById('setting-mapping')?.textContent ?? '')
        .trim()
        .split('\n')
        .map((one) => one.trim()),
    ).toEqual(['Title: the answer to Headline', 'Featured: Yes', 'gone: x']);
  });

  it('says the media folder and Stop on failure as they are for who may only read them', async () => {
    const one = action('a1', {
      type: 'content',
      name: 'Save the story',
      stopOnFailure: true,
      settings: { documentType: 'story', mediaFolder: '0a0b0c0d-0000-4000-8000-000000000001' },
    });
    const { element, root } = await sidebarOf({
      action: one,
      definition: form([one]),
      actionTypes: TYPES,
      choices: choicesOf(TYPES),
      documentType: async () => STORY,
      readOnly: true,
    });
    must(root.querySelector<HTMLButtonElement>('.more'), 'More options').click();
    await settle(element);
    // The folder as Umbraco's own input names it, and the switch in a word.
    expect(root.getElementById('setting-mediaFolder')?.localName).toBe('umb-input-media');
    expect(text(root.getElementById('stop-on-failure'))).toBe('On');
  });

  it('marks the document type and the state invalid where the form check says so, in its words', async () => {
    checking.answer = () => [problem('$.actions[0].settings.documentType', 'content_type_missing', 'Choose one.')];
    const { box, found } = await open({});
    expect(box('setting-documentType').getAttribute('aria-invalid')).toBe('true');
    expect(found('setting-documentType')).toEqual(['Choose the document type each entry becomes.']);
    document.body.replaceChildren();
    checking.answer = () => [problem('$.actions[0].settings.state', 'state_loop', "'Approve again' moves it back.")];
    const reject = action('a1', {
      type: 'setState',
      name: 'Reject again',
      trigger: 'approved',
      settings: { state: 'rejected' },
    });
    const approve = action('a2', {
      type: 'setState',
      name: 'Approve again',
      trigger: 'rejected',
      settings: { state: 'approved' },
    });
    const state = await sidebarOf({ action: reject, definition: form([reject, approve]) });
    expect(state.box('setting-state').getAttribute('aria-invalid')).toBe('true');
    expect(state.found('setting-state')).toEqual([
      '"Approve again" moves the entry back, so the two would move it back and forth for ever. Change one of them.',
    ]);
  });

  it("asks the state of Change the entry's state, and says that its rule picks the entries", async () => {
    const one = action('a1', { type: 'setState', name: 'Spam it', settings: { state: 'spam' } });
    const { element, root, box, keep, kept } = await sidebarOf({ action: one, definition: form([one]) });
    const state = box<HTMLSelectElement>('setting-state');
    expect([state.value, options(state)]).toEqual(['spam', ['Approved', 'Rejected', 'Spam']]);
    expect(text(root.querySelector('label[for="setting-state"]'))).toBe('Move the entry to');
    expect(text(root.getElementById('setting-state-help'))).toBe(
      "Approving or rejecting starts that trigger's actions for the entry.",
    );
    await select(element, state, 'rejected');
    await keep();
    expect(kept().settings).toEqual({ state: 'rejected' });
  });
});
