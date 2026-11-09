// Privacy > People (ROADMAP 13.1 part 13.2, IA 9, ADR-0223): Find by an address or a member lists the person's entries with
// their forms, files and mail; a held entry says until when and releases; Erase\u2026 opens its preview and then says what
// went and where it is recorded; Export everything says how far it is and links the file from a toast. The erase dialog
// names the counts, deletes by default and anonymises when chosen, with Cancel focused; the hold sidebar says each
// problem under its box and sends the day the hold ends.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { EntryExportStatusResponseModel, PersonEntryResponseModel, PersonFoundResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

const api = vi.hoisted(() => ({
  found: undefined as unknown,
  findError: undefined as unknown,
  asked: [] as unknown[],
  preview: undefined as unknown,
  erased: [] as unknown[],
  erase: undefined as unknown,
  held: [] as unknown[],
  holdError: undefined as unknown,
  released: [] as unknown[],
  exported: [] as unknown[],
  export: undefined as unknown,
  global: ['privacyTools', 'manageSettings'] as string[],
  modals: [] as { token: string; data: unknown }[],
  answers: {} as Record<string, unknown>,
  member: undefined as string | undefined,
  followers: [] as { status: unknown; listener: { progress(s: unknown): void; finished(s: unknown): void } }[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: { toString(): string }, args?: { data?: unknown }) => {
    api.modals.push({ token: token.toString(), data: args?.data });
    const answer = api.answers[token.toString()];
    if (answer === undefined) throw new Error('closed');
    return answer;
  },
}));

vi.mock('@umbraco-cms/backoffice/member', () => ({
  UMB_MEMBER_PICKER_MODAL: { toString: () => 'Umb.Modal.MemberPicker' },
  UmbMemberItemRepository: class {
    requestItems = async (keys: string[]) => ({ data: keys.map((unique) => ({ unique, name: 'Ada Lovelace' })) });
  },
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  peopleFind: async (options: { body: unknown }) => {
    api.asked.push(options.body);
    return api.found ? { data: api.found } : { error: api.findError };
  },
  peoplePreview: async () => ({ data: api.preview }),
  peopleErase: async (options: { body: unknown }) => {
    api.erased.push(options.body);
    return { data: api.erase };
  },
  peopleHold: async (options: { body: unknown }) => {
    api.held.push(options.body);
    if (api.holdError) return { error: api.holdError };
    return { data: { entries: (options.body as { entries: unknown[] }).entries.length } };
  },
  peopleRelease: async (options: { body: unknown }) => {
    api.released.push(options.body);
    return { data: { entries: 1 } };
  },
  peopleExport: async (options: { body: unknown }) => {
    api.exported.push(options.body);
    return { data: api.export };
  },
}));

vi.mock('./people-api', async (original) => {
  const actual = await original<typeof import('./people-api')>();
  return {
    ...actual,
    personExportFollower: {
      follow: (
        _form: string,
        status: unknown,
        listener: { progress(s: unknown): void; finished(s: unknown): void },
      ) => {
        api.followers.push({ status, listener });
        return () => undefined;
      },
    },
  };
});

vi.mock('../section/my-permissions', () => ({
  myPermissions: async () => ({ global: api.global, anywhere: api.global }),
}));

const { PeopleScreenElement, looksLikeAddress } = await import('./people-screen.element');
const { PeopleEraseModalElement } = await import('./people-erase.modal.element');
const { PeopleHoldModalElement, holdDays } = await import('./people-hold.modal.element');

const said = (root: ParentNode | null | undefined): string => (root?.textContent ?? '').replace(/\s+/g, ' ').trim();

