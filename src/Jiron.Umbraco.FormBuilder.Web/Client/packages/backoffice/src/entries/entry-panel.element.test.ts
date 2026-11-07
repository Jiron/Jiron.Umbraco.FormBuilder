// The entry panel (ROADMAP 11.2, IA 6.3, ADR-0208): its header in each place, the answers with Reveal, files, consents
// and signatures, the bar of each state and user, a change of state that stays on the entry with Undo, a move back that
// asks first, Edit answers with its problems, the timeline with Retry, notes, Delete... and Forward....
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  EntryPanelAnswerResponseModel,
  EntryPanelResponseModel,
  EntryStateResponseModel,
  EntryTimelineResponseModel,
} from '../api';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { ENTRY_FORWARD_MODAL_ALIAS, ENTRY_IMAGE_MODAL_ALIAS } from './entry-modals';
import type { EntryPanelElement, PanelPlace } from './entry-panel.element';

const opened = vi.hoisted(() => ({
  modals: [] as { token: string; data: unknown }[],
  answers: {} as Record<string, unknown>,
  confirms: true,
  confirmations: [] as { headline?: string; content?: unknown; confirmLabel?: string }[],
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: { toString(): string }, args?: { data?: unknown }) => {
    opened.modals.push({ token: token.toString(), data: args?.data });
    const answer = opened.answers[token.toString()];
    if (answer === undefined) throw new Error('closed');
    return answer;
  },
  umbConfirmModal: async (_host: unknown, data: { headline?: string; confirmLabel?: string }) => {
    opened.confirmations.push(data);
    if (!opened.confirms) throw new Error('cancelled');
  },
}));

/** The server, as the panel's calls reach it. */
const server = vi.hoisted(() => ({
  panel: undefined as EntryPanelResponseModel | undefined,
  opens: 0,
  reveals: [] as string[],
  revealed: undefined as EntryPanelAnswerResponseModel | undefined,
  timeline: undefined as EntryTimelineResponseModel | undefined,
  notes: [] as { id: number; author: string; mine: boolean; body: string; created: string; edited: string | null }[],
  added: [] as string[],
  changedNotes: [] as { id: number; body: string }[],
  edits: [] as { answers: { field: string; values: string[] }[]; overbook: boolean }[],
  editAnswer: [] as unknown[],
  moves: [] as { entries: readonly string[]; state: string; overbook: boolean }[],
  moveAnswers: [] as (Partial<EntryStateResponseModel> | undefined)[],
  undos: [] as string[],
  deletes: [] as boolean[],
  reruns: [] as boolean[],
  retries: [] as { run: number; confirmed: boolean }[],
  retryAnswers: [] as ('queued' | 'confirm' | 'failed')[],
  files: [] as string[],
  saved: [] as string[],
}));

vi.mock('./entry-panel-api', () => ({
  openEntry: async () => {
    server.opens += 1;
    return server.panel ? structuredClone(server.panel) : undefined;
  },
  revealAnswer: async (_host: unknown, _form: string, _entry: string, field: string) => {
    server.reveals.push(field);
    return server.revealed;
  },
  readTimeline: async () => server.timeline,
  readNotes: async () => server.notes,
  addNote: async (_host: unknown, _form: string, _entry: string, body: string) => {
    server.added.push(body);
    return { id: 9, author: 'Ana', mine: true, body, created: new Date().toISOString(), edited: null };
  },
  changeNote: async (_host: unknown, _form: string, _entry: string, id: number, body: string) => {
    server.changedNotes.push({ id, body });
    return { id, author: 'Ana', mine: true, body, created: new Date().toISOString(), edited: new Date().toISOString() };
  },
  editAnswers: async (
    _host: unknown,
    _form: string,
    _entry: string,
    answers: { field: string; values: string[] }[],
    overbook: boolean,
  ) => {
    server.edits.push({ answers, overbook });
    return server.editAnswer.shift() ?? { edited: { changed: answers.length }, problems: [] };
  },
  retryRun: async (_host: unknown, _form: string, _entry: string, run: number, confirmed: boolean) => {
    server.retries.push({ run, confirmed });
    return server.retryAnswers.shift() ?? 'queued';
  },
  fileBlob: async (_host: unknown, _form: string, file: string) => {
    server.files.push(file);
    return new Blob(['bytes']);
  },
}));

