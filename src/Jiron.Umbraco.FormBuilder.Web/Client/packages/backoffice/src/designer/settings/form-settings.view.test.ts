// The form's Settings view (ROADMAP 10.1a, ADR-0184): Submission open and the other groups closed with their line, as
// this browser leaves them; each setting a change of the draft, typing in one box one step to undo, a value it can't
// keep said under its box once it is left, and Ctrl+Z outside a text box; email verification, More options and the
// reference's example; opening and closing on the site's clock; who may fill it in; the protection's choices with a
// provider without keys dimmed and a provider's own setting; the themes and marks with "Site default" named; the
// developer's names; the form check's problems under their setting; Fix it to a setting; the language shared with the
// Design view; a read that fails; and After sending's outcomes (ROADMAP 10.1b, ADR-0185), each changed, added, deleted
// with Undo or moved as one step, a Fix it to one opening its sidebar. A stand-in workspace drives the real undo history.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbBasicState, UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FormSettingsChoicesResponseModel } from '../../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import type { CheckIssue, FixTarget } from '../check/form-check';
import { UNCHECKED, type CheckState } from '../check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { DesignDefinition } from '../definition';
import type { Edit, EditorState } from '../editor-model';
import type { EditorView } from '../form-workspace.context';
import { EditorHistory } from '../history';
import { builtInTypes, captchaProviders, englishLook } from '../preview/fixtures';
import type { DesignOutcome } from './outcome-model';
import type { OutcomeListElement } from './outcome-list.element';
import type { OutcomeModalData } from './outcome.modal.token';
import { DOT, groupOf } from './settings-model';
import type { UndoNotificationData } from '../../ui/undo-notification.element';

// What the server answers: the site's choices and providers, or nothing while it fails.
const server = vi.hoisted(() => ({ fails: false, asked: 0, choices: null as unknown, providers: null as unknown }));
// The cultures the look was read in, and whether the site has German too.
const reading = vi.hoisted(() => ({ cultures: [] as (string | undefined)[], german: false }));

// The outcome sidebars opened, and what the one open answers.
const opened = vi.hoisted(() => ({ sidebars: [] as unknown[], answer: undefined as unknown }));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: { closed?: () => void } }) => {
    opened.sidebars.push(args?.data);
    args?.data?.closed?.();
    return opened.answer;
  },
}));

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => ({ data: await request }),
}));

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  formSettingsChoices: async () => {
    server.asked++;
    return server.fails ? undefined : server.choices;
  },
  captchaProviderList: async () => (server.fails ? undefined : server.providers),
  visitorTextsGet: async ({ query }: { query: { culture: string } }) => ({ culture: query.culture, groups: [] }),
}));

vi.mock('../preview/look', async (original) => ({
  ...(await original<typeof import('../preview/look')>()),
  readLook: async (_host: unknown, culture?: string) => {
    reading.cultures.push(culture);
    if (!reading.german) return englishLook;
    const languages = [
      { culture: 'en-US', name: 'English (United States)', isDefault: true },
      { culture: 'de-DE', name: 'Deutsch (Deutschland)', isDefault: false },
    ];
    return {
      ...englishLook,
      languages,
      culture: culture ?? 'en-US',
      words: culture === 'de-DE' ? { ...englishLook.words, send: 'Senden' } : englishLook.words,
    };
  },
}));

const { FormSettingsViewElement, readOpenGroups } = await import('./form-settings.view');

const CARD = '6f0a2c1e-58b3-4d97-a3e1-2b7c9d40e611';

const CHOICES: FormSettingsChoicesResponseModel = {
  timeZone: 'Europe/Berlin',
  defaults: {
    protection: 'standard',
    contentRules: 'normal',
    requiredMarker: 'optional',
    theme: 'default',
    emailLayout: CARD,
    insights: true,
    ip: 'none',
    retention: { submitted: 365, approved: 365, rejected: 90, spam: 30 },
  },
  host: { fullIp: false, fullIpKey: 'JironFormBuilder:Privacy:AllowFullIp' },
  themes: [
    { alias: 'default', label: 'Default' },
    { alias: 'brand', label: 'Brand' },
  ],
  emailLayouts: [{ key: CARD, name: '#jfbEmailLayouts_card' }],
  memberGroups: [
    { key: '11111111-1111-1111-1111-111111111111', name: 'Alumni' },
    { key: '22222222-2222-2222-2222-222222222222', name: 'Volunteers' },
  ],
  limits: {
    lineLength: 500,
    htmlLength: 50000,
    nameLength: 64,
    verificationDays: 90,
    retentionDays: 3650,
    referenceDigits: 10,
    outcomes: 50,
    actions: 50,
  },
};

/** A new form's definition as the server writes it, with an email field. */
const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            { key: 'name', type: 'text', alias: 'name', label: { '': 'Name' } },
            { key: 'mail', type: 'email', alias: 'email', label: { '': 'Email' } },
          ],
        },
      ],
    },
  ],
  settings: {
    submission: { summaryPage: false, progress: 'steps', store: 'always', approval: 'automatic' },
    availability: { members: 'anyone', onePerMember: false },
    protection: { level: 'inherit', contentRules: 'inherit' },
    privacy: { ip: 'inherit', insights: true },
    appearance: { requiredMarker: 'inherit', validationSummary: true, choiceLayout: 'vertical' },
    developer: { headless: false },
  },
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
  readonly fixes = new UmbBasicState<FixTarget | null>(null);
  readonly fixRequest = this.fixes.asObservable();
  language: string | null = null;
  host: Element | undefined;
  /** The form's key, as the workspace has it once the form is read. */
  readonly unique = new UmbBasicState<string | undefined>('form1').asObservable();
  /** Whether visitors get a published version. */
  readonly states = new UmbBasicState<string | undefined>('draft');
  readonly state = this.states.asObservable();
  checkOf = async () => [];

  constructor(definition: DesignDefinition) {
    this.#history = new EditorHistory({ definition, selection: null });
    this.#editor = new UmbObjectState<EditorView>(this.#view());
    this.editor = this.#editor.asObservable();
  }

  getHostElement = () => this.host;
  getEntityType = () => 'jfb-form';
  getUnique = () => 'form1';
  getEditorState = (): EditorState => this.#history.state;
  getLanguage = () => this.language;
  setLanguage = (culture: string | null) => {
    this.language = culture;
  };
  requestFix = (target: FixTarget) => this.fixes.setValue(target);
  takeFix = (makes: (target: FixTarget) => boolean): FixTarget | null => {
    const target = this.fixes.getValue();
    if (!target || !makes(target)) return null;
    this.fixes.setValue(null);
    return target;
  };

  edit(edit: Edit, said: string, merge?: string): Edit {
    const result = this.#history.apply(edit, said, merge);
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
  peek(color: string, options: { data: Partial<UndoNotificationData> & { message: string } }) {
    this.shown.push({ color, data: options.data });
    return { close: () => undefined };
  },
};

