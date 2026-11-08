// Export... and Export selected... (ROADMAP 11.4, IA 6.4, ADR-0210): the sidebar opens with the user's last choices, names
// the entries and their number, offers the fields and leaves out the sensitive ones unless asked, shows "More options"
// only for the formats that take them, saves a file that fits a download, prints a printable view, sends a larger
// export to the background, and lists the user's exports of the form, against a fake of the server.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
  EntryExportPlanResponseModel,
  EntryExportRequestModel,
  EntryExportViewResponseModel,
  EntryListFieldResponseModel,
} from '../api';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { loadEntryWords } from './entry-words';

const field = (
  key: string,
  label: string,
  extra: Partial<EntryListFieldResponseModel> = {},
): EntryListFieldResponseModel => ({
  key,
  label,
  alias: key,
  type: 'text',
  kind: 'text',
  sensitive: false,
  removed: false,
  column: true,
  sorts: true,
  tests: [],
  options: [],
  ...extra,
});

const server = vi.hoisted(() => ({
  view: undefined as unknown as EntryExportViewResponseModel,
  fields: [] as EntryListFieldResponseModel[],
  plan: undefined as unknown as EntryExportPlanResponseModel,
  planned: [] as EntryExportRequestModel[],
  files: [] as EntryExportRequestModel[],
  started: [] as EntryExportRequestModel[],
  fileCode: undefined as string | undefined,
  startCode: undefined as string | undefined,
  /** What a file waits for before it comes. */
  holdFile: Promise.resolve() as Promise<unknown>,
}));

const done = vi.hoisted(() => ({
  saved: [] as string[],
  printed: [] as string[],
  followed: [] as string[],
  downloaded: [] as string[],
}));

vi.mock('./export-api', () => ({
  readExportView: async () => server.view,
  planExport: async (_host: unknown, _form: string, request: EntryExportRequestModel) => {
    server.planned.push(request);
    return { value: server.plan };
  },
  writeExportFile: async (_host: unknown, _form: string, request: EntryExportRequestModel) => {
    server.files.push(request);
    await server.holdFile;
    return server.fileCode ? { code: server.fileCode } : { value: new Blob([`<p>${request.format}</p>`]) };
  },
  startExport: async (_host: unknown, _form: string, request: EntryExportRequestModel) => {
    server.started.push(request);
    return server.startCode
      ? { code: server.startCode }
      : { value: { key: 'k1', state: 'queued', total: server.plan.entries, more: false, done: 0 } };
  },
}));

vi.mock('./entries-api', () => ({
  readView: async () => ({ fields: server.fields, columns: [], filterSets: [] }),
}));

vi.mock('./export-print', () => ({
  printDocument: async (html: string) => (done.printed.push(html), true),
}));

vi.mock('./export-follow', () => ({
  followExport: async (_host: unknown, form: string, status: { key: string }) =>
    void done.followed.push(`${form} ${status.key}`),
  saveExport: async (_notices: unknown, form: string, status: { key: string }) => (
    done.downloaded.push(`${form} ${status.key}`),
    true
  ),
}));

vi.mock('../library/actions/export-form.action', () => ({
  saveBlob: (name: string) => void done.saved.push(name),
}));

await import('./entry-export.modal.element');

const FORMATS = ['csv', 'xlsx', 'json', 'print', 'files'].map((name) => ({
  name,
  displayName: `#jfbExport_format${{ csv: 'Csv', xlsx: 'Excel', json: 'Json', print: 'Print', files: 'Files' }[name]}`,
  writesFiles: name === 'files',
  takesDelimiter: name === 'csv',
  takesHeaders: name !== 'json' && name !== 'files',
  fileName: `contact-entries.${name === 'print' ? 'html' : name === 'files' ? 'zip' : name}`,
}));

