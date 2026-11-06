// The form's Actions view (ROADMAP 10.2a, ADR-0188): the triggers in their order, each with when it runs or the setting
// it needs and a link to it; a card for each action; every change one step to undo (a switch, a move, a copy, a
// delete with Undo in its toast, an action added or changed, one moved to another trigger at the end of its actions);
// the limit; read-only without `manageActions`; Fix it and `?action=` to an action; and a read that fails. A stand-in
// workspace drives the real undo history; the action types are the reviewed snapshot the server is held to.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbBasicState, UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ActionTypeResponseModel } from '../../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import type { UndoNotificationData } from '../../ui/undo-notification.element';
import type { FixTarget } from '../check/form-check';
import { UNCHECKED, type CheckState } from '../check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { DesignDefinition } from '../definition';
import type { Edit, EditorState } from '../editor-model';
import type { EditorView } from '../form-workspace.context';
import { EditorHistory } from '../history';
import { builtInTypes, englishLook } from '../preview/fixtures';
import type { ActionListElement } from './action-list.element';
import { actionsOf, type DesignAction } from './action-model';
import type { TestAnswer } from './action-test';
import type { ActionModalData } from './action.modal.token';

/** The built-in action types, as the snapshot holds what the server serves. */
const ACTION_TYPES = JSON.parse(
  readFileSync(
    join(__dirname, '..', '..', '..', '..', '..', '..', '..', '..', 'docs', 'api', 'action-types.json'),
    'utf8',
  ),
) as ActionTypeResponseModel[];

// What the server answers, or nothing while it fails; the sidebars opened, and what the one open answers.
const server = vi.hoisted(() => ({ fails: false, limit: 50 }));
const opened = vi.hoisted(() => ({ sidebars: [] as unknown[], sizes: [] as unknown[], answer: undefined as unknown }));
/** Umbraco's confirm dialog: whether the user confirms, and how often it was asked. */
const confirming = vi.hoisted(() => ({ yes: true, asked: 0, said: [] as unknown[] }));

/** The form's address, at its Actions view. */
const ADDRESS = '/umbraco/section/jfb-forms/workspace/jfb-form/edit/form1/view/actions';

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbConfirmModal: async (_host: unknown, data: { headline: string; content: { values: unknown[] } }) => {
    confirming.asked++;
    confirming.said.push([data.headline, data.content.values[0]]);
    if (!confirming.yes) throw new Error('declined');
  },
  umbOpenModal: async (
    _host: unknown,
    _token: unknown,
    args?: { data?: { closed?: () => void }; modal?: { size?: string } },
  ) => {
    opened.sidebars.push(args?.data);
    opened.sizes.push(args?.modal?.size);
    args?.data?.closed?.();
    return opened.answer;
  },
}));

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => ({ data: await request }),
}));

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  actionTypeList: async () => (server.fails ? undefined : { types: ACTION_TYPES }),
  formSettingsChoices: async () =>
    server.fails
      ? undefined
      : {
          limits: { actions: server.limit },
          defaults: { emailLayout: 'card' },
          emailLayouts: [
            { key: 'card', name: '#jfbEmailLayouts_card' },
            { key: 'brand', name: 'Brand mail' },
          ],
        },
}));

vi.mock('../preview/look', async (original) => ({
  ...(await original<typeof import('../preview/look')>()),
  readLook: async () => (server.fails ? undefined : englishLook),
}));

const { FormActionsViewElement } = await import('./form-actions.view');

const action = (key: string, more: Partial<DesignAction> = {}): DesignAction => ({
  key,
  type: 'email',
  name: `Action ${key}`,
  enabled: true,
  trigger: 'submitted',
  stopOnFailure: false,
  ...more,
});

const definitionWith = (
  actions: readonly DesignAction[],
  submission: Record<string, unknown> = {},
): DesignDefinition => ({
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
  settings: { submission: { approval: 'automatic', ...submission } },
  ...(actions.length > 0 ? { actions } : {}),
});