async function viewOf(definition = DEFINITION, language: string | null = null) {
  const workspace = new StandIn(definition);
  workspace.language = language;
  const host = testHost();
  workspace.host = host;
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
  host.provideContext(UMB_NOTIFICATION_CONTEXT, {
    peek: (color: string, options: Parameters<typeof toasts.peek>[1]) => toasts.peek(color, options),
    getHostElement: () => host,
  } as never);
  const view = new FormSettingsViewElement();
  host.append(view);
  await settle(view);
  const root = shadow(view);
  const box = <E extends HTMLElement = HTMLInputElement>(id: string) =>
    must(root.getElementById(id) as E | null, `#${id}`);
  const head = (group: string) => must(root.querySelector<HTMLButtonElement>(`#group-${group}`), `the ${group} group`);
  const open = async (group: string) => {
    if (head(group).getAttribute('aria-expanded') !== 'true') head(group).click();
    await settle(view);
  };
  /** Types into a box as a person does, a step at a time, and leaves it when `leave`. */
  const type = async (id: string, value: string, leave = true) => {
    const input = box<HTMLInputElement | HTMLTextAreaElement>(id);
    input.focus();
    input.value = value;
    input.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
    if (leave) input.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
    await settle(view);
  };
  const choose = async (id: string, value: string) => {
    const select = box<HTMLSelectElement>(id);
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
    await settle(view);
  };
  const click = async (element: HTMLElement) => {
    element.click();
    await settle(view);
  };
  const settings = (group: Parameters<typeof groupOf>[1]) => groupOf(workspace.getEditorState().definition, group);
  const why = (id: string) => [...root.querySelectorAll(`[id^="${id}-why-"]`)].map((one) => text(one));
  return { workspace, view, root, box, head, open, type, choose, click, settings, why };
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
  server.choices = CHOICES;
  server.asked = 0;
  server.providers = captchaProviders;
  reading.cultures = [];
  reading.german = false;
  localStorage.clear();
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the groups', () => {
  it('show Submission and After sending open and the others closed, each with the line of what is set', async () => {
    const { head, root } = await viewOf();
    expect([head('submission'), head('after')].map((one) => one.getAttribute('aria-expanded'))).toEqual([
      'true',
      'true',
    ]);
    const lines = Object.fromEntries(
      ['availability', 'protection', 'appearance', 'developer'].map((group) => [
        group,
        text(head(group).querySelector('.summary')),
      ]),
    );
    expect(lines).toEqual({
      availability: `Always open ${DOT} No limit`,
      protection: 'Site default (Standard)',
      appearance: `Default theme ${DOT} Optional fields marked`,
      developer: 'Headless access off',
    });
    expect(root.querySelectorAll('.group-body')).toHaveLength(2);
  });

  it('stay as this browser left them', async () => {
    const first = await viewOf();
    await first.click(first.head('submission'));
    await first.click(first.head('developer'));
    expect([...readOpenGroups()]).toEqual(['after', 'developer']);
    document.body.replaceChildren();
    const again = await viewOf();
    expect([again.head('submission'), again.head('developer')].map((one) => one.getAttribute('aria-expanded'))).toEqual(
      ['false', 'true'],
    );
  });
});

describe('a setting', () => {
  it('changes the draft as it is typed, one step to undo for each visit to its box', async () => {
    const { workspace, type, settings } = await viewOf();
    await type('submit-label', 'Se', false);
    await type('submit-label', 'Send it', false);
    await type('submit-label', 'Send it');
    expect(settings('submission')['submitLabel']).toEqual({ '': 'Send it' });
    expect(workspace.said).toEqual(['Changed Send button', 'Changed Send button']);
    workspace.undo();
    expect(settings('submission')['submitLabel']).toBeUndefined();
  });

  it('makes a later visit to the same box a step of its own', async () => {
    const { workspace, box, type, settings } = await viewOf();
    await type('submit-label', 'Send');
    box('entry-title').focus();
    await type('submit-label', 'Send it');
    workspace.undo();
    expect(settings('submission')['submitLabel']).toEqual({ '': 'Send' });
  });

  it("keeps a value it can't keep out of the draft, and says why under its box once it is left", async () => {
    const { workspace, open, type, box, why, settings } = await viewOf();
    await open('availability');
    await type('max-entries', '12x', false);
    expect(why('max-entries')).toEqual([]);
    await type('max-entries', '12x');
    expect(why('max-entries')).toEqual(['Enter a whole number from 1 to 2,147,483,647.']);
    expect(box('max-entries').getAttribute('aria-invalid')).toBe('true');
    expect(box('max-entries').getAttribute('aria-describedby')).toContain('max-entries-why-0');
    expect(settings('availability')['maxEntries']).toBeUndefined();
    await type('max-entries', '120');
    expect([why('max-entries'), settings('availability')['maxEntries'], workspace.said]).toEqual([
      [],
      120,
      ['Changed Entry limit'],
    ]);
  });

  it('is undone and redone by Ctrl+Z and Ctrl+Y outside a text box, which says so', async () => {
    const { view, box, click, settings, root } = await viewOf();
    await click(box('approval'));
    expect(settings('submission')['approval']).toBe('manual');
    box('approval').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, composed: true }),
    );
    await settle(view);
    expect(settings('submission')['approval']).toBe('automatic');
    expect(box('approval').checked).toBe(false);
    expect(text(root.querySelector('[role="status"]'))).toBe('Undone: Changed Manual approval');
    box('approval').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'y', ctrlKey: true, bubbles: true, composed: true }),
    );
    await settle(view);
    expect(settings('submission')['approval']).toBe('manual');
  });

  it("leaves Ctrl+Z in a text box to the box's own Undo", async () => {
    const { view, box, type, settings } = await viewOf();
    await type('entry-title', 'x');
    box('entry-title').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, composed: true }),
    );
    await settle(view);
    expect(settings('submission')['entryTitle']).toBe('x');
  });
});