vi.mock('./entries-api', () => ({
  changeState: async (_host: unknown, _form: string, entries: readonly string[], state: string, overbook = false) => {
    server.moves.push({ entries, state, overbook });
    const answer = server.moveAnswers.shift();
    return {
      changeKey: 'change1',
      undoUntilUtc: new Date(Date.now() + 10_000).toISOString(),
      moved: entries.map((key) => ({ key, state })),
      unmoved: [],
      ...answer,
    };
  },
  undoChange: async (_host: unknown, _form: string, change: string) => {
    server.undos.push(change);
    return 'done';
  },
  deleteEntries: async (_host: unknown, _form: string, entries: readonly string[], confirm: boolean) => {
    server.deletes.push(confirm);
    return { entries: entries.length, files: 1, deleted: confirm };
  },
  rerunActions: async (_host: unknown, _form: string, entries: readonly string[], confirm: boolean) => {
    server.reruns.push(confirm);
    return {
      actions: [{ key: 'a1', type: 'email', name: 'Email the team', trigger: 'approved' }],
      entries: entries.length,
      leftOut: 0,
      queued: confirm,
    };
  },
}));

vi.mock('../library/actions/export-form.action', () => ({
  saveBlob: (name: string) => server.saved.push(name),
}));

await import('./entry-panel.element');

const ADMIN = ['viewEntries', 'editEntries', 'deleteEntries', 'manageActions', 'viewSensitive'];

const answer = (field: string, label: string, more: Partial<EntryPanelAnswerResponseModel> = {}) => ({
  field,
  label,
  removed: false,
  sensitive: false,
  masked: false,
  notKept: false,
  texts: [],
  files: [],
  consent: null,
  signedName: null,
  edit: null,
  ...more,
});

const panelOf = (more: Partial<EntryPanelResponseModel> = {}): EntryPanelResponseModel => ({
  key: 'e7',
  form: 'form1',
  formName: 'Job application',
  number: 7,
  title: 'Jane Doe',
  state: 'submitted',
  received: '2026-11-07T11:00:00Z',
  manualApproval: true,
  isTest: false,
  details: {
    page: 'https://example.org/careers/apply',
    culture: 'en',
    duration: 365,
    member: null,
    memberName: null,
    ipAddress: null,
    spamReasons: [],
  },
  pages: [
    {
      key: 'p1',
      title: null,
      groups: [
        {
          key: 'g1',
          title: null,
          answers: [
            answer('name', 'Name', {
              texts: ['Jane Doe'],
              edit: { kind: 'text', values: ['Jane Doe'], options: [], required: true },
            }),
            answer('salary', 'Salary', { sensitive: true, masked: true }),
            answer('cv', 'CV', {
              files: [
                { key: 'f1', name: 'cv.pdf', contentType: 'application/pdf', size: 389_120, image: false, held: false },
                { key: 'f2', name: 'photo.png', contentType: 'image/png', size: 2048, image: true, held: false },
                { key: 'f3', name: 'late.doc', contentType: 'application/msword', size: 100, image: false, held: true },
              ],
            }),
            answer('terms', 'Terms', { consent: 'I agree to the terms.' }),
            answer('sign', 'Signature', { texts: ['The signature'], signedName: 'Jane Doe' }),
            answer('once', 'Reference', { notKept: true }),
            answer('old', 'Old question', { removed: true, texts: ['Yes'] }),
          ],
        },
      ],
    },
  ],
  notes: 0,
  permissions: ADMIN,
  ...more,
});