function entry(more: Partial<PersonEntryResponseModel> = {}): PersonEntryResponseModel {
  return {
    key: '11111111-1111-4111-8111-111111111111',
    number: 12,
    form: { key: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Contact us', trashed: false },
    state: 'approved',
    test: false,
    received: '2026-10-02T09:00:00Z',
    files: 0,
    emails: 2,
    ...more,
  };
}

const FOUND: PersonFoundResponseModel = {
  entries: [
    entry(),
    entry({
      key: '22222222-2222-4222-8222-222222222222',
      number: 3,
      form: { key: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', name: 'Job application', trashed: true },
      files: 2,
      emails: 0,
      heldUntil: '2027-04-30T00:00:00Z',
    }),
    entry({ key: '33333333-3333-4333-8333-333333333333', number: 0, state: 'draft', emails: 1 }),
  ],
  more: false,
  forms: 2,
};

async function screenOf() {
  const host = testHost();
  const peek = vi.fn();
  const stay = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, stay, getHostElement: () => host } as never);
  const screen = new PeopleScreenElement();
  host.append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('#email')).not.toBeNull());
  const root = shadow(screen);
  const find = async (email: string) => {
    const box = must(root.querySelector<HTMLInputElement>('#email'), 'email');
    box.value = email;
    box.dispatchEvent(new Event('input'));
    must(root.querySelector('form'), 'form').dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(screen);
    await settle(screen);
  };
  const button = (label: string) =>
    must(
      [...root.querySelectorAll<HTMLElement>('uui-button, button')].find(
        (one) => one.getAttribute('label') === label || one.textContent?.trim() === label,
      ),
      label,
    );
  return { screen, root, find, button, peek, stay };
}

beforeAll(registerEnglish);

afterEach(() => {
  Object.assign(api, {
    found: undefined,
    findError: undefined,
    asked: [],
    preview: undefined,
    erased: [],
    erase: undefined,
    held: [],
    holdError: undefined,
    released: [],
    exported: [],
    export: undefined,
    global: ['privacyTools', 'manageSettings'],
    modals: [],
    answers: {},
    followers: [],
  });
  document.body.replaceChildren();
});