describe('a choice and a switch', () => {
  it('show the value the draft has, through Undo and Redo', async () => {
    const { workspace, view, choose, box, click } = await viewOf();
    await choose('store', 'never');
    workspace.undo();
    await settle(view);
    expect(box<HTMLSelectElement>('store').value).toBe('always');
    workspace.redo();
    await settle(view);
    expect(box<HTMLSelectElement>('store').value).toBe('never');
    // A choice the browser puts back on its own, as it restores a page's boxes, gives way to the draft's at the next
    // drawing.
    box<HTMLSelectElement>('store').value = 'always';
    await click(box('approval'));
    expect(box<HTMLSelectElement>('store').value).toBe('never');
  });

  it('keep the focus as the settings they bring come under them', async () => {
    const { view, box, root, open, choose } = await viewOf();
    box('verification').focus();
    box('verification').click();
    await settle(view);
    expect([root.activeElement?.id, root.getElementById('verification-field') !== null]).toEqual([
      'verification',
      true,
    ]);
    await open('availability');
    box('access').focus();
    await choose('access', 'groups');
    expect([root.activeElement?.id, root.getElementById('member-groups') !== null]).toEqual(['access', true]);
  });
});

describe('the submission', () => {
  it('confirms the first email field once verification is on, with the link days in bounds', async () => {
    const { box, click, type, choose, why, settings, root } = await viewOf();
    await click(box('verification'));
    expect(settings('submission')['verification']).toEqual({ emailField: 'mail', days: 7 });
    expect([...box<HTMLSelectElement>('verification-field').options].map((option) => text(option))).toEqual(['Email']);
    await type('verification-days', '91');
    expect(why('verification-days')).toEqual(['Enter a whole number from 1 to 90.']);
    await type('verification-days', '');
    expect(why('verification-days')).toEqual(['Enter a whole number from 1 to 90.']);
    await type('verification-days', '30');
    expect(settings('submission')['verification']).toEqual({ emailField: 'mail', days: 30 });
    await choose('store', 'never');
    expect(settings('submission')['store']).toBe('never');
    await click(box('verification'));
    expect(settings('submission')['verification']).toBeUndefined();
    expect(root.getElementById('verification-field')).toBeNull();
  });

  it("offers the site's own label as a button's placeholder, before the visitor's word", async () => {
    server.choices = { ...CHOICES, defaults: { ...CHOICES.defaults, submitLabel: { '': 'Send us a note' } } };
    const { box } = await viewOf();
    expect(box('submit-label').placeholder).toBe('Send us a note');
  });

  it('turns verification off for a form without an email field, and says what it needs', async () => {
    const definition = {
      ...DEFINITION,
      pages: [{ key: 'p1', groups: [{ key: 'g1', fields: [{ key: 'n', type: 'text' }] }] }],
    };
    const { box, root } = await viewOf(definition);
    expect(box('verification').disabled).toBe(true);
    expect(text(root.getElementById('verification-help'))).toContain('It needs an email field on the form');
  });

  it('keeps a number of days to store entries for, until another choice', async () => {
    const days = {
      ...DEFINITION,
      settings: { ...(DEFINITION['settings'] as object), submission: { store: 'days', storeDays: 30 } },
    };
    const { root, box, choose, settings } = await viewOf(days);
    expect(box<HTMLSelectElement>('store').value).toBe('days');
    expect(text(box<HTMLSelectElement>('store').selectedOptions[0])).toBe('For a number of days');
    expect(box('store-days').value).toBe('30');
    await choose('store', 'always');
    expect(settings('submission')).toEqual({ store: 'always' });
    expect(root.getElementById('store-days')).toBeNull();
  });

  it('stores entries for a number of days, from 30, each one in range, and says why another number is refused', async () => {
    const { root, box, choose, type, settings, why } = await viewOf();
    await choose('store', 'days');
    expect(settings('submission')).toMatchObject({ store: 'days', storeDays: 30 });
    expect(text(root.getElementById('store-days-unit'))).toBe('days');
    expect(box('store-days').getAttribute('aria-describedby')).toContain('store-days-unit');
    await type('store-days', '3651');
    expect(why('store-days')).toEqual(['Enter a whole number from 1 to 3,650.']);
    expect(settings('submission')).toMatchObject({ storeDays: 30 });
    await type('store-days', '');
    expect(why('store-days')).toEqual(['Enter a whole number from 1 to 3,650.']);
    await type('store-days', '400');
    expect(why('store-days')).toEqual([]);
    expect(settings('submission')).toMatchObject({ store: 'days', storeDays: 400 });
  });

  it('holds the page buttons, the visitor texts, the summary page, the progress and the reference under More options', async () => {
    const { root, click, type, choose, box, settings } = await viewOf();
    const more = must(root.querySelector<HTMLButtonElement>('.more-toggle'), 'More options');
    expect([text(more), root.getElementById('next-label')]).toEqual(['More options (5)', null]);
    await click(more);
    expect(more.getAttribute('aria-expanded')).toBe('true');
    // The toggle counts what it opens.
    expect(root.querySelectorAll('#submission-more > .item').length).toBe(5);
    expect(
      [...must(root.getElementById('submission-more'), 'More options').querySelectorAll('jfb-learn-more')].map((link) =>
        link.getAttribute('topic'),
      ),
    ).toEqual(['visitorTexts']);
    expect([box('next-label').placeholder, box('back-label').placeholder]).toEqual(['Next', 'Back']);
    await type('next-label', 'Continue');
    await click(box('summary-page'));
    await choose('progress', 'bar');
    await type('reference-prefix', 'REQ-');
    const year = new Date().getFullYear();
    expect(text(root.querySelector('.example code'))).toBe(`REQ-${year}-00042`);
    await type('reference-digits', '3');
    await click(box('reference-year'));
    expect(text(root.querySelector('.example code'))).toBe('REQ-042');
    expect(settings('submission')).toMatchObject({
      nextLabel: { '': 'Continue' },
      summaryPage: true,
      progress: 'bar',
      entryReference: { prefix: 'REQ-', year: false, digits: 3 },
    });
  });
});