function modalContext() {
  const state = new UmbObjectState<object>({});
  return {
    value: state.asObservable(),
    setValue: (next: object) => state.setValue(next),
    updateValue: (partial: object) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function open(data: object = { form: 'f1', filter: { states: [], fields: [], forms: [] } }) {
  const host = testHost();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek: vi.fn(), stay: vi.fn(), getHostElement: () => host } as never);
  const context = modalContext();
  const element = document.createElement('jfb-entry-export-modal');
  element.modalContext = context as never;
  element.data = data as never;
  host.append(element);
  await vi.waitFor(() => expect(shadow(element).querySelector('uui-select, [role="alert"]')).not.toBeNull());
  await settle(element);
  await settle(element);
  const root = shadow(element);
  const button = (label: string) => all(element, 'uui-button').find((one) => one.getAttribute('label') === label);
  const press = async (label: string) => {
    must(button(label), `the button ${label}`).click();
    await settle(element);
    await settle(element);
  };
  const choose = async (format: string) => {
    const select = must(root.querySelector<HTMLSelectElement>('uui-select'), 'the format');
    select.value = format;
    select.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const checkbox = (label: string) =>
    [...root.querySelectorAll<HTMLInputElement>('uui-checkbox')].find((one) => one.getAttribute('label') === label);
  const tick = async (label: string, checked: boolean) => {
    const box = must(checkbox(label), `the box ${label}`);
    box.checked = checked;
    box.dispatchEvent(new Event('change'));
    await settle(element);
  };
  return { element, root, context, button, press, choose, checkbox, tick };
}

beforeAll(async () => {
  registerEnglish();
  await loadEntryWords('en');
});

beforeEach(() => {
  Object.assign(server, {
    view: { formats: FORMATS, maySeeSensitive: true, exports: [] },
    fields: [field('name', 'Name'), field('email', 'Email'), field('salary', 'Salary', { sensitive: true })],
    plan: { entries: 214, more: false, background: false },
    planned: [],
    files: [],
    started: [],
    fileCode: undefined,
    startCode: undefined,
    holdFile: Promise.resolve(),
  });
  Object.assign(done, { saved: [], printed: [], followed: [], downloaded: [] });
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the export sidebar', () => {
  it('names the format, the entries and the fields, and leaves out the sensitive ones unless asked', async () => {
    const { root, button, checkbox } = await open();

    const select = must(root.querySelector('uui-select'), 'the format') as unknown as {
      options: { name: string; selected: boolean }[];
    };
    expect(select.options.map((one) => one.name)).toEqual([
      'CSV',
      'Excel',
      'JSON',
      'Printable view',
      'ZIP of the uploaded files',
    ]);
    expect(select.options.find((one) => one.selected)?.name).toBe('CSV');
    expect(text(root.querySelector('.field p'))).toBe('All 214 entries, no filters');
    expect(checkbox('All fields (2)')?.checked).toBe(true);
    expect(checkbox('Include sensitive fields')?.checked).toBe(false);
    expect(text(root.querySelector('.sensitive .help'))).toBe('Sensitive fields are left out.');
    expect(button('Export 214 entries')?.hasAttribute('disabled')).toBe(false);
    expect(server.planned).toHaveLength(1);
  });

  it('opens with the last choices, names the ticked entries, and offers More options only where they apply', async () => {
    server.view = {
      ...server.view,
      choices: {
        format: 'json',
        fields: ['email'],
        sensitive: false,
        options: { delimiter: 'semicolon', headers: 'aliases', byteOrderMark: false },
      },
    };
    server.plan = { entries: 6, more: false, background: false };
    const { root, button, checkbox, choose, press } = await open({
      form: 'f1',
      entries: ['a', 'b', 'c', 'd', 'e', 'f'],
    });

    expect(text(root.querySelector('.field p'))).toBe('6 entries selected');
    expect(checkbox('All fields (2)')?.checked).toBe(false);
    expect([checkbox('Name')?.checked, checkbox('Email')?.checked]).toEqual([false, true]);
    expect(button('More options'), 'JSON takes none').toBeUndefined();

    await choose('csv');
    await press('More options');
    expect(button('More options')?.getAttribute('aria-expanded')).toBe('true');
    const legends = [...root.querySelectorAll('.options legend')].map((one) => text(one));
    expect(legends).toEqual(['Delimiter', 'Column headers']);
    expect(root.querySelector<HTMLInputElement>('.options uui-radio-group')?.value).toBe('semicolon');
    expect(checkbox('Start with a byte-order mark')?.checked).toBe(false);

    await choose('xlsx');
    expect(
      [...root.querySelectorAll('.options legend')].map((one) => text(one)),
      'Excel takes headers only',
    ).toEqual(['Column headers']);
  });

  it('saves a file that fits a download under its name, with what was chosen', async () => {
    const { tick, press, context } = await open();

    await tick('All fields (2)', false);
    await tick('Name', false);
    await tick('Include sensitive fields', true);
    await press('Export 214 entries');

    expect(server.files).toEqual([
      {
        format: 'csv',
        filter: { states: [], fields: [], forms: [] },
        fields: ['email'],
        sensitive: true,
        options: { delimiter: 'comma', headers: 'labels', byteOrderMark: true },
        remember: true,
      },
    ]);
    expect(done.saved).toEqual(['contact-entries.csv']);
    expect(context.getValue()).toEqual({ done: 'saved' });
    expect(context.submit).toHaveBeenCalled();
  });

  it('neither saves nor prints a file that comes after Cancel', async () => {
    let release: (value: unknown) => void = () => undefined;
    server.holdFile = new Promise((resolve) => (release = resolve));
    const { element, button } = await open();
    must(button('Export 214 entries'), 'the export').click();
    await settle(element);
    expect(server.files).toHaveLength(1);
    // Cancel takes the sidebar away while the file is on its way.
    element.remove();
    release(undefined);
    await settle();
    await settle();
    expect(done.saved).toEqual([]);
    expect(done.printed).toEqual([]);
  });

  it('prints a printable view in the browser, and names its button so', async () => {
    const { choose, press, button } = await open();

    await choose('print');
    expect(button('Print 214 entries')).toBeDefined();
    await press('Print 214 entries');

    expect(done.printed).toEqual(['<p>print</p>']);
    expect(done.saved).toEqual([]);
  });

  it('sends a larger export to the background, which a toast follows, and says when too many are under way', async () => {
    server.plan = { entries: 12400, more: false, background: true };
    const { root, press, context } = await open();

    expect(text(root.querySelector('.field .help'))).toBe(
      'This export continues in the background; a notification links to the file.',
    );
    server.startCode = 'exports_busy';
    await press('Export 12,400 entries');
    expect(text(root.querySelector('[role="alert"]'))).toBe(
      'You have 3 exports under way. Wait for one to finish, then export again.',
    );
    expect(context.submit).not.toHaveBeenCalled();

    server.startCode = undefined;
    await press('Export 12,400 entries');
    expect(done.followed).toEqual(['f1 k1']);
    expect(context.getValue()).toEqual({ done: 'background' });
  });

  it("lists the user's exports of the form, a ready one with Download", async () => {
    server.view = {
      ...server.view,
      exports: [
        { key: 'k2', state: 'ready', total: 12400, more: false, done: 12400, fileName: 'contact-entries.csv' },
        { key: 'k3', state: 'running', total: 20000, more: false, done: 5000 },
      ],
    };
    const { root, press } = await open();

    expect([...root.querySelectorAll('.exports li span')].map((one) => text(one))).toEqual([
      'Your export of 12,400 entries is ready.',
      'Preparing the export: 5,000 of 20,000 entries.',
    ]);
    await press('Download');
    expect(done.downloaded).toEqual(['f1 k2']);
  });

  it('offers the file fields only for the ZIP of the files, and says when there are none', async () => {
    server.fields = [field('name', 'Name'), field('cv', 'CV', { kind: 'file', type: 'file' })];
    const { choose, checkbox } = await open();

    await choose('files');
    expect(checkbox('All file fields (1)')?.checked).toBe(true);

    server.fields = [field('name', 'Name')];
    document.body.replaceChildren();
    const again = await open();
    await again.choose('files');
    expect([...again.root.querySelectorAll('.field .help')].map((help) => text(help))).toContain(
      'This form has no file fields, so the ZIP holds no files.',
    );
  });

  it('sends every ZIP of the files to the background, however few entries it holds', async () => {
    server.fields = [field('name', 'Name'), field('cv', 'CV', { kind: 'file', type: 'file' })];
    const { root, choose, press, context } = await open();
    expect(text(root.querySelector('.field .help')), 'a spreadsheet of 214 entries downloads at once').not.toBe(
      'This export continues in the background; a notification links to the file.',
    );

    await choose('files');
    expect(text(root.querySelector('.field .help'))).toBe(
      'This export continues in the background; a notification links to the file.',
    );
    await press('Export 214 entries');
    expect(server.files, 'never as a download').toEqual([]);
    expect(done.followed).toEqual(['f1 k1']);
    expect(context.getValue()).toEqual({ done: 'background' });
  });
});
