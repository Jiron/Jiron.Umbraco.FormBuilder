// After sending on the Design view (ROADMAP 10.1b and 22.1, ADR-0185, ADR-0256): the form's outcomes in order, the
// sentence on their order where there are several, each changed, added, deleted with Undo or moved as one step, the
// focus back on the list, a Fix it opening an outcome's sidebar on its setting, the form check's problems on the rows
// and the list, an outcome without the language shown marked, the toast on a published form, a heading of its own
// where it lies under the sheet, a read of the site's bounds that fails, and the focus after Undo. A stand-in workspace
// drives the real undo history.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbBasicState, UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FormSettingsLimitsResponseModel } from '../../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import type { UndoNotificationData } from '../../ui/undo-notification.element';
import type { CheckIssue } from '../check/form-check';
import { UNCHECKED, type CheckState } from '../check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { DesignDefinition } from '../definition';
import type { Edit, EditorState } from '../editor-model';
import type { EditorView } from '../form-workspace.context';
import { EditorHistory } from '../history';
import { builtInTypes, englishLook } from '../preview/fixtures';
import type { CanvasLook } from '../preview/look';
import type { OutcomeListElement } from './outcome-list.element';
import type { DesignOutcome } from './outcome-model';
import type { OutcomeModalData } from './outcome.modal.token';

// The outcome sidebars opened, and what the one open answers.
const opened = vi.hoisted(() => ({ sidebars: [] as unknown[], answer: undefined as unknown }));
// Whether the site's bounds can be read, and how often they were asked for.
const server = vi.hoisted(() => ({ fails: false, asked: 0 }));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: { closed?: () => void } }) => {
    opened.sidebars.push(args?.data);
    args?.data?.closed?.();
    return opened.answer;
  },
}));

const LIMITS: FormSettingsLimitsResponseModel = {
  lineLength: 500,
  htmlLength: 50000,
  nameLength: 64,
  verificationDays: 90,
  resumeDays: 365,
  retentionDays: 3650,
  referenceDigits: 10,
  outcomes: 50,
  actions: 50,
};

const { FormOutcomesElement, changedOutcome } = await import('./form-outcomes.element');

const BASE: DesignDefinition = {
  pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [{ key: 'name', type: 'text', alias: 'name' }] }] }],
};

const message: DesignOutcome = { key: 'o1', kind: 'message', message: { '': '<p>Thanks!</p>' }, showAnswers: false };
const press: DesignOutcome = {
  key: 'o2',
  kind: 'redirect',
  redirect: { url: '/press', delaySeconds: 0 },
  rules: [{ key: 'r1', effect: 'use', enabled: true, match: 'all', conditions: [{ field: 'name', op: 'notEmpty' }] }],
};
const withOutcomes = (outcomes: readonly DesignOutcome[]): DesignDefinition => ({ ...BASE, outcomes });

const GERMAN: CanvasLook = {
  ...englishLook,
  culture: 'de-DE',
  languages: [
    { culture: 'en-US', name: 'English (United States)', isDefault: true },
    { culture: 'de-DE', name: 'Deutsch (Deutschland)', isDefault: false },
  ],
};

/** A workspace that holds the editor's state in the real history, as the form workspace does. */
class StandIn {
  readonly #history: EditorHistory;
  readonly #editor: UmbObjectState<EditorView>;
  readonly editor;
  readonly said: string[] = [];
  readonly fieldTypes = new UmbObjectState(builtInTypes).asObservable();
  readonly checks = new UmbObjectState<CheckState>(UNCHECKED);
  readonly check = this.checks.asObservable();
  readonly unique = new UmbBasicState<string | undefined>('form1').asObservable();
  readonly states = new UmbBasicState<string | undefined>('draft');
  readonly state = this.states.asObservable();
  host: Element | undefined;
  checkOf = async () => [];
  previewOf = async () => undefined;
  /** The form's setting choices, as the workspace reads them once for the open form. */
  choicesOf: (form: string) => Promise<{ limits: FormSettingsLimitsResponseModel } | undefined> = async () => {
    server.asked++;
    return server.fails ? undefined : { limits: LIMITS };
  };