async function open(place: PanelPlace = 'beside', more: Partial<EntryPanelElement> = {}) {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const panel = document.createElement('jfb-entry-panel');
  Object.assign(panel, { place, ...more });
  panel.form = 'form1';
  panel.entry = 'e7';
  const told: { name: string; detail: unknown }[] = [];
  for (const name of ['jfb-entry-seen', 'jfb-entry-moved', 'jfb-entry-gone', 'jfb-entry-close'])
    host.addEventListener(name, (event) => told.push({ name, detail: (event as CustomEvent).detail }));
  host.append(panel);
  await vi.waitFor(() => expect(shadow(panel).querySelector('.head, [role="alert"]')).not.toBeNull());
  await settle(panel);
  const root = shadow(panel);
  const buttons = () => all<HTMLElement>(panel, 'uui-button, button');
  const button = (name: string) =>
    must(
      buttons().find(
        (one) => one.getAttribute('label') === name || one.getAttribute('aria-label') === name || text(one) === name,
      ),
      `the button ${name}`,
    );
  const tab = (name: string) =>
    must(
      all<HTMLElement>(panel, 'uui-tab').find((one) => one.getAttribute('label') === name),
      `the tab ${name}`,
    );
  const bar = () => [...root.querySelectorAll('footer.bar > uui-button')].map((one) => one.getAttribute('label'));
  const answerOf = (label: string) => {
    const term = [...root.querySelectorAll('dt')].find((one) => text(one).startsWith(label));
    return must(term?.nextElementSibling as HTMLElement | null, `the answer ${label}`);
  };
  const click = async (element: HTMLElement) => {
    element.click();
    await settle(panel);
  };
  return { panel, root, peek, told, button, tab, bar, answerOf, click };
}

beforeAll(registerEnglish);

beforeEach(() => {
  opened.modals = [];
  opened.answers = {};
  opened.confirms = true;
  opened.confirmations = [];
  Object.assign(server, {
    panel: panelOf(),
    opens: 0,
    reveals: [],
    revealed: answer('salary', 'Salary', { sensitive: true, texts: ['72,000'] }),
    timeline: undefined,
    notes: [],
    added: [],
    changedNotes: [],
    edits: [],
    editAnswer: [],
    moves: [],
    moveAnswers: [],
    undos: [],
    deletes: [],
    reruns: [],
    retries: [],
    retryAnswers: [],
    files: [],
    saved: [],
  });
});

afterEach(() => {
  document.body.replaceChildren();
});

describe("the entry panel's header", () => {
  it('names the number beside a row that shows the title, and tells the list the entry was opened', async () => {
    const { root, told } = await open('beside');
    expect(text(root.querySelector('h2'))).toBe('Entry #7');
    expect(root.querySelector('h2')?.classList.contains('visually-hidden')).toBe(false);
    // The row beside it shows the state.
    expect(root.querySelector('.head uui-tag')).toBeNull();
    expect(told).toContainEqual({ name: 'jfb-entry-seen', detail: { key: 'e7', state: 'submitted' } });
  });

  it('says nothing the row shows: beside a row named by its number, the heading is for screen readers only', async () => {
    const { root } = await open('beside', { rowShowsTitle: false });
    expect(root.querySelector('h2')?.classList.contains('visually-hidden')).toBe(true);
  });

  it('adds the title and the state where it covers the list, and the form, linked, where it stands alone', async () => {
    const covering = await open('covering');
    expect(text(covering.root.querySelector('h2'))).toBe('Jane Doe \u00b7 Entry #7');
    expect(text(covering.root.querySelector('.head uui-tag'))).toBe('Waiting for approval');
    document.body.replaceChildren();

    const alone = await open('alone');
    expect(text(alone.root.querySelector('h2'))).toBe('Jane Doe \u00b7 Entry #7');
    const link = must(alone.root.querySelector('a.form-link'), 'the form link');
    expect(text(link)).toBe('Job application');
    expect(link.getAttribute('href')).toBe('section/jfb-forms/workspace/jfb-form/edit/form1/view/entries');
    // Alone, nothing is beside it to close.
    expect(alone.root.querySelector('.head uui-button')).toBeNull();
  });

  it('says so where the entry could not be read', async () => {
    server.panel = undefined;
    const beside = await open('beside');
    expect(text(beside.root.querySelector('[role="alert"]'))).toBe("The entry couldn't be shown.");
  });
});