/** A workspace that holds the editor's state in the real history, as the form workspace does. */
class StandIn {
  readonly #history: EditorHistory;
  readonly #editor: UmbObjectState<EditorView>;
  readonly editor;
  readonly said: string[] = [];
  readonly fieldTypes = new UmbObjectState(builtInTypes).asObservable();
  readonly checks = new UmbObjectState<CheckState>(UNCHECKED);
  readonly check = this.checks.asObservable();
  readonly fixes = new UmbBasicState<FixTarget | null>(null);
  readonly fixRequest = this.fixes.asObservable();
  readonly unique = new UmbBasicState<string | undefined>('form1').asObservable();
  readonly states = new UmbBasicState<string | undefined>('draft');
  readonly state = this.states.asObservable();
  readonly forms = new UmbBasicState<{ permissions: string[] } | undefined>({
    permissions: ['design', 'manageActions'],
  });
  readonly form = this.forms.asObservable();
  host: Element | undefined;
  checkOf = async () => UNCHECKED;
  /** The email previews asked for: the action and the language. */
  readonly previewed: (readonly [string, string])[] = [];
  emailPreviewOf = async (_definition: DesignDefinition, key: string, culture: string) => {
    this.previewed.push([key, culture]);
    return undefined;
  };
  /** The tests asked for: the action, whether confirmed, and the definition. */
  readonly tested: (readonly [string, boolean, DesignDefinition])[] = [];
  testAnswer: TestAnswer = { kind: 'noAnswer' };
  testActionOf = async (definition: DesignDefinition, key: string, confirmed: boolean) => {
    this.tested.push([key, confirmed, definition]);
    return this.testAnswer;
  };

  constructor(definition: DesignDefinition) {
    this.#history = new EditorHistory({ definition, selection: null });
    this.#editor = new UmbObjectState<EditorView>(this.#view());
    this.editor = this.#editor.asObservable();
  }

  getHostElement = () => this.host;
  getEntityType = () => 'jfb-form';
  getUnique = () => 'form1';
  getLanguage = () => null;
  getEditorState = (): EditorState => this.#history.state;
  requestFix = (target: FixTarget) => this.fixes.setValue(target);
  takeFix = (makes: (target: FixTarget) => boolean): FixTarget | null => {
    const target = this.fixes.getValue();
    if (!target || !makes(target)) return null;
    this.fixes.setValue(null);
    return target;
  };

  edit(edit: Edit, said: string): Edit {
    const result = this.#history.apply(edit, said);
    if (result.ok && said) this.said.push(said);
    this.#editor.setValue(this.#view());
    return result;
  }

  undo(): string | undefined {
    const said = this.#history.undo();
    this.#editor.setValue(this.#view());
    return said;
  }

  redo(): string | undefined {
    const said = this.#history.redo();
    this.#editor.setValue(this.#view());
    return said;
  }

  #view(): EditorView {
    return { state: this.#history.state, canUndo: this.#history.canUndo, canRedo: this.#history.canRedo };
  }
}

/** The toasts the view shows. */
const toasts = {
  shown: [] as { color: string; data: Partial<UndoNotificationData> & { message: string } }[],
  closed: 0,
  peek(color: string, options: { data: Partial<UndoNotificationData> & { message: string } }) {
    this.shown.push({ color, data: options.data });
    return { close: () => void this.closed++ };
  },
};

async function viewOf(definition: DesignDefinition, permissions = ['design', 'manageActions']) {
  const workspace = new StandIn(definition);
  workspace.forms.setValue({ permissions });
  const host = testHost();
  workspace.host = host;
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
  host.provideContext(UMB_NOTIFICATION_CONTEXT, {
    peek: (color: string, options: Parameters<typeof toasts.peek>[1]) => toasts.peek(color, options),
    getHostElement: () => host,
  } as never);
  const view = new FormActionsViewElement();
  host.append(view);
  // The words, the types and the choices are read before the view draws.
  await vi.waitFor(() => expect(shadow(view).querySelector('.trigger, .empty, .failed')).not.toBeNull());
  await settle(view);
  const root = shadow(view);
  const sections = () => [...root.querySelectorAll<HTMLElement>('.trigger')];
  const list = (trigger: string) =>
    must(root.querySelector<ActionListElement>(`jfb-action-list[data-trigger="${trigger}"]`), `${trigger}'s list`);
  const cards = (trigger: string) => [...shadow(list(trigger)).querySelectorAll<HTMLElement>('.card')];
  const names = (trigger: string) => cards(trigger).map((card) => text(card.querySelector('.name')));
  const actions = () => actionsOf(workspace.getEditorState().definition);
  // The view's own live region; each list has its own in its shadow root.
  const said = () => text(root.querySelector('[aria-live="polite"]'));
  return { workspace, view, root, sections, list, cards, names, actions, said };
}