  constructor(definition: DesignDefinition) {
    this.#history = new EditorHistory({ definition, selection: null });
    this.#editor = new UmbObjectState<EditorView>(this.#view());
    this.editor = this.#editor.asObservable();
  }

  getHostElement = () => this.host;
  getEntityType = () => 'jfb-form';
  getEditorState = (): EditorState => this.#history.state;

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

  #view(): EditorView {
    return { state: this.#history.state, canUndo: this.#history.canUndo, canRedo: this.#history.canRedo };
  }
}

/** The toasts shown. */
const toasts = {
  shown: [] as { data: Partial<UndoNotificationData> & { message: string } }[],
};

async function outcomesOf(definition: DesignDefinition, look: CanvasLook = englishLook, headed = false) {
  const workspace = new StandIn(definition);
  const host = testHost();
  workspace.host = host;
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
  host.provideContext(UMB_NOTIFICATION_CONTEXT, {
    peek: (_color: string, options: { data: Partial<UndoNotificationData> & { message: string } }) => {
      toasts.shown.push({ data: options.data });
      return { close: () => undefined };
    },
    getHostElement: () => host,
  } as never);
  const element = new FormOutcomesElement();
  element.look = look;
  element.headed = headed;
  host.append(element);
  await settle(element);
  const root = shadow(element);
  const list = () => must(root.querySelector<OutcomeListElement>('jfb-outcome-list'), 'the list');
  const focused = () => shadow(list()).activeElement?.closest('.outcome-row')?.getAttribute('data-key');
  const held = () => (workspace.getEditorState().definition['outcomes'] ?? []) as DesignOutcome[];
  return { workspace, element, root, list, focused, held };
}

const issue = (path: string, code: string, severity: CheckIssue['severity'] = 'error'): CheckIssue => ({
  severity,
  code,
  path,
  message: 'In English.',
});

beforeAll(registerEnglish);