describe("the entry panel's answers", () => {
  it('starts with the details line, then each answer as it was kept', async () => {
    const { root, answerOf } = await open();
    expect(text(root.querySelector('.details span'))).toBe(
      'Sent from /careers/apply \u00b7 English \u00b7 6 min to complete',
    );
    expect(text(answerOf('Name'))).toBe('Jane Doe');
    expect(text(answerOf('Terms'))).toBe('Yes: \u201cI agree to the terms.\u201d');
    expect(text(answerOf('Signature').querySelector('.line'))).toBe('The signature');
    expect(text(answerOf('Signature').querySelector('.small'))).toBe('Signed as Jane Doe');
    expect(text(answerOf('Reference'))).toBe('not kept');
    expect(text(root.querySelectorAll('dt')[6] ?? null)).toBe('Old question No longer on the form');
    expect(text(answerOf('CV'))).toContain('late.doc \u00b7 Held back while it is checked');
  });

  it('reveals one sensitive value at once, with Hide and the line on the audit log', async () => {
    const { answerOf, click, button } = await open();
    expect(text(answerOf('Salary'))).toContain('Reveal');
    await click(button('Reveal Salary'));
    expect(server.reveals).toEqual(['salary']);
    expect(text(answerOf('Salary').querySelector('.line'))).toBe('72,000');
    expect(text(answerOf('Salary').querySelector('.small'))).toBe('Shown to you and recorded in the audit log.');
    await click(button('Hide'));
    expect(text(answerOf('Salary'))).toContain('Reveal');
  });

  it('keeps a sensitive value hidden from a user who may not see it, without Reveal', async () => {
    server.panel = panelOf({ permissions: ['viewEntries'] });
    const { answerOf } = await open();
    expect(text(answerOf('Salary'))).toBe('Hidden: sensitive');
    expect(answerOf('Salary').querySelector('button')).toBeNull();
  });

  it('opens an image in the viewer and downloads every other file', async () => {
    const { click, button } = await open();
    await click(button('Open photo.png'));
    expect(opened.modals[0]).toEqual({
      token: ENTRY_IMAGE_MODAL_ALIAS,
      data: { form: 'form1', file: 'f2', name: 'photo.png', meta: 'CV \u00b7 image/png, 2 KB' },
    });
    await click(button('Open cv.pdf'));
    expect(server.files).toEqual(['f1']);
    expect(server.saved).toEqual(['cv.pdf']);
  });
});