describe('the People screen', () => {
  it("finds a person's entries by address and lists each with its form, when it came, its files and its mail", async () => {
    api.found = FOUND;
    const { root, find } = await screenOf();

    await find('  lena@example.com ');

    expect(api.asked).toEqual([{ email: 'lena@example.com' }]);
    expect(must(root.querySelectorAll('uui-box')[1], 'results').getAttribute('headline')).toBe('3 entries in 2 forms');
    const rows = [...root.querySelectorAll('tbody tr')];
    expect(rows).toHaveLength(3);
    expect(rows[0]?.querySelector('a')?.getAttribute('href')).toContain('11111111-1111-4111-8111-111111111111');
    expect([...(rows[0]?.querySelectorAll('td.number') ?? [])].map((cell) => text(cell))).toEqual(['none', '2']);
    expect(said(rows[1]?.querySelector('td.wide'))).toBe('Job application In the Recycle Bin');
    expect(said(rows[1]?.querySelector('uui-tag'))).toBe('Legal hold until Apr 30, 2027');
    expect(rows[2]?.querySelector('a')).toBeNull();
    expect(said(rows[2]?.querySelector('td'))).toContain('Saved draft');
  });

  it('says once when the text is not an address, and sends nothing', async () => {
    const { root, find } = await screenOf();

    await find('Lena Weber');

    expect(api.asked).toEqual([]);
    expect(text(root.getElementById('email-why'))).toBe('Enter one email address, such as name@example.com.');
    expect(root.querySelector('#email')?.getAttribute('aria-invalid')).toBe('true');
    expect([looksLikeAddress('a@b'), looksLikeAddress('a b@c'), looksLikeAddress('no-at')]).toEqual([
      true,
      false,
      false,
    ]);
  });

  it('says when nobody is found, and how members are found', async () => {
    api.found = { entries: [], more: false, forms: 0 };
    const { root, find } = await screenOf();

    await find('l.weber@example.com');

    expect(said(root.querySelector('[role="status"]'))).toBe(
      'No entries found for l.weber@example.com. Entries sent while signed in are found through the member.',
    );
  });

  it('finds by a member chosen with the member picker, named as the picker names them', async () => {
    api.found = { entries: [entry()], more: false, forms: 1 };
    api.answers['Umb.Modal.MemberPicker'] = { selection: ['cccccccc-cccc-4ccc-8ccc-cccccccccccc'] };
    const { root, screen, button } = await screenOf();

    button('Find a member instead').click();
    await settle(screen);
    button('Choose a member').click();
    await vi.waitFor(() => expect(api.asked).toHaveLength(1));
    await settle(screen);

    expect(api.asked).toEqual([{ member: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc' }]);
    expect(said(root.querySelector('.member'))).toContain('Member: Ada Lovelace');
  });

  it('releases a legal hold from its row', async () => {
    api.found = FOUND;
    const { root, find, screen, peek } = await screenOf();
    await find('lena@example.com');

    must(root.querySelector<HTMLButtonElement>('.held button'), 'Release').click();
    await vi.waitFor(() => expect(peek).toHaveBeenCalled());
    await settle(screen);

    expect(api.released).toEqual([{ email: 'lena@example.com', entries: ['22222222-2222-4222-8222-222222222222'] }]);
    expect(root.querySelector('uui-tag')).toBeNull();
  });

  it('sets a hold through its sidebar, with the entries sent, and shows it in their rows', async () => {
    api.found = FOUND;
    api.answers['Jiron.FormBuilder.Modal.PeopleHold'] = {
      held: 1,
      keys: ['11111111-1111-4111-8111-111111111111'],
      until: '2027-01-15T00:00:00Z',
    };
    const { root, find, screen, button } = await screenOf();
    await find('lena@example.com');

    button('Set legal hold\u2026').click();
    await vi.waitFor(() => expect(api.modals).toHaveLength(1));
    await settle(screen);

    const data = api.modals[0]?.data as { entries: PersonEntryResponseModel[] };
    expect(data.entries.map((each) => each.number)).toEqual([12, 3]);
    expect(said(root.querySelector('tbody tr uui-tag'))).toBe('Legal hold until Jan 15, 2027');
  });

  it('opens the erase preview, then says what went, with the audit log for those who may open it', async () => {
    api.found = FOUND;
    api.preview = { entries: 1, drafts: 1, held: 1, files: 0, emails: 3, more: false, forms: [] };
    api.answers['Jiron.FormBuilder.Modal.PeopleErase'] = {
      erased: { mode: 'delete', entries: 1, drafts: 1, files: 0, emails: 3, held: 1, auditId: 41 },
    };
    const { root, find, screen, button } = await screenOf();
    await find('lena@example.com');

    button('Erase\u2026').click();
    await vi.waitFor(() => expect(root.querySelector('.done')).not.toBeNull());
    await settle(screen);

    expect(api.modals[0]?.data).toMatchObject({ person: { email: 'lena@example.com' }, who: 'lena@example.com' });
    expect(said(root.querySelector('.done'))).toBe(
      'Everything about lena@example.com is erased: 1 entry, 1 saved draft and 3 emails. Statistics stay. 1 entry on legal hold stays until its hold ends. Open the audit log',
    );
    expect(root.querySelector('table')).toBeNull();
  });

  it('says what an anonymise kept and what it deleted, leaving out what there was none of', async () => {
    api.found = FOUND;
    api.preview = { entries: 1, drafts: 0, held: 0, files: 2, emails: 0, more: false, forms: [] };
    api.answers['Jiron.FormBuilder.Modal.PeopleErase'] = {
      erased: { mode: 'anonymise', entries: 1, drafts: 0, files: 2, emails: 0, held: 0, auditId: 42 },
    };
    const { root, find, screen, button } = await screenOf();
    await find('lena@example.com');

    button('Erase\u2026').click();
    await vi.waitFor(() => expect(root.querySelector('.done')).not.toBeNull());
    await settle(screen);
    expect(said(root.querySelector('.done p'))).toBe(
      'Everything about lena@example.com is erased: 1 entry anonymised, which stays in the counts, and 2 files deleted. Statistics stay.',
    );
  });

  it('says how far the export is, and links the file from a toast once it is ready', async () => {
    api.found = FOUND;
    const queued: EntryExportStatusResponseModel = { key: 'e1', state: 'queued', total: 3, more: false, done: 0 };
    api.export = queued;
    const { root, find, screen, button, stay } = await screenOf();
    await find('lena@example.com');

    button('Export everything').click();
    await vi.waitFor(() => expect(api.followers).toHaveLength(1));
    api.followers[0]?.listener.progress({ ...queued, state: 'running', done: 2 });
    await settle(screen);
    expect(said(root.querySelector('.progress'))).toBe(
      'Preparing the export: 2 of 3 entries. You can keep working; a notification links to the file.',
    );
    expect(button('Export everything').hasAttribute('disabled')).toBe(true);

    api.followers[0]?.listener.finished({
      ...queued,
      state: 'ready',
      done: 3,
      fileName: 'personal-data-2026-11-09.zip',
    });
    await settle(screen);
    expect(root.querySelector('.progress')).toBeNull();
    expect(stay).toHaveBeenCalledWith('positive', expect.objectContaining({ elementName: expect.any(String) }));
  });
});

function modalContext() {
  return {
    value: new UmbObjectState({}).asObservable(),
    getValue: () => ({}),
    setValue: vi.fn(),
    updateValue: vi.fn(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

describe('the erase dialog', () => {
  async function dialogOf() {
    const element = new PeopleEraseModalElement();
    const context = modalContext();
    element.modalContext = context as never;
    element.data = {
      person: { email: 'lena@example.com' },
      who: 'lena@example.com',
      preview: {
        entries: 5,
        drafts: 1,
        held: 1,
        files: 2,
        emails: 4,
        more: false,
        forms: [
          { key: 'a', name: 'Contact us', trashed: false, entries: 2, drafts: 1, held: 0, files: 0, emails: 3 },
          { key: 'b', name: 'Job application', trashed: false, entries: 3, drafts: 0, held: 1, files: 2, emails: 1 },
        ],
      },
    };
    document.body.append(element);
    await settle(element);
    return { element, root: shadow(element), context };
  }

  it('says what goes, per form, and deletes by default with Cancel focused', async () => {
    api.erase = { mode: 'delete', entries: 5, drafts: 1, files: 2, emails: 4, held: 1, auditId: 7 };
    const { element, root, context } = await dialogOf();

    expect(must(root.querySelector('uui-dialog-layout'), 'layout').getAttribute('headline')).toBe(
      'Erase everything about lena@example.com?',
    );
    expect(text(root.getElementById('what'))).toBe(
      "6 entries in 2 forms, 2 files and 4 emails are erased. Statistics stay. You can't undo this.",
    );
    expect(text(root.querySelector('#what + p'))).toBe('1 entry on legal hold stays until its hold ends.');
    expect([...root.querySelectorAll('.forms dd')].map((cell) => text(cell))).toEqual([
      '2 entries, 1 saved draft, 3 emails',
      '3 entries, 2 files, 1 email, 1 on legal hold',
    ]);
    await vi.waitFor(() => expect(element.shadowRoot?.activeElement?.id).toBe('cancel'));

    must(root.querySelector<HTMLElement>('uui-button[color="danger"]'), 'Erase').click();
    await vi.waitFor(() => expect(context.submit).toHaveBeenCalled());
    expect(api.erased).toEqual([{ email: 'lena@example.com', mode: 'delete' }]);
  });

  it('anonymises when chosen', async () => {
    api.erase = { mode: 'anonymise', entries: 5, drafts: 1, files: 2, emails: 4, held: 1, auditId: 7 };
    const { root, element, context } = await dialogOf();

    must([...root.querySelectorAll<HTMLInputElement>('input[type="radio"]')][1], 'anonymise').dispatchEvent(
      new Event('change'),
    );
    await settle(element);
    must(root.querySelector<HTMLElement>('uui-button[color="danger"]'), 'Erase').click();
    await vi.waitFor(() => expect(context.submit).toHaveBeenCalled());

    expect(api.erased).toEqual([{ email: 'lena@example.com', mode: 'anonymise' }]);
  });
});

describe('the legal hold sidebar', () => {
  async function sidebarOf() {
    const element = new PeopleHoldModalElement();
    const context = modalContext();
    element.modalContext = context as never;
    element.data = {
      person: { email: 'lena@example.com' },
      entries: [entry(), entry({ key: 'k2', number: 3, heldUntil: '2027-04-30T00:00:00Z' })],
    };
    document.body.append(element);
    await settle(element);
    const root = shadow(element);
    const set = () => must([...root.querySelectorAll<HTMLElement>('uui-button')].at(-1), 'Set').click();
    return { element, root, context, set };
  }

  it('says each problem under what it is about, and sends nothing', async () => {
    const { element, root, set } = await sidebarOf();

    set();
    await settle(element);

    expect([...root.querySelectorAll('.why')].map((one) => text(one))).toEqual([
      'Choose at least one entry.',
      'Give the reason on one line, in at most 500 characters.',
      'Choose a day after today and at most a year ahead.',
    ]);
    expect(api.held).toEqual([]);
  });

  it('sends the entries chosen, the reason and the day it ends, and answers them', async () => {
    const { element, root, context, set } = await sidebarOf();
    const { first } = holdDays(new Date());
    must(root.querySelector<HTMLInputElement>('input[type="checkbox"]'), 'entry').checked = true;
    must(root.querySelector<HTMLInputElement>('input[type="checkbox"]'), 'entry').dispatchEvent(new Event('change'));
    const reason = must(root.querySelector<HTMLInputElement>('#reason'), 'reason');
    reason.value = 'Pending court case';
    reason.dispatchEvent(new Event('input'));
    const until = must(root.querySelector<HTMLInputElement>('#until'), 'until');
    until.value = first;
    until.dispatchEvent(new Event('input'));
    await settle(element);

    expect(said(root.querySelectorAll('.pick')[1])).toContain('Legal hold until Apr 30, 2027');
    set();
    await vi.waitFor(() => expect(context.submit).toHaveBeenCalled());

    expect(api.held).toEqual([
      {
        email: 'lena@example.com',
        entries: ['11111111-1111-4111-8111-111111111111'],
        until: `${first}T00:00:00Z`,
        reason: 'Pending court case',
      },
    ]);
    expect(context.setValue).toHaveBeenCalledWith({
      held: 1,
      keys: ['11111111-1111-4111-8111-111111111111'],
      until: `${first}T00:00:00Z`,
    });
  });

  it.each([
    ['hold_reason', 'reason', 'Give the reason on one line, in at most 500 characters.'],
    ['hold_end', 'until', 'Choose a day after today and at most a year ahead.'],
  ])("says the server's %s under its box and stays open", async (code, box, words) => {
    const { element, root, context, set } = await sidebarOf();
    const { first } = holdDays(new Date());
    const pick = must(root.querySelector<HTMLInputElement>('input[type="checkbox"]'), 'entry');
    pick.checked = true;
    pick.dispatchEvent(new Event('change'));
    for (const [id, value] of [
      ['reason', 'Pending court case'],
      ['until', first],
    ] as const) {
      const input = must(root.querySelector<HTMLInputElement>(`#${id}`), id);
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    api.holdError = new UmbApiError('refused', 400, null, { code } as never);
    await settle(element);

    set();
    await vi.waitFor(() => expect(api.held).toHaveLength(1));
    await settle(element);

    expect([...root.querySelectorAll('.why')].map((one) => text(one))).toEqual([words]);
    expect(must(root.querySelector(`#${box}`), box).getAttribute('aria-invalid')).toBe('true');
    expect(context.submit).not.toHaveBeenCalled();
  });
});