beforeEach(() => {
  opened.sidebars = [];
  opened.answer = undefined;
  toasts.shown = [];
  server.fails = false;
  server.asked = 0;
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('After sending', () => {
  it("lists the draft's outcomes with the default message, and the sentence on their order where there are several", async () => {
    const { root, list } = await outcomesOf(withOutcomes([press, message]));
    expect(list().outcomes.map((one) => one.key)).toEqual(['o2', 'o1']);
    expect([list().defaultMessage, list().limit, list().culture]).toEqual([
      'Thank you. We have received your message.',
      50,
      'en-US',
    ]);
    expect(text(root.querySelector('.note'))).toBe('Visitors get the first outcome whose rule matches.');
    document.body.replaceChildren();
    const one = await outcomesOf(withOutcomes([message]));
    expect(one.root.querySelector('.note')).toBeNull();
  });

  it('has a heading of its own only where it lies under the sheet', async () => {
    const column = await outcomesOf(withOutcomes([message]));
    expect(column.root.querySelector('h2')).toBeNull();
    document.body.replaceChildren();
    const under = await outcomesOf(withOutcomes([message]), englishLook, true);
    expect(text(under.root.querySelector('h2'))).toBe('After sending');
    expect(under.element.hasAttribute('headed')).toBe(true);
  });

  it('changes an outcome in its sidebar, one step to undo', async () => {
    const { element, list, workspace, held } = await outcomesOf(withOutcomes([message]));
    opened.answer = { outcome: { ...message, showAnswers: true }, at: 0 };
    list().open('o1');
    await settle(element);
    const data = opened.sidebars[0] as OutcomeModalData;
    expect([data.outcome?.key, data.number, data.count, data.culture, data.form, data.htmlLength]).toEqual([
      'o1',
      1,
      1,
      'en-US',
      'form1',
      50000,
    ]);
    expect(held()).toEqual([{ ...message, showAnswers: true }]);
    expect(workspace.said).toEqual(['Changed an outcome']);
    workspace.undo();
    expect(held()).toEqual([message]);
    // A draft not yet published says nothing more.
    expect(toasts.shown).toEqual([]);
  });

  it('adds an outcome where its sidebar placed it, and says where it went', async () => {
    const { element, list, workspace, held } = await outcomesOf(withOutcomes([message]));
    opened.answer = { outcome: { ...press, key: 'o3' }, at: 0 };
    list().open(null);
    await settle(element);
    expect((opened.sidebars[0] as OutcomeModalData).outcome).toBeNull();
    expect(held().map((one) => one.key)).toEqual(['o3', 'o1']);
    expect(workspace.said).toEqual(['Added an outcome']);
    expect(text(shadow(list()).querySelector('[role="status"]'))).toBe('Added. Redirect to /press: position 1 of 2.');
  });

  it('moves an outcome to the place its sidebar chose, and says where it went', async () => {
    const { element, list, held } = await outcomesOf(withOutcomes([message, press]));
    opened.answer = { outcome: message, at: 1 };
    list().open('o1');
    await settle(element);
    expect(held().map((one) => one.key)).toEqual(['o2', 'o1']);
    expect(text(shadow(list()).querySelector('[role="status"]'))).toBe('Moved. Message: "Thanks!": position 2 of 2.');
  });

  it('says nothing for an Update that changes nothing, even on a published form', async () => {
    const { element, list, workspace, focused } = await outcomesOf(withOutcomes([message]));
    workspace.states.setValue('published');
    opened.answer = { outcome: message, at: 0 };
    list().open('o1');
    await settle(element);
    expect([workspace.said, toasts.shown]).toEqual([[], []]);
    expect(focused()).toBe('o1');
  });

  it('gives the focus back to the list: the outcome, a new one, the one after a deleted one', async () => {
    const { element, list, focused } = await outcomesOf(withOutcomes([press, message]));
    // Cancel.
    list().open('o1');
    await settle(element);
    expect(focused()).toBe('o1');
    opened.answer = { outcome: { key: 'o3', kind: 'message' }, at: 2 };
    list().open(null);
    await settle(element);
    expect(focused()).toBe('o3');
    opened.answer = { deleted: true };
    list().open('o2');
    await settle(element);
    expect(focused()).toBe('o1');
  });

  it('opens no new outcome once the form has the most it can have, nor a second sidebar over one', async () => {
    const outcomes = Array.from({ length: 50 }, (_, at): DesignOutcome => ({ key: `o${at}`, kind: 'message' }));
    const full = await outcomesOf(withOutcomes(outcomes));
    full.list().open(null);
    await settle(full.element);
    expect(opened.sidebars).toEqual([]);
    document.body.replaceChildren();
    const { element, list } = await outcomesOf(withOutcomes([message]));
    list().open('o1');
    list().open('o1');
    await settle(element);
    expect(opened.sidebars).toHaveLength(1);
  });

  it('deletes an outcome at once, Undo in the toast bringing it back, said and focused', async () => {
    const { element, list, held, focused } = await outcomesOf(withOutcomes([press, message]));
    opened.answer = { deleted: true };
    list().open('o2');
    await settle(element);
    expect(held().map((one) => one.key)).toEqual(['o1']);
    const [toast] = toasts.shown;
    expect(toast?.data.message).toBe('Outcome deleted.');
    await toast?.data.undo?.();
    expect(held().map((one) => one.key)).toEqual(['o2', 'o1']);
    await settle(element);
    // The toast is gone with its Undo: the focus goes to the outcome back in the list.
    expect(focused()).toBe('o2');
    expect(text(shadow(list()).querySelector('[role="status"]'))).toBe('Undone: Deleted an outcome');
  });

  it("does nothing on a toast's Undo once another change came after it", async () => {
    const { element, list, held } = await outcomesOf(withOutcomes([press, message]));
    opened.answer = { deleted: true };
    list().open('o2');
    await settle(element);
    opened.answer = { outcome: { ...message, showAnswers: true }, at: 0 };
    list().open('o1');
    await settle(element);
    await toasts.shown[0]?.data.undo?.();
    expect(held()).toEqual([{ ...message, showAnswers: true }]);
  });

  it('moves an outcome as the list asks, one step to undo', async () => {
    const { list, workspace, held } = await outcomesOf(withOutcomes([press, message]));
    list().move(0, 1);
    expect(held().map((one) => one.key)).toEqual(['o1', 'o2']);
    expect(workspace.said).toEqual(['Moved an outcome']);
  });

  it('says on a published form that visitors get a change once it is published', async () => {
    const { element, list, workspace } = await outcomesOf(withOutcomes([message]));
    workspace.states.setValue('published');
    opened.answer = { outcome: { ...message, showAnswers: true }, at: 0 };
    list().open('o1');
    await settle(element);
    expect(toasts.shown.map((one) => one.data.message)).toEqual(['Updated. Visitors get it once you publish.']);
  });
});

describe("After sending's Fix it", () => {
  it("opens an outcome's sidebar on its setting, by the key the check found it under", async () => {
    const { element } = await outcomesOf(withOutcomes([message, press]));
    await element.fix('o2', ['outcomes', 0, 'redirect', 'url'], false);
    // The key leads, as the outcomes may have moved since the check.
    const data = opened.sidebars[0] as OutcomeModalData;
    expect([data.outcome?.key, data.focus]).toEqual(['o2', 'address']);
  });

  it('finds the outcome by its place without a key, and waits for the router when handed on (ADR-0227)', async () => {
    const { element } = await outcomesOf(withOutcomes([message, press]));
    const fixed = element.fix(undefined, ['outcomes', 1, 'rules', 0], true);
    await settle(element);
    expect(opened.sidebars).toHaveLength(0);
    await fixed;
    const data = opened.sidebars[0] as OutcomeModalData;
    expect([data.outcome?.key, data.focus]).toEqual(['o2', 'rule']);
  });

  it('puts the focus on Add outcome for a path at the list, or an outcome gone since', async () => {
    const { element, list } = await outcomesOf(withOutcomes([message]));
    await element.fix(undefined, ['outcomes'], false);
    expect(shadow(list()).activeElement?.classList.contains('add')).toBe(true);
    (shadow(list()).activeElement as HTMLElement).blur();
    await element.fix('gone', ['outcomes', 3, 'message'], false);
    expect(shadow(list()).activeElement?.classList.contains('add')).toBe(true);
    expect(opened.sidebars).toEqual([]);
  });

  it('waits for the bounds to be read before it leads anywhere', async () => {
    const workspace = new StandIn(withOutcomes([message]));
    const host = testHost();
    workspace.host = host;
    host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
    const element = new FormOutcomesElement();
    element.look = englishLook;
    host.append(element);
    await element.fix('o1', ['outcomes', 0, 'message'], false);
    expect((opened.sidebars[0] as OutcomeModalData).focus).toBe('message');
  });
});

describe("After sending's problems", () => {
  it("counts an outcome's problems on its row, no suggestion, in the colour of the heaviest", async () => {
    const definition = withOutcomes([message, press]);
    const { element, list, workspace } = await outcomesOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [
        issue('$.outcomes[1].redirect.url', 'invalid_redirect'),
        issue('$.outcomes[0].message', 'x', 'hint'),
        issue('$.outcomes[0].message', 'y', 'warning'),
        issue('$.settings.privacy.ip', 'z'),
      ],
    });
    await settle(element);
    expect([...list().flags]).toEqual([
      ['o2', { count: 1, error: true }],
      ['o1', { count: 1, error: false }],
    ]);
    expect(list().problems).toEqual([]);
  });

  it("shows the list's own problems in the list, above Add outcome, a suggestion too, its weight in words", async () => {
    const definition = withOutcomes([message]);
    const { element, list, workspace } = await outcomesOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [issue('$.outcomes', 'too_many_outcomes'), issue('$.outcomes', 'x', 'hint')],
    });
    await settle(element);
    expect(list().problems.map((one) => [one.text, one.severity])).toEqual([
      ['In English.', 'error'],
      ['In English.', 'hint'],
    ]);
    expect(list().weight('warning')).toBe('Warning:');
  });
});