describe("the entry panel's bar", () => {
  it('holds the state\u2019s actions, the primary on the right, the rest in the "..."', async () => {
    const { bar, root } = await open();
    expect(bar()).toEqual(['More actions for this entry', 'Forward\u2026', 'Mark as spam', 'Reject', 'Approve']);
    const more = [...root.querySelectorAll('.menu button')].map((one) => text(one));
    expect(more).toEqual(['Delete\u2026', 'Re-run actions']);
  });

  it('gives a privacy officer Delete... alone', async () => {
    server.panel = panelOf({ permissions: ['viewEntries', 'viewSensitive', 'deleteEntries', 'privacyTools'] });
    const { bar } = await open();
    expect(bar()).toEqual(['Delete\u2026']);
  });

  it('approves, stays on the entry with the next state\u2019s actions, and offers Undo', async () => {
    const { panel, bar, button, click, peek, told } = await open();
    await click(button('Approve'));
    expect(server.moves).toEqual([{ entries: ['e7'], state: 'approved', overbook: false }]);
    expect(told).toContainEqual({ name: 'jfb-entry-moved', detail: { key: 'e7', state: 'approved' } });
    expect(bar()).toEqual(['More actions for this entry', 'Delete\u2026', 'Forward\u2026', 'Mark as spam']);
    const toast = peek.mock.calls.at(-1)?.[1] as {
      elementName: string;
      data: { message: string; undo(): Promise<void> };
    };
    expect(toast.elementName).toBe('jfb-undo-notification');
    expect(toast.data.message).toBe('Entry #7 approved.');
    await toast.data.undo();
    await settle(panel);
    expect(server.undos).toEqual(['change1']);
    expect(told.at(-1)).toEqual({ name: 'jfb-entry-moved', detail: { key: 'e7', state: 'submitted' } });
    expect(bar().at(-1)).toBe('Approve');
  });

  it('acts on a key only with an action its bar offers', async () => {
    const { panel } = await open();
    panel.act('notSpam');
    await settle(panel);
    expect(server.moves).toEqual([]);
    panel.act('reject');
    await settle(panel);
    expect(server.moves).toEqual([{ entries: ['e7'], state: 'rejected', overbook: false }]);
  });

  it('asks before a move back onto a full option, and overbooks only once the user agrees (ADR-0090)', async () => {
    server.panel = panelOf({ state: 'rejected' });
    const asks = {
      full: [{ field: 'seat', fieldLabel: 'Seat', option: 'front', optionLabel: 'Front row', places: 2, taken: 2 }],
      held: [],
    };
    server.moveAnswers = [{ moved: [], asks }];
    opened.confirms = false;
    const { panel } = await open();
    panel.act('backToWaiting');
    await settle(panel);
    expect(opened.confirmations[0]?.headline).toBe('Move it back anyway?');
    expect(server.moves).toEqual([{ entries: ['e7'], state: 'submitted', overbook: false }]);

    server.moveAnswers = [{ moved: [], asks }];
    opened.confirms = true;
    panel.act('backToWaiting');
    await settle(panel);
    expect(opened.confirmations[1]?.confirmLabel).toBe('Move back anyway');
    expect(server.moves.slice(1)).toEqual([
      { entries: ['e7'], state: 'submitted', overbook: false },
      { entries: ['e7'], state: 'submitted', overbook: true },
    ]);
  });

  it('deletes after asking, and tells the list the entry is gone', async () => {
    const { panel, told, peek } = await open();
    panel.act('delete');
    await settle(panel);
    expect(opened.confirmations[0]).toMatchObject({
      headline: 'Delete entry #7?',
      confirmLabel: 'Delete entry',
    });
    expect(server.deletes).toEqual([false, true]);
    expect(told.at(-1)).toEqual({ name: 'jfb-entry-gone', detail: { key: 'e7', state: 'submitted' } });
    expect(peek.mock.calls.at(-1)?.[1]).toEqual({ data: { message: 'Entry #7 deleted.' } });
  });

  it('forwards through its sidebar, which may offer sensitive values to who may see them', async () => {
    opened.answers[ENTRY_FORWARD_MODAL_ALIAS] = { to: 'Ana' };
    const { panel, peek } = await open();
    panel.act('forward');
    await settle(panel);
    expect(opened.modals[0]).toEqual({
      token: ENTRY_FORWARD_MODAL_ALIAS,
      data: { form: 'form1', entry: 'e7', number: 7, maySendSensitive: true },
    });
    expect(peek.mock.calls.at(-1)?.[1]).toEqual({ data: { message: 'Entry forwarded to Ana.' } });
  });
});