describe('the availability', () => {
  it("takes opening and closing on the site's clock, and refuses a closing before the opening", async () => {
    const { open, type, why, settings, root } = await viewOf();
    await open('availability');
    expect(text(root.querySelector('#body-availability .note'))).toBe("Times are on the site's clock: Europe/Berlin.");
    await type('opens-at', '2026-12-01T09:00');
    expect(settings('availability')['opensAt']).toBe('2026-12-01T08:00:00Z');
    await type('closes-at', '2026-11-30T18:00');
    expect([why('closes-at'), settings('availability')['closesAt']]).toEqual([
      ['This is before it opens. Choose a later time.'],
      undefined,
    ]);
    await type('closes-at', '2026-12-24T18:00');
    expect(settings('availability')['closesAt']).toBe('2026-12-24T17:00:00Z');
    await type('opens-at', '2026-12-25T09:00');
    expect(why('opens-at')).toEqual(['This is after it closes. Choose an earlier time.']);
  });

  it('refuses a time typed only in part, and leaves the stored one', async () => {
    const { open, box, view, why, settings } = await viewOf();
    await open('availability');
    const opens = box('opens-at');
    Object.defineProperty(opens, 'validity', { value: { badInput: true } });
    opens.value = '';
    opens.dispatchEvent(new Event('change', { bubbles: true, composed: true }));
    await settle(view);
    expect([why('opens-at'), settings('availability')['opensAt']]).toEqual([['Enter a date and a time.'], undefined]);
    expect(opens.getAttribute('aria-describedby')).toBe('time-zone opening-help opens-at-why-0');
  });

  it('takes an opening refused for the closing once the closing moves past it', async () => {
    const { open, type, why, settings } = await viewOf();
    await open('availability');
    await type('closes-at', '2026-12-24T18:00');
    await type('opens-at', '2026-12-25T09:00');
    expect(why('opens-at')).toEqual(['This is after it closes. Choose an earlier time.']);
    await type('closes-at', '2026-12-31T18:00');
    expect([why('opens-at'), settings('availability')['opensAt'], settings('availability')['closesAt']]).toEqual([
      [],
      '2026-12-25T08:00:00Z',
      '2026-12-31T17:00:00Z',
    ]);
  });

  it('admits anyone, members, or members of the groups chosen by name, and keeps one the site lost', async () => {
    const gone = '33333333-3333-3333-3333-333333333333';
    const definition = {
      ...DEFINITION,
      settings: { ...(DEFINITION['settings'] as object), availability: { members: 'groups', memberGroups: [gone] } },
    };
    const { open, root, click, choose, settings } = await viewOf(definition);
    await open('availability');
    const names = () => [...root.querySelectorAll('#member-groups label')].map((one) => text(one));
    expect(names()).toEqual(['Alumni', 'Volunteers', 'A group the site no longer has']);
    const boxes = () => [...root.querySelectorAll<HTMLInputElement>('#member-groups input')];
    await click(must(boxes()[1], 'Volunteers'));
    await click(must(boxes()[2], 'the lost group'));
    expect(settings('availability')['memberGroups']).toEqual(['22222222-2222-2222-2222-222222222222']);
    await choose('access', 'members');
    expect(settings('availability')).toEqual({ members: 'members' });
    expect(root.getElementById('member-groups')).toBeNull();
  });

  it('asks for one entry per member once the form is for members, and drops it for anyone (ADR-0231)', async () => {
    const { open, root, click, choose, settings } = await viewOf();
    await open('availability');
    expect(root.getElementById('one-per-member')).toBeNull();
    await choose('access', 'members');
    await click(must(root.getElementById('one-per-member'), 'One entry per member'));
    expect(settings('availability')).toEqual({ members: 'members', onePerMember: true });
    await choose('access', 'groups');
    expect(settings('availability')['onePerMember']).toBe(true);
    await choose('access', 'anyone');
    expect(settings('availability')).toEqual({ members: 'anyone' });
    expect(root.getElementById('one-per-member')).toBeNull();
  });
});

describe('the spam protection', () => {
  it("names the site's choice, dims a provider without its keys, and shows a chosen provider's own setting", async () => {
    const { open, root, click, type, why, settings } = await viewOf();
    await open('protection');
    const rows = [...root.querySelectorAll<HTMLElement>('.choice')];
    expect(rows.map((row) => text(row.querySelector('.choice-name')))).toEqual([
      'Site default (Standard)',
      'Standard',
      'Standard with proof of work',
      'reCAPTCHA v2',
      'reCAPTCHA v3',
      'hCaptcha',
    ]);
    expect(rows.map((row) => row.hasAttribute('data-dimmed'))).toEqual([false, false, false, false, true, true]);
    // Each named by its name alone and described by its line, so a screen reader says each once.
    const standard = must(rows[1]?.querySelector('input'), 'Standard');
    expect(text(root.getElementById(standard.getAttribute('aria-labelledby') ?? ''))).toBe('Standard');
    expect(text(root.getElementById(standard.getAttribute('aria-describedby') ?? ''))).toContain('A hidden field');
    expect(text(rows[4]?.querySelector('.choice-line'))).toContain('no keys for it yet');
    await click(must(rows[4]?.querySelector<HTMLInputElement>('input'), 'reCAPTCHA v3'));
    expect(settings('protection')).toMatchObject({ level: 'captcha', captcha: { provider: 'recaptchaV3' } });
    await type('setting-captcha-threshold', '2');
    expect(why('setting-captcha-threshold')).toEqual([]);
    expect(text(root.getElementById('setting-captcha-threshold-why'))).toBe('Enter 1 or less.');
    await type('setting-captcha-threshold', '0.7');
    expect(settings('protection')['captcha']).toEqual({ provider: 'recaptchaV3', settings: { threshold: '0.7' } });
    await click(must(rows[1]?.querySelector<HTMLInputElement>('input'), 'Standard'));
    expect(settings('protection')).toEqual({ level: 'standard', contentRules: 'inherit' });
  });

  it("offers content checks with the site's named", async () => {
    const { open, box, choose, settings } = await viewOf();
    await open('protection');
    expect(text(box<HTMLSelectElement>('content-rules').options[0])).toBe('Site default (Normal)');
    await choose('content-rules', 'strict');
    expect(settings('protection')['contentRules']).toBe('strict');
  });
});