describe('the language After sending is shown in', () => {
  it("marks an outcome without the designer's language, and nothing in the default language", async () => {
    const german = await outcomesOf(withOutcomes([message]), GERMAN);
    expect(german.list().untranslated).toBe('No German text yet');
    expect(text(shadow(german.list()).querySelector('.row-line'))).toContain('No German text yet');
    document.body.replaceChildren();
    const english = await outcomesOf(withOutcomes([message]), { ...GERMAN, culture: 'en-US' });
    expect(english.list().untranslated).toBe('');
  });

  it('opens the sidebar in the language shown', async () => {
    const { element, list } = await outcomesOf(withOutcomes([message]), GERMAN);
    list().open('o1');
    await settle(element);
    expect((opened.sidebars[0] as OutcomeModalData).culture).toBe('de-DE');
  });
});

describe("a read of the site's bounds that fails", () => {
  it('is said, with Try again', async () => {
    server.fails = true;
    const { element, root } = await outcomesOf(withOutcomes([message]), englishLook, true);
    expect(text(root.querySelector('[role="alert"] p'))).toBe("The outcomes couldn't be read. Try again in a moment.");
    expect(text(root.querySelector('h2'))).toBe('After sending');
    server.fails = false;
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await settle(element);
    expect(server.asked).toBe(2);
    expect(root.querySelector('jfb-outcome-list')).not.toBeNull();
  });
});