beforeAll(registerEnglish);

beforeEach(() => {
  server.fails = false;
  server.limit = 50;
  opened.sidebars = [];
  opened.sizes = [];
  opened.answer = undefined;
  toasts.shown = [];
  toasts.closed = 0;
  confirming.yes = true;
  confirming.asked = 0;
  confirming.said = [];
  history.replaceState(null, '', ADDRESS);
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the Actions view', () => {
  it('shows the triggers in their order, when each runs, and the setting a trigger needs, with a link to it', async () => {
    const { root, sections } = await viewOf(definitionWith([action('a')]));
    expect(sections().map((section) => text(section.querySelector('h2')))).toEqual([
      'Submitted',
      'Approved',
      'Rejected',
      'Verified',
    ]);
    expect(sections().map((section) => text(section.querySelector('.when')))).toEqual([
      'when an entry comes in',
      'as each entry comes in, because this form has no manual approval',
      '',
      '',
    ]);
    expect(text(sections()[1]?.querySelector('.none'))).toBe('No actions here yet.');
    expect(text(sections()[2]?.querySelector('.off'))).toBe(
      'Runs when someone rejects an entry. It needs manual approval: turn it on in Settings',
    );
    expect(text(sections()[3]?.querySelector('.off'))).toBe(
      'Runs when a visitor confirms their address. It needs email verification: turn it on in Settings',
    );
    expect(root.querySelector('#add')?.getAttribute('label')).toBe('Add action');
  });

  it("leads a trigger's link to its setting in the Settings view", async () => {
    const { workspace, sections } = await viewOf(definitionWith([action('a')]));
    must(sections()[3]?.querySelector<HTMLAnchorElement>('.off a'), 'the link').click();
    expect(workspace.fixes.getValue()).toEqual({
      kind: 'view',
      view: 'settings',
      path: '$.settings.submission.verification',
    });
    expect(window.location.pathname).toBe(ADDRESS.replace(/actions$/, 'settings'));
  });

  it('says a trigger whose setting is on and that holds nothing, and one that holds actions that do not run', async () => {
    const manual = definitionWith([action('r', { trigger: 'rejected' })], {
      approval: 'manual',
      verification: { emailField: 'mail' },
    });
    const { sections, names } = await viewOf(manual);
    expect(text(sections()[1]?.querySelector('.when'))).toBe('when someone approves an entry');
    expect(text(sections()[3]?.querySelector('.none'))).toBe('No actions here yet.');
    expect(names('rejected')).toEqual(['Action r']);
    document.body.replaceChildren();
    const held = await viewOf(definitionWith([action('r', { trigger: 'rejected' })]));
    expect(text(held.sections()[2]?.querySelector('.off'))).toBe(
      "These don't run until the form has manual approval: turn it on in Settings",
    );
    expect(held.names('rejected')).toEqual(['Action r']);
  });

  it('starts empty with one sentence, Add action its only button', async () => {
    const { root, sections } = await viewOf(definitionWith([]));
    expect(sections()).toEqual([]);
    expect(text(root.querySelector('.empty'))).toBe(
      'No actions yet. Most forms email the team when an entry comes in, and Add action starts with that.',
    );
  });

  it("names each card's action by what it does, its rule in words, and a site's own type by its type", async () => {
    const { cards } = await viewOf(
      definitionWith([
        action('a', { name: 'Email the team', settings: { to: ['sales@example.com', 'support@example.com'] } }),
        action('b', { name: null, settings: { toField: 'mail' } }),
        action('c', {
          type: 'webhook',
          name: 'Send to the CRM',
          settings: { url: 'https://crm.example.com/hooks' },
          rules: [
            {
              key: 'r',
              effect: 'run',
              enabled: true,
              match: 'all',
              conditions: [{ field: 'topic', op: 'equals', value: 'Sales' }],
            },
          ],
        }),
        action('d', { type: 'crm', name: 'Our CRM' }),
        action('e', { type: 'crm', name: null }),
      ]),
    );
    const lines = cards('submitted').map((card) => [
      text(card.querySelector('.name')),
      text(card.querySelector('.line')),
      text(card.querySelector('.rule')),
    ]);
    expect(lines).toEqual([
      ['Email the team', 'To sales@example.com and support@example.com', ''],
      ['Email', 'To the answer of Email address', ''],
      ['Send to the CRM', 'Webhook to crm.example.com', 'Runs when Topic is "Sales"'],
      ['Our CRM', 'crm', ''],
      // Without a name of its own, its type is its name, said once.
      ['crm', '', ''],
    ]);
  });

  it("counts the check's errors and warnings in an action on its card, and not its hints", async () => {
    const definition = definitionWith([action('a'), action('b'), action('gone')]);
    const { workspace, view, cards } = await viewOf(definitionWith([action('a'), action('b')]));
    const issue = (path: string, severity: 'error' | 'warning' | 'hint') => ({
      severity,
      code: 'x',
      path,
      message: 'x',
    });
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [
        issue('$.actions[0].settings.to', 'warning'),
        issue('$.actions[0].settings.replyToField', 'hint'),
        issue('$.actions[1].name', 'error'),
        issue('$.actions[1].settings.subject', 'warning'),
        issue('$.actions[2].name', 'error'),
      ],
    });
    await settle(view);
    const flags = cards('submitted').map((card) => {
      const flag = card.querySelector<HTMLElement>('.flag');
      return [text(flag), flag?.dataset['severity'] ?? null];
    });
    expect(flags).toEqual([
      ['1 problem', 'warning'],
      ['2 problems', 'error'],
    ]);
  });

  it('switches an action off as one step to undo, its state said by the switch alone', async () => {
    const { workspace, view, list, actions, said } = await viewOf(definitionWith([action('a'), action('b')]));
    list('submitted').toggle('b', false);
    await settle(view);
    expect(actions().map((one) => one.enabled)).toEqual([true, false]);
    expect(workspace.said).toEqual(['Switched an action on or off']);
    expect(said()).toBe('');
    // Ctrl+Z outside a text box takes it back.
    view.dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, composed: true }));
    await settle(view);
    expect(actions().map((one) => one.enabled)).toEqual([true, true]);
  });

  it("moves an action within its trigger, the other triggers' keeping their places", async () => {
    const { view, list, actions } = await viewOf(
      definitionWith([action('s1'), action('a1', { trigger: 'approved' }), action('s2')]),
    );
    list('submitted').move(1, 0);
    await settle(view);
    expect(actions().map((one) => one.key)).toEqual(['s2', 'a1', 's1']);
  });

  it('copies an action right after it, with the focus on the copy', async () => {
    const { view, list, names, actions, said } = await viewOf(definitionWith([action('a'), action('b')]));
    list('submitted').request('a', 'duplicate');
    await settle(view);
    await settle(list('submitted'));
    expect(names('submitted')).toEqual(['Action a', 'Action a (copy)', 'Action b']);
    expect(actions()[1]?.key).not.toBe('a');
    expect(said()).toBe('Duplicated. Action a (copy) is right after it.');
    expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe(actions()[1]?.key);
  });

  it('deletes an action with Undo in its toast, which brings it back with the focus on it', async () => {
    const { view, list, names } = await viewOf(definitionWith([action('a'), action('b')]));
    list('submitted').request('a', 'delete');
    await settle(view);
    expect(names('submitted')).toEqual(['Action b']);
    const toast = must(toasts.shown.at(-1), 'the toast');
    expect(toast.data.message).toBe('Action a deleted.');
    await toast.data.undo?.();
    await settle(view);
    expect(names('submitted')).toEqual(['Action a', 'Action b']);
    await vi.waitFor(() =>
      expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe('a'),
    );
  });

  it('closes the toast with Undo once another change is made, as its Undo would take that one back', async () => {
    const { view, list } = await viewOf(definitionWith([action('a'), action('b')]));
    list('submitted').request('a', 'delete');
    await settle(view);
    expect(toasts.closed).toBe(0);
    list('submitted').toggle('b', false);
    await settle(view);
    expect(toasts.closed).toBe(1);
  });

  it('keeps the focus on the card it was on through Undo, or puts it on Add action where Undo takes the card away', async () => {
    const { view, list, names } = await viewOf(definitionWith([action('a')]));
    opened.answer = { action: action('n'), at: null };
    must(shadow(view).querySelector<HTMLElement>('#add'), 'Add action').click();
    await vi.waitFor(() =>
      expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe('n'),
    );
    const press = (key: string) =>
      shadow(list('submitted')).activeElement?.dispatchEvent(
        new KeyboardEvent('keydown', { key, ctrlKey: true, bubbles: true, composed: true }),
      );
    press('z');
    await vi.waitFor(() => expect(names('submitted')).toEqual(['Action a']));
    await vi.waitFor(() => expect(shadow(view).activeElement?.id).toBe('add'));
  });

  it("adds an action from its sidebar at the end of its trigger's, said, and the focus on it", async () => {
    const { workspace, view, list, names, said } = await viewOf(
      definitionWith([action('a'), action('x', { trigger: 'approved' })]),
    );
    opened.answer = { action: action('n', { name: 'Email the team' }) };
    must(shadow(view).querySelector<HTMLElement>('#add'), 'Add action').click();
    await vi.waitFor(() => expect(names('submitted')).toEqual(['Action a', 'Email the team']));
    const data = opened.sidebars[0] as ActionModalData;
    expect(data.action).toBeNull();
    expect(data.readOnly).toBe(false);
    expect(data.choices.map((choice) => choice.name)).toEqual([
      '#jfbActionPresets_emailNotifyTeam',
      '#jfbActionPresets_emailConfirmSender',
      '#jfbActionTypes_webhook',
      '#jfbActionPresets_webhookSlack',
      '#jfbActionPresets_webhookTeams',
      '#jfbActionTypes_setState',
      '#jfbActionTypes_content',
    ]);
    expect(data.needs).toEqual({ submitted: null, approved: null, rejected: 'approval', verified: 'verification' });
    expect(workspace.said).toEqual(['Added an action']);
    expect(said()).toBe('Added. Email the team runs when an entry comes in.');
    await vi.waitFor(() =>
      expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe('n'),
    );
    // On a published form, a toast says when it starts to run.
    workspace.states.setValue('published');
    opened.answer = { action: action('m', { name: 'Tell sales' }) };
    must(shadow(view).querySelector<HTMLElement>('#add'), 'Add action').click();
    await vi.waitFor(() =>
      expect(toasts.shown.at(-1)?.data.message).toBe('Tell sales added. It runs for new entries once you publish.'),
    );
  });

  it('puts an action at the place chosen in its sidebar, and says where it went', async () => {
    const { view, list, names, said } = await viewOf(definitionWith([action('a'), action('b'), action('c')]));
    opened.answer = { action: action('c', { name: 'Tell sales' }), at: 0 };
    list('submitted').open('c');
    await vi.waitFor(() => expect(names('submitted')).toEqual(['Tell sales', 'Action a', 'Action b']));
    expect(said()).toBe('Moved. Tell sales: position 1 of 3.');
    await vi.waitFor(() =>
      expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe('c'),
    );
    await settle(view);
  });

  it("puts an action whose trigger changed at the end of the new trigger's, as one step to undo", async () => {
    const { workspace, view, list, names, said } = await viewOf(
      definitionWith([action('a'), action('x', { trigger: 'approved' }), action('b')]),
    );
    opened.answer = { action: action('a', { trigger: 'approved' }) };
    list('submitted').request('a', 'moveTrigger');
    await vi.waitFor(() => expect(names('approved')).toEqual(['Action x', 'Action a']));
    expect((opened.sidebars[0] as ActionModalData).focus).toEqual({ kind: 'trigger' });
    expect(names('submitted')).toEqual(['Action b']);
    expect(workspace.said).toEqual(['Changed an action']);
    expect(said()).toBe('Moved. Action a: position 2 of 2.');
    await settle(view);
  });

  it('says the limit in place of Add action', async () => {
    server.limit = 2;
    const { root, view, list, names, workspace } = await viewOf(definitionWith([action('a'), action('b')]));
    expect(root.querySelector('#add')).toBeNull();
    expect(text(root.querySelector('.note'))).toBe('A form has at most 2 actions.');
    expect(list('submitted').full).toBe(true);
    // A Duplicate asked anyway makes no copy.
    list('submitted').request('a', 'duplicate');
    await settle(view);
    expect(names('submitted')).toEqual(['Action a', 'Action b']);
    // With no Add action to press, a Fix it of an action gone puts the focus on the first card.
    workspace.requestFix({ kind: 'view', view: 'actions', path: '$.actions[5].name', key: 'gone' });
    await vi.waitFor(() =>
      expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe('a'),
    );
  });

  it("opens Umbraco's large sidebar for an email, with the site's layouts and its preview, and the medium one for another type", async () => {
    const { workspace, view, list } = await viewOf(
      definitionWith([action('a'), action('b', { type: 'webhook', settings: { url: 'https://x.example.com' } })]),
    );
    must(shadow(view).querySelector<HTMLElement>('#add'), 'Add action').click();
    await vi.waitFor(() => expect(opened.sidebars).toHaveLength(1));
    await settle(view);
    list('submitted').open('a');
    await vi.waitFor(() => expect(opened.sidebars).toHaveLength(2));
    await settle(view);
    list('submitted').open('b');
    await vi.waitFor(() => expect(opened.sidebars).toHaveLength(3));
    expect(opened.sizes).toEqual(['large', 'large', 'medium']);
    const data = opened.sidebars[1] as ActionModalData;
    expect([data.layouts, data.defaultLayout]).toEqual([
      [
        { key: 'card', name: '#jfbEmailLayouts_card' },
        { key: 'brand', name: 'Brand mail' },
      ],
      'card',
    ]);
    await data.preview?.(data.definition, 'a', 'de-DE');
    expect(workspace.previewed).toEqual([['a', 'de-DE']]);
  });

  it('tests an action from its card as the draft holds it, and says what it came to in a toast (ADR-0190)', async () => {
    const { workspace, list } = await viewOf(definitionWith([action('a')]));
    workspace.testAnswer = {
      kind: 'done',
      result: { outcome: 'sent', code: 'sent_to_you', recipient: 'ana@example.com', preview: [] },
    };
    const started = vi.fn();
    await list('submitted').test?.('a', started);
    expect(workspace.tested.map(([key, confirmed]) => [key, confirmed])).toEqual([['a', false]]);
    expect(workspace.tested[0]?.[2]).toEqual(workspace.getEditorState().definition);
    expect([confirming.asked, started.mock.calls.length]).toEqual([0, 1]);
    // Led by the action's name, so toasts of several tests tell their actions apart.
    expect(toasts.shown.at(-1)).toEqual({
      color: 'positive',
      data: { message: 'Action a: Test email sent to you (ana@example.com).' },
    });
  });

  it('asks before a test that reaches what the action is set up to reach, and sends nothing when declined', async () => {
    const { workspace, list } = await viewOf(
      definitionWith([action('b', { type: 'webhook', settings: { url: 'https://x.example.com' } })]),
    );
    confirming.yes = false;
    const started = vi.fn();
    await list('submitted').test?.('b', started);
    // Declined: nothing asked of the server, nothing said, and it never started.
    expect([confirming.asked, workspace.tested.length, toasts.shown.length, started.mock.calls.length]).toEqual([
      1, 0, 0, 0,
    ]);
    // It names where the test goes, as the card says it.
    expect(confirming.said).toEqual([
      ['Send a test?', 'Webhook to x.example.com. A sample entry goes there as a real one would, marked as a test.'],
    ]);
    confirming.yes = true;
    workspace.testAnswer = {
      kind: 'done',
      result: {
        outcome: 'failed',
        code: 'http_503',
        message: 'The webhook was not delivered (http_503).',
        preview: [],
      },
    };
    await list('submitted').test?.('b', started);
    expect(workspace.tested.map(([key, confirmed]) => [key, confirmed])).toEqual([['b', true]]);
    expect(started.mock.calls.length).toBe(1);
    expect(toasts.shown.at(-1)).toEqual({
      color: 'danger',
      data: { message: 'Action b: The webhook was not delivered (http_503).' },
    });
  });

  it("hands an action's sidebar its Test, which tests the action as the sidebar holds it", async () => {
    const { workspace, list, view } = await viewOf(definitionWith([action('a')]));
    list('submitted').open('a');
    await vi.waitFor(() => expect(opened.sidebars).toHaveLength(1));
    await settle(view);
    const data = opened.sidebars[0] as ActionModalData;
    const held = { ...data.definition, actions: [{ ...action('a'), name: 'Held' }] };
    await data.test?.(held, 'a', () => undefined);
    expect(workspace.tested.map(([key, , definition]) => [key, definition])).toEqual([['a', held]]);
  });

  it('is read-only without manageActions: one line says why, no Add action, and a card opens its settings as they are', async () => {
    const { root, list, view, actions, workspace } = await viewOf(definitionWith([action('a')]), ['design']);
    expect(root.querySelector('#add')).toBeNull();
    expect(text(root.querySelector('.read-only'))).toBe(
      'Changing these actions needs the Manage actions permission for this form. An administrator can grant it.',
    );
    expect(list('submitted').readOnly).toBe(true);
    list('submitted').open('a');
    await vi.waitFor(() => expect((opened.sidebars[0] as ActionModalData | undefined)?.readOnly).toBe(true));
    // A change asked anyway changes nothing.
    list('submitted').toggle('a', false);
    await settle(view);
    expect([actions()[0]?.enabled, workspace.said]).toEqual([true, []]);
  });

  it('leads a Fix it to its action: the card, and the sidebar on the box the path names', async () => {
    const { workspace, view, list } = await viewOf(
      definitionWith([action('a'), action('b', { type: 'webhook', settings: { url: 'http://x' } })]),
    );
    workspace.requestFix({ kind: 'view', view: 'actions', path: '$.actions[1].settings.url', key: 'b' });
    await vi.waitFor(() => expect(opened.sidebars).toHaveLength(1));
    const data = opened.sidebars[0] as ActionModalData;
    expect(data.action?.key).toBe('b');
    expect(data.focus).toEqual({ kind: 'setting', path: 'url' });
    expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe('b');
    // A link's ?action=: the card, with the focus on it, and no sidebar.
    workspace.requestFix({ kind: 'view', view: 'actions', path: '$.actions', key: 'a' });
    await settle(view);
    await vi.waitFor(() =>
      expect(shadow(list('submitted')).activeElement?.closest('.card')?.getAttribute('data-key')).toBe('a'),
    );
    expect(opened.sidebars).toHaveLength(1);
    // An action gone since leads to Add action.
    workspace.requestFix({ kind: 'view', view: 'actions', path: '$.actions[5].name', key: 'gone' });
    await vi.waitFor(() => expect(shadow(view).activeElement?.id).toBe('add'));
  });

  it('says a read that fails, and reads again with Try again', async () => {
    server.fails = true;
    const { root, view } = await viewOf(definitionWith([action('a')]));
    expect(text(root.querySelector('.failed p'))).toBe("The actions couldn't be read. Try again in a moment.");
    server.fails = false;
    must(root.querySelector<HTMLElement>('.failed uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(shadow(view).querySelector('.trigger')).not.toBeNull());
  });
});