describe('the appearance and the developer', () => {
  it("offers the site's themes and marks, the site's own named", async () => {
    const { open, box, choose, settings } = await viewOf();
    await open('appearance');
    expect([...box<HTMLSelectElement>('theme').options].map((option) => text(option))).toEqual([
      'Site default (Default)',
      'Default',
      'Brand',
    ]);
    await choose('theme', 'brand');
    expect(settings('appearance')['theme']).toBe('brand');
    await choose('theme', '');
    expect(settings('appearance')['theme']).toBeUndefined();
    expect(text(box<HTMLSelectElement>('required-marker').options[0])).toBe('Site default (Optional fields marked)');
  });

  it('takes the names one a line as the server checks them', async () => {
    const { root, open, type, why, settings } = await viewOf();
    await open('developer');
    expect([...root.querySelectorAll('jfb-learn-more')].map((link) => link.getAttribute('topic'))).toEqual([
      'headless',
    ]);
    await type('context-values', 'crm\njfbServer');
    expect(why('context-values')).toEqual([
      '"jfbServer" starts with "jfb", which Jiron Form Builder keeps for itself.',
    ]);
    await type('query-values', 'ref\nutm_source');
    expect(settings('developer')['queryValues']).toEqual(['ref', 'utm_source']);
  });
});

describe('privacy', () => {
  it("sets the form's own days per state, each empty for the site's, which its box shows, and refuses one out of range", async () => {
    const { root, box, open, type, settings, why } = await viewOf();
    await open('privacy');
    expect(['submitted', 'approved', 'rejected', 'spam'].map((state) => box(`retention-${state}`).placeholder)).toEqual(
      ['365', '365', '90', '30'],
    );
    expect(text(root.querySelector('#retention legend'))).toBe('Days kept in each state');
    await type('retention-spam', '7');
    expect(settings('privacy')).toMatchObject({ retentionDays: { spam: 7 } });
    await type('retention-rejected', '0');
    expect(why('retention-rejected')).toEqual(['Enter a whole number from 1 to 3,650.']);
    await type('retention-rejected', '40000');
    expect(why('retention-rejected')).toEqual(['Enter a whole number from 1 to 3,650.']);
    expect(settings('privacy')).not.toHaveProperty('retentionDays.rejected');
    await type('retention-spam', '');
    expect(settings('privacy')).not.toHaveProperty('retentionDays.spam');
  });

  it('says a closed group has days of its own', async () => {
    const own = {
      ...DEFINITION,
      settings: { ...(DEFINITION['settings'] as object), privacy: { retentionDays: { spam: 7 }, insights: true } },
    };
    const { head } = await viewOf(own);
    expect(text(head('privacy'))).toContain('Own retention days');
  });

  it("chooses what entries keep of IP addresses: the site's on Site default, full waiting for the host", async () => {
    const { root, box, open, choose, settings, head, click } = await viewOf();
    await open('privacy');
    const select = box<HTMLSelectElement>('ip');
    expect([...select.options].map((option) => [text(option), option.disabled])).toEqual([
      ['Site default (No IP addresses)', false],
      ['Keep none', false],
      ['Anonymised, the end removed', false],
      ['Full addresses', true],
    ]);
    expect(text(root.getElementById('ip-help'))).toBe(
      "What each entry keeps of the address it was sent from. Full addresses need JironFormBuilder:Privacy:AllowFullIp in the site's configuration.",
    );
    await choose('ip', 'anonymised');
    expect(settings('privacy')['ip']).toBe('anonymised');
    // Closed, the group says it.
    await click(head('privacy'));
    expect(text(head('privacy'))).toContain('Anonymised IP addresses');
  });

  it("offers full addresses where the host allows them, and keeps a form's full one chosen where it does not", async () => {
    server.choices = {
      ...CHOICES,
      host: { ...CHOICES.host, fullIp: true },
      defaults: { ...CHOICES.defaults, ip: 'anonymised' },
    };
    const allowed = await viewOf();
    await allowed.open('privacy');
    const select = allowed.box<HTMLSelectElement>('ip');
    expect(text(select.options[0])).toBe('Site default (Anonymised IP addresses)');
    expect(select.options[3]?.disabled).toBe(false);
    expect(text(allowed.root.getElementById('ip-help'))).toBe('What each entry keeps of the address it was sent from.');
    document.body.replaceChildren();

    server.choices = CHOICES;
    const full = {
      ...DEFINITION,
      settings: { ...(DEFINITION['settings'] as object), privacy: { ip: 'full', insights: true } },
    };
    const held = await viewOf(full);
    await held.open('privacy');
    const kept = held.box<HTMLSelectElement>('ip');
    expect([kept.value, kept.options[3]?.disabled]).toEqual(['full', false]);
  });
});