describe("the site's bounds", () => {
  it("are the open form's one read, so a new instance draws its list at once", async () => {
    const { readOnce } = await import('../form-workspace.context');
    const first = await outcomesOf(withOutcomes([message]));
    first.workspace.choicesOf = readOnce(async () => {
      server.asked++;
      return { limits: LIMITS };
    });
    server.asked = 0;
    const again = new FormOutcomesElement();
    again.look = englishLook;
    first.element.after(again);
    const twice = new FormOutcomesElement();
    twice.look = englishLook;
    again.after(twice);
    await settle(again);
    await settle(twice);
    expect(server.asked).toBe(1);
    for (const one of [again, twice]) {
      expect(shadow(one).querySelector('jfb-outcome-list')).not.toBeNull();
      expect(shadow(one).querySelector('uui-loader')).toBeNull();
    }
  });
});

describe('the focus after Undo', () => {
  it('goes to the outcome brought back or changed, and to Add outcome for one taken away', async () => {
    const { element, list, workspace, focused } = await outcomesOf(withOutcomes([press, message]));
    list().move(0, 1);
    let before = workspace.getEditorState().definition;
    workspace.undo();
    expect(await element.focusChange(before)).toBe(true);
    expect(focused()).toBe('o2');
    opened.answer = { outcome: { key: 'o3', kind: 'message' }, at: 2 };
    list().open(null);
    await settle(element);
    before = workspace.getEditorState().definition;
    workspace.undo();
    await element.focusChange(before);
    expect(shadow(list()).activeElement?.classList.contains('add')).toBe(true);
    // An undo of something else leaves the focus where it is.
    expect(await element.focusChange(workspace.getEditorState().definition)).toBe(false);
  });

  it('names the outcome a change brought back, else the first one changed', () => {
    const was = withOutcomes([press, message]);
    expect(changedOutcome(withOutcomes([message]), was)).toBe('o2');
    expect(changedOutcome(was, withOutcomes([press, { ...message, showAnswers: true }]))).toBe('o1');
    expect(changedOutcome(was, withOutcomes([message, press]))).toBe('o1');
    expect(changedOutcome(was, withOutcomes([press]))).toBeNull();
  });
});