describe('editing answers', () => {
  it('turns the answers into fields, sends only what changed, and says a problem at its field', async () => {
    server.editAnswer = [{ problems: [{ code: 'required', path: 'answers[0].values' }] }];
    const { root, bar, button, click } = await open();
    await click(button('Edit answers'));
    expect(text(root.querySelector('.edit-note'))).toBe('Your changes are recorded, with the old values.');
    expect(bar()).toEqual(['Cancel', 'Save']);
    // A file, a consent and a value the user can't see stay as they are.
    expect(root.querySelectorAll('uui-input, uui-textarea, select').length).toBe(1);
    const input = must(root.querySelector<HTMLInputElement & { value: string }>('uui-input#edit-name'), 'the name');
    input.value = '';
    input.dispatchEvent(new Event('input'));
    await click(button('Save'));
    expect(server.edits).toEqual([{ answers: [{ field: 'name', values: [] }], overbook: false }]);
    expect(text(root.querySelector('#edit-name-problem'))).toBe('Give an answer.');
    expect(input.getAttribute('aria-describedby')).toBe('edit-name-problem');
  });

  it('asks before a change that doubles a unique answer, then saves with the user\u2019s agreement (ADR-0091)', async () => {
    server.editAnswer = [
      { edited: { changed: 0, asks: { full: [], held: [{ field: 'name', fieldLabel: 'Name' }] } }, problems: [] },
    ];
    const { root, button, click, peek } = await open();
    await click(button('Edit answers'));
    const input = must(root.querySelector<HTMLInputElement & { value: string }>('uui-input#edit-name'), 'the name');
    input.value = 'John Roe';
    input.dispatchEvent(new Event('input'));
    await click(button('Save'));
    expect(opened.confirmations[0]).toMatchObject({ headline: 'Save anyway?', confirmLabel: 'Save anyway' });
    expect(server.edits.map((edit) => edit.overbook)).toEqual([false, true]);
    expect(peek.mock.calls.at(-1)?.[1]).toEqual({ data: { message: 'The answers are saved.' } });
    expect(root.querySelector('uui-input')).toBeNull();
  });
});

describe('the timeline and notes', () => {
  it('lists the runs with Retry, and asks before retrying one that may have arrived', async () => {
    server.timeline = {
      events: [
        {
          id: 1,
          when: '2026-11-07T11:00:00Z',
          kind: 'submitted',
          user: null,
          from: null,
          to: null,
          count: null,
          recipient: null,
          changes: [],
        },
      ],
      runs: [
        {
          id: 4,
          when: '2026-11-07T11:01:00Z',
          action: 'a1',
          name: 'Email the team',
          type: 'email',
          state: 'failed',
          attempts: 3,
          error: 'No answer.',
        },
        {
          id: 5,
          when: '2026-11-07T11:02:00Z',
          action: 'a2',
          name: 'Tell the CRM',
          type: 'webhook',
          state: 'uncertain',
          attempts: 1,
          error: null,
        },
      ],
      mail: [],
    };
    const { panel, button, tab, click } = await open();
    await click(tab('Timeline'));
    expect(all(panel, '.event').length).toBe(3);
    await click(button('Retry Email the team'));
    expect(server.retries).toEqual([{ run: 4, confirmed: false }]);
    await click(button('Retry Tell the CRM'));
    expect(opened.confirmations[0]).toMatchObject({ headline: 'Retry this delivery?', confirmLabel: 'Retry anyway' });
    expect(server.retries.at(-1)).toEqual({ run: 5, confirmed: true });
  });

  it('adds a note, newest first, and edits the user\u2019s own', async () => {
    server.notes = [
      { id: 1, author: 'Ben', mine: false, body: 'Called her.', created: '2026-11-07T10:00:00Z', edited: null },
    ];
    const { panel, root, button, tab, click } = await open();
    await click(tab('Notes'));
    expect(text(root.querySelector('#note-help'))).toBe(
      'Visitors never see notes. Everyone who may open this entry can read them.',
    );
    const box = must(root.querySelector<HTMLTextAreaElement>('uui-textarea#note'), 'the note box');
    box.value = 'Invite for an interview.';
    box.dispatchEvent(new Event('input'));
    await settle(panel);
    await click(button('Add note'));
    expect(server.added).toEqual(['Invite for an interview.']);
    expect(all(panel, '.note-body').map((one) => text(one))).toEqual(['Invite for an interview.', 'Called her.']);
    expect(text(root.querySelector('uui-tab:last-of-type'))).toBe('Notes (1)');
    // Only the user's own note offers Edit.
    expect(all(panel, '.note .link').length).toBe(1);
  });
});