describe('the form check', () => {
  it('shows its problems under their setting, and counts them on a closed group', async () => {
    const definition = {
      ...DEFINITION,
      settings: { ...(DEFINITION['settings'] as object), availability: { members: 'groups' } },
    };
    const { workspace, view, head, open, why } = await viewOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [
        issue('$.settings.availability.memberGroups', 'part_missing'),
        issue('$.settings.protection.captcha.provider', 'captcha_not_configured', 'warning'),
      ],
    });
    await settle(view);
    expect(text(head('availability').querySelector('.flag'))).toBe('1 problem');
    // A provider without its keys says so on its line among the choices, not again as the check's warning.
    expect(head('protection').querySelector('.flag')).toBeNull();
    await open('availability');
    expect(why('member-groups')).toHaveLength(1);
    expect(why('member-groups')[0]).not.toBe('In English.');
  });

  it('leads a Fix it to its setting: the group and More options open, the focus on its box', async () => {
    const { workspace, view, root, head } = await viewOf();
    await settle(view);
    workspace.requestFix({ kind: 'view', view: 'settings', path: '$.settings.submission.entryReference.digits' });
    await settle(view);
    expect(root.activeElement?.id).toBe('reference-digits');
    workspace.requestFix({ kind: 'view', view: 'settings', path: '$.settings.developer.contextValues[0]' });
    await settle(view);
    expect([head('developer').getAttribute('aria-expanded'), root.activeElement?.id]).toEqual([
      'true',
      'context-values',
    ]);
    workspace.requestFix({ kind: 'view', view: 'settings', path: '$.settings.privacy.ip' });
    await settle(view);
    expect([head('privacy').getAttribute('aria-expanded'), root.activeElement?.id]).toEqual(['true', 'ip']);
    workspace.requestFix({ kind: 'view', view: 'settings', path: '$.settings.privacy.unknown' });
    await settle(view);
    expect(root.activeElement).toBe(head('submission'));
    expect(workspace.fixes.getValue()).toBeNull();
  });

  it('shows a problem at the box drawn for it, and counts one under closed More options on its toggle', async () => {
    const definition = {
      ...DEFINITION,
      settings: {
        ...(DEFINITION['settings'] as object),
        availability: { members: 'anyone', memberGroups: ['11111111-1111-1111-1111-111111111111'] },
        protection: { level: 'captcha', captcha: { provider: 'recaptchaV3', settings: { score: '1' } } },
      },
    };
    const { workspace, view, root, open, why } = await viewOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [
        issue('$.settings.availability.memberGroups', 'property_not_supported'),
        issue('$.settings.protection.captcha.settings.score', 'setting_unknown'),
        issue('$.settings.submission.entryReference.digits', 'invalid_value'),
        // Verification is off, so its field has no box: the problem shows at its switch.
        issue('$.settings.submission.verification.emailField', 'part_missing'),
      ],
    });
    await settle(view);
    expect(why('verification')).toHaveLength(1);
    expect(text(root.querySelector('.more-toggle .flag'))).toBe('1 problem');
    await open('availability');
    await open('protection');
    expect([why('access').length, why('protection').length]).toEqual([1, 1]);
    must(root.querySelector<HTMLButtonElement>('.more-toggle'), 'More options').click();
    await settle(view);
    expect([root.querySelector('.more-toggle .flag'), why('reference-digits')]).toEqual([null, ['In English.']]);
  });

  it("keeps a provider's stored value in its box beside the check's problem with it", async () => {
    const definition = {
      ...DEFINITION,
      settings: {
        ...(DEFINITION['settings'] as object),
        protection: { level: 'captcha', captcha: { provider: 'recaptchaV3', settings: { threshold: '2' } } },
      },
    };
    const { workspace, view, open, box, root } = await viewOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [issue('$.settings.protection.captcha.settings.threshold', 'setting_invalid')],
    });
    await settle(view);
    await open('protection');
    const threshold = box('setting-captcha-threshold');
    expect([threshold.value, threshold.getAttribute('aria-invalid')]).toEqual(['2', 'true']);
    expect(threshold.getAttribute('aria-describedby')).toContain('setting-captcha-threshold-found-0');
    expect(text(root.getElementById('setting-captcha-threshold-found-0'))).toBe('In English.');
  });

  it("says a warning's weight in words beside its sign", async () => {
    const definition = {
      ...DEFINITION,
      settings: { ...(DEFINITION['settings'] as object), appearance: { theme: 'retired' } },
    };
    const { workspace, view, open, root } = await viewOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [issue('$.settings.appearance.theme', 'theme_missing', 'warning')],
    });
    await settle(view);
    await open('appearance');
    const line = must(root.getElementById('theme-why-0'), 'the warning');
    expect([line.getAttribute('data-severity'), line.querySelector('uui-icon')?.getAttribute('name')]).toEqual([
      'warning',
      'icon-info',
    ]);
    expect(text(line)).toBe('Warning: The site has no theme "retired", so the form shows with the default theme.');
  });

  it("counts no suggestion on a closed group's line, and shows it under its setting", async () => {
    const definition = {
      ...DEFINITION,
      settings: { ...(DEFINITION['settings'] as object), appearance: { theme: 'retired' } },
    };
    const { workspace, view, open, head, root } = await viewOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [issue('$.settings.appearance.theme', 'theme_missing', 'hint')],
    });
    await settle(view);
    expect(head('appearance').querySelector('.flag')).toBeNull();
    await open('appearance');
    expect(root.getElementById('theme-why-0')?.getAttribute('data-severity')).toBe('hint');
  });

  it('leaves a Fix it of another view to it', async () => {
    const { workspace, view } = await viewOf();
    for (const target of [
      { kind: 'element', key: 'name' },
      { kind: 'view', view: 'actions', path: '$.actions[0].settings.subject' },
    ] satisfies FixTarget[]) {
      workspace.requestFix(target);
      await settle(view);
      expect(workspace.fixes.getValue()).toBe(target);
    }
  });

  it('takes a Fix it asked before the view was shown once it has read what it shows', async () => {
    const workspace = new StandIn(DEFINITION);
    workspace.requestFix({ kind: 'view', view: 'settings', path: '$.settings.appearance.cssClass' });
    const host = testHost();
    workspace.host = host;
    host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
    const view = new FormSettingsViewElement();
    host.append(view);
    await settle(view);
    expect(shadow(view).activeElement?.id).toBe('css-class');
  });
});

describe('the language', () => {
  it("is the Design view's, and its switch is shared with it", async () => {
    reading.german = true;
    const { workspace, view, root, box, type, settings } = await viewOf(DEFINITION, 'de-DE');
    expect(reading.cultures).toEqual(['de-DE']);
    expect(box('submit-label').placeholder).toBe('Senden');
    expect(text(root.querySelector('.toolbar .note'))).toContain('Editing the German texts');
    await type('submit-label', 'Abschicken');
    expect(settings('submission')['submitLabel']).toEqual({ '': '', 'de-DE': 'Abschicken' });
    const language = must(root.querySelector<HTMLSelectElement>('.language select'), 'the language');
    language.focus();
    language.value = 'en-US';
    language.dispatchEvent(new Event('change'));
    await settle(view);
    expect([workspace.language, box('submit-label').value], "null for the site's default").toEqual([null, '']);
  });
});

describe('what a translator has left', () => {
  const lacking: DesignDefinition = {
    ...DEFINITION,
    settings: {
      ...(DEFINITION['settings'] as Record<string, unknown>),
      submission: { submitLabel: { '': 'Send' }, nextLabel: { '': 'On', 'de-DE': 'Weiter' } },
      availability: { members: 'anyone', closedMessage: { '': '<p>Closed for now.</p>' } },
    },
    outcomes: [{ key: 'o1', kind: 'message', message: { '': '<p>Thanks!</p>' } }],
  };

  it('marks each text without the language shown beside its label, and a closed group says it once', async () => {
    reading.german = true;
    const { root, box, head, open, view } = await viewOf(lacking, 'de-DE');
    const mark = root.querySelector('#submit-label-untranslated');
    expect(text(mark)).toBe('No German text yet');
    expect(box('submit-label').getAttribute('aria-describedby')?.split(' ')).toContain('submit-label-untranslated');
    // The next button's label has its German text, and the back button's none at all: More options says nothing.
    expect(text(root.querySelector('.more-toggle'))).not.toContain('No German text yet');
    expect(text(head('availability'))).toContain('No German text yet');
    expect(text(root.querySelector('jfb-outcome-list')?.shadowRoot?.querySelector('.row-line'))).toContain(
      'No German text yet',
    );
    await open('availability');
    expect(text(root.querySelector('[data-group="availability"] .label-row .untranslated'))).toBe('No German text yet');
    expect(
      must(
        root.querySelector<HTMLElement & { description: string }>('jfb-rich-text#closed-message'),
        'the closed message',
      ).description,
    ).toContain('No German text yet');
    view.remove();
  });

  it('marks nothing in the default language', async () => {
    reading.german = true;
    const { root, head, open } = await viewOf(lacking, 'en-US');
    await open('availability');
    expect(root.querySelectorAll('.untranslated')).toHaveLength(0);
    expect(text(head('submission'))).not.toContain('text yet');
  });
});

describe('a Fix it in a text of another language', () => {
  it('shows the language of the text, and puts the focus in its box', async () => {
    reading.german = true;
    const { workspace, view, root } = await viewOf();
    workspace.requestFix({ kind: 'view', view: 'settings', path: "$.settings.submission.submitLabel['de-DE']" });
    await settle(view);
    expect([workspace.language, reading.cultures.at(-1), root.activeElement?.id]).toEqual([
      'de-DE',
      'de-DE',
      'submit-label',
    ]);
  });
});

describe('a read that fails', () => {
  it('is said, with Try again', async () => {
    server.fails = true;
    const { view, root } = await viewOf();
    expect(text(root.querySelector('[role="alert"] p'))).toBe("The settings couldn't be read. Try again in a moment.");
    server.fails = false;
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await settle(view);
    expect(server.asked).toBe(2);
    expect(root.querySelectorAll('.group')).toHaveLength(7);
  });
});

describe('after sending', () => {
  const message: DesignOutcome = { key: 'o1', kind: 'message', message: { '': '<p>Thanks!</p>' }, showAnswers: false };
  const press: DesignOutcome = {
    key: 'o2',
    kind: 'redirect',
    redirect: { url: '/press', delaySeconds: 0 },
    rules: [{ key: 'r1', effect: 'use', enabled: true, match: 'all', conditions: [{ field: 'name', op: 'notEmpty' }] }],
  };
  const withOutcomes = (outcomes: readonly DesignOutcome[]): DesignDefinition => ({ ...DEFINITION, outcomes });
  const listIn = (root: ShadowRoot) => must(root.querySelector<OutcomeListElement>('jfb-outcome-list'), 'the list');
  const outcomesIn = (workspace: { getEditorState(): EditorState }) =>
    (workspace.getEditorState().definition['outcomes'] ?? []) as DesignOutcome[];

  it("lists the draft's outcomes, and says closed what visitors get", async () => {
    const { root, head, click } = await viewOf(withOutcomes([press, message]));
    const list = listIn(root);
    expect(list.outcomes.map((one) => one.key)).toEqual(['o2', 'o1']);
    expect(list.defaultMessage).toBe('Thank you. We have received your message.');
    await click(head('after'));
    expect(text(head('after').querySelector('.summary'))).toBe('2 outcomes');
    document.body.replaceChildren();
    // One outcome is named by what visitors get.
    localStorage.clear();
    const one = await viewOf(withOutcomes([message]));
    await one.click(one.head('after'));
    expect(text(one.head('after').querySelector('.summary'))).toBe('Message: "Thanks!"');
    document.body.replaceChildren();
    // A browser that kept nothing opens After sending; closed, it says what visitors then get.
    localStorage.clear();
    const none = await viewOf();
    await none.click(none.head('after'));
    expect(text(none.head('after').querySelector('.summary'))).toBe('The default message');
  });

  it('changes an outcome in its sidebar, one step to undo', async () => {
    const { root, view, workspace } = await viewOf(withOutcomes([message]));
    opened.answer = { outcome: { ...message, showAnswers: true }, at: 0 };
    listIn(root).open('o1');
    await settle(view);
    const data = opened.sidebars[0] as OutcomeModalData;
    expect([data.outcome?.key, data.number, data.count, data.culture, data.form, data.htmlLength]).toEqual([
      'o1',
      1,
      1,
      'en-US',
      'form1',
      50000,
    ]);
    expect(outcomesIn(workspace)).toEqual([{ ...message, showAnswers: true }]);
    expect(workspace.said).toEqual(['Changed an outcome']);
    workspace.undo();
    expect(outcomesIn(workspace)).toEqual([message]);
    // A draft not yet published says nothing more.
    expect(toasts.shown).toEqual([]);
  });

  it('adds an outcome where its sidebar placed it, and says where it went', async () => {
    const { root, view, workspace } = await viewOf(withOutcomes([message]));
    const added = { ...press, key: 'o3' };
    opened.answer = { outcome: added, at: 0 };
    listIn(root).open(null);
    await settle(view);
    expect((opened.sidebars[0] as OutcomeModalData).outcome).toBeNull();
    expect(outcomesIn(workspace).map((one) => one.key)).toEqual(['o3', 'o1']);
    expect(workspace.said).toEqual(['Added an outcome']);
    expect(text(shadow(listIn(root)).querySelector('[role="status"]'))).toBe(
      'Added. Redirect to /press: position 1 of 2.',
    );
  });

  it('moves an outcome to the place its sidebar chose, and says where it went', async () => {
    const { root, view, workspace } = await viewOf(withOutcomes([message, press]));
    opened.answer = { outcome: message, at: 1 };
    listIn(root).open('o1');
    await settle(view);
    expect(outcomesIn(workspace).map((one) => one.key)).toEqual(['o2', 'o1']);
    expect(text(shadow(listIn(root)).querySelector('[role="status"]'))).toBe(
      'Moved. Message: "Thanks!": position 2 of 2.',
    );
  });

  it('says nothing for an Update that changes nothing, even on a published form', async () => {
    const { root, view, workspace } = await viewOf(withOutcomes([message]));
    workspace.states.setValue('published');
    opened.answer = { outcome: message, at: 0 };
    listIn(root).open('o1');
    await settle(view);
    expect([workspace.said, toasts.shown]).toEqual([[], []]);
    expect(shadow(listIn(root)).activeElement?.closest('.outcome-row')?.getAttribute('data-key')).toBe('o1');
  });

  it('gives the focus back to the list: the outcome, a new one, the one after a deleted one', async () => {
    const { root, view } = await viewOf(withOutcomes([press, message]));
    const focused = () => shadow(listIn(root)).activeElement?.closest('.outcome-row')?.getAttribute('data-key');
    // Cancel.
    listIn(root).open('o1');
    await settle(view);
    expect(focused()).toBe('o1');
    opened.answer = { outcome: { key: 'o3', kind: 'message' }, at: 2 };
    listIn(root).open(null);
    await settle(view);
    expect(focused()).toBe('o3');
    opened.answer = { deleted: true };
    listIn(root).open('o2');
    await settle(view);
    expect(focused()).toBe('o1');
  });

  it('opens no new outcome once the form has the most it can have', async () => {
    const outcomes = Array.from({ length: 50 }, (_, at): DesignOutcome => ({ key: `o${at}`, kind: 'message' }));
    const { root, view } = await viewOf(withOutcomes(outcomes));
    expect(listIn(root).limit).toBe(50);
    listIn(root).open(null);
    await settle(view);
    expect(opened.sidebars).toEqual([]);
  });

  it('deletes an outcome at once, Undo in the toast bringing it back', async () => {
    const { root, view, workspace } = await viewOf(withOutcomes([press, message]));
    opened.answer = { deleted: true };
    listIn(root).open('o2');
    await settle(view);
    expect(outcomesIn(workspace).map((one) => one.key)).toEqual(['o1']);
    const [toast] = toasts.shown;
    expect(toast?.data.message).toBe('Outcome deleted.');
    await toast?.data.undo?.();
    expect(outcomesIn(workspace).map((one) => one.key)).toEqual(['o2', 'o1']);
    // The toast is gone with its Undo: the focus goes to the outcome back in the list.
    await settle(view);
    expect(shadow(listIn(root)).activeElement?.closest('.outcome-row')?.getAttribute('data-key')).toBe('o2');
  });

  it('moves an outcome as the list asks, one step to undo', async () => {
    const { root, workspace } = await viewOf(withOutcomes([press, message]));
    listIn(root).move(0, 1);
    expect(outcomesIn(workspace).map((one) => one.key)).toEqual(['o1', 'o2']);
    expect(workspace.said).toEqual(['Moved an outcome']);
  });

  it("leads a Fix it to an outcome's setting in its sidebar, and one at the list to Add outcome", async () => {
    const { root, view, workspace } = await viewOf(withOutcomes([message, press]));
    workspace.requestFix({ kind: 'view', view: 'settings', path: '$.outcomes[0].redirect.url', key: 'o2' });
    // The sidebar opens once the router stays still (ADR-0227).
    await settle(view);
    expect(opened.sidebars).toHaveLength(0);
    await vi.waitFor(() => expect(opened.sidebars).toHaveLength(1));
    // The key leads, as the outcomes may have moved since the check.
    expect([
      (opened.sidebars[0] as OutcomeModalData).outcome?.key,
      (opened.sidebars[0] as OutcomeModalData).focus,
    ]).toEqual(['o2', 'address']);
    workspace.requestFix({ kind: 'view', view: 'settings', path: '$.outcomes' });
    await settle(view);
    expect(opened.sidebars).toHaveLength(1);
    expect(shadow(listIn(root)).activeElement?.classList.contains('add')).toBe(true);
  });

  it("counts an outcome's problems on its row and on the closed group", async () => {
    const definition = withOutcomes([message, press]);
    const { root, view, workspace, head, click } = await viewOf(definition);
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [
        issue('$.outcomes[1].redirect.url', 'invalid_redirect'),
        issue('$.outcomes[0].message', 'x', 'hint'),
        issue('$.outcomes[0].message', 'y', 'warning'),
      ],
    });
    await settle(view);
    expect([...listIn(root).flags]).toEqual([
      ['o2', { count: 1, error: true }],
      ['o1', { count: 1, error: false }],
    ]);
    await click(head('after'));
    expect(text(head('after').querySelector('.summary'))).toBe(`2 problems ${DOT} 2 outcomes`);
    expect(head('after').querySelector('.flag')?.getAttribute('data-severity')).toBe('error');
    // Warnings alone are counted in a warning's colour.
    workspace.checks.setValue({
      status: 'checked',
      definition,
      issues: [issue('$.outcomes[0].message', 'y', 'warning')],
    });
    await settle(view);
    expect(head('after').querySelector('.flag')?.getAttribute('data-severity')).toBe('warning');
  });

  it("shows the list's own problems in the list, above Add outcome", async () => {
    const definition = withOutcomes([message]);
    const { root, view, workspace } = await viewOf(definition);
    workspace.checks.setValue({ status: 'checked', definition, issues: [issue('$.outcomes', 'too_many_outcomes')] });
    await settle(view);
    expect(listIn(root).problems).toHaveLength(1);
    expect(root.querySelector('[id^="outcomes-why-"]')).toBeNull();
  });

  it('says on a published form that visitors get a change once it is published', async () => {
    const { root, view, workspace } = await viewOf(withOutcomes([message]));
    workspace.states.setValue('published');
    opened.answer = { outcome: { ...message, showAnswers: true }, at: 0 };
    listIn(root).open('o1');
    await settle(view);
    expect(toasts.shown.map((one) => one.data.message)).toEqual(['Updated. Visitors get it once you publish.']);
  });
});
