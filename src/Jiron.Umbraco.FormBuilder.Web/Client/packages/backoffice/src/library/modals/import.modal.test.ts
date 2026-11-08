// The import sidebar's two jobs (ADR-0046, ADR-0166, ADR-0220): a new copy in a folder, or "Replace from a file..." on a
// form, where the same steps check the file against that form, say that it becomes the form's draft (and, in the form's
// own workspace, that changes not saved yet are discarded), and send the form's version with the file. A form someone
// changed since the action read it is refused in the editor's words.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { ImportCheckResponseModel } from '../../api';
import { must, registerEnglish, settle, shadow, text } from '../../testing';
import type { ImportModalData } from './tokens';

const calls = {
  checkedImport: [] as unknown[],
  checkedReplace: [] as unknown[],
  replaced: [] as unknown[],
  replaceAnswer: undefined as unknown,
};

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const CHECK: ImportCheckResponseModel = {
  name: 'Contact',
  alias: 'contact',
  packageVersion: '1.0.0',
  exportedUtc: '2026-11-01T10:00:00Z',
  holds: { pages: 1, fields: 3, rules: 0, outcomes: 1, actions: 0 },
  notes: [],
};

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  folderGet: async () => ({ data: { name: 'Events' } }),
  formExchangeCheckImport: async (options: unknown) => {
    calls.checkedImport.push(options);
    return { data: CHECK };
  },
  formExchangeCheckReplace: async (options: unknown) => {
    calls.checkedReplace.push(options);
    return { data: CHECK };
  },
  formExchangeReplace: async (options: unknown) => {
    calls.replaced.push(options);
    return calls.replaceAnswer;
  },
}));

const { ImportModalElement } = await import('./import.modal.element');

async function sidebarOf(data: ImportModalData) {
  const element = new ImportModalElement();
  const updateValue = vi.fn();
  const submit = vi.fn();
  element.modalContext = {
    value: new UmbObjectState({}).asObservable(),
    getValue: () => ({}),
    setValue: () => undefined,
    updateValue,
    submit,
    reject: vi.fn(),
  } as never;
  element.data = data;
  document.body.append(element);
  await settle(element);
  return { element, root: shadow(element), updateValue, submit };
}

async function choose(element: InstanceType<typeof ImportModalElement>, root: ShadowRoot, json = '{"form":"contact"}') {
  const input = must(root.querySelector<HTMLInputElement>('input[type=file]'), 'the file input');
  Object.defineProperty(input, 'files', {
    configurable: true,
    value: [{ name: 'contact.json', size: json.length, text: async () => json }],
  });
  input.dispatchEvent(new Event('change'));
  for (let i = 0; i < 5; i++) await settle(element);
}

const primary = (root: ShadowRoot) => must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'the button');
const headline = (root: ShadowRoot) => root.querySelector('umb-body-layout')?.getAttribute('headline');

const TARGET = { formKey: 'f1', name: 'Contact', concurrencyVersion: 7, unsaved: false };
const REPLACE: ImportModalData = { folderKey: null, replace: TARGET };

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  calls.checkedImport = [];
  calls.checkedReplace = [];
  calls.replaced = [];
  calls.replaceAnswer = undefined;
});

describe('the import sidebar for a new copy', () => {
  it('checks the file for its folder and imports it as a new copy', async () => {
    const { element, root } = await sidebarOf({ folderKey: 'folder1' });
    expect(headline(root)).toBe('Import a form');
    expect(primary(root).getAttribute('label')).toBe('Import as a new copy');
    await choose(element, root);
    expect(calls.checkedImport).toEqual([{ body: { json: '{"form":"contact"}', folderKey: 'folder1' } }]);
    expect(calls.checkedReplace).toEqual([]);
    expect(text(root.querySelector('umb-body-layout > p.muted'))).toContain('It arrives as a draft in Events');
  });
});

describe("the import sidebar replacing a form's draft", () => {
  it("names the form, checks the file against it and says it becomes the form's draft", async () => {
    const { element, root } = await sidebarOf(REPLACE);
    expect(headline(root)).toBe('Replace Contact from a file');
    expect(text(root.querySelector('uui-box p'))).toContain('Its form becomes the draft of this form');
    expect(primary(root).getAttribute('label')).toBe('Replace the draft');
    expect(primary(root).hasAttribute('disabled')).toBe(true);
    await choose(element, root);
    expect(calls.checkedReplace).toEqual([{ path: { key: 'f1' }, body: { json: '{"form":"contact"}' } }]);
    expect(calls.checkedImport).toEqual([]);
    const said = text(root.querySelector('umb-body-layout > p.muted'));
    expect(said).toBe('It becomes the draft of Contact. Visitors keep seeing the published version until you publish.');
    expect(primary(root).hasAttribute('disabled')).toBe(false);
  });

  it("says that changes not saved yet are discarded, in the form's own workspace with some", async () => {
    const { element, root } = await sidebarOf({ ...REPLACE, replace: { ...TARGET, unsaved: true } });
    await choose(element, root);
    expect(text(root.querySelector('umb-body-layout > p.muted'))).toContain(
      "Changes you haven't saved yet are discarded.",
    );
  });

  it("sends the file with the form's version, and ends with the form", async () => {
    calls.replaceAnswer = { data: { form: { key: 'f1' }, version: 8, notes: [] } };
    const { element, root, updateValue, submit } = await sidebarOf(REPLACE);
    await choose(element, root);
    primary(root).dispatchEvent(new Event('click'));
    for (let i = 0; i < 5; i++) await settle(element);
    expect(calls.replaced).toEqual([
      { path: { key: 'f1' }, body: { json: '{"form":"contact"}', concurrencyVersion: 7 } },
    ]);
    expect(updateValue).toHaveBeenLastCalledWith({ formKey: 'f1' });
    expect(submit).toHaveBeenCalledOnce();
  });

  it('refuses a form someone changed since the action read it, in the words for that', async () => {
    calls.replaceAnswer = {
      error: new UmbApiError('changed', 409, null, { code: 'concurrency_conflict' } as never),
    };
    const { element, root, submit } = await sidebarOf(REPLACE);
    await choose(element, root);
    primary(root).dispatchEvent(new Event('click'));
    for (let i = 0; i < 5; i++) await settle(element);
    expect(text(root.querySelector('[role="alert"]'))).toBe(
      'Someone changed this after you opened it. Reload the page, then try again.',
    );
    expect(submit).not.toHaveBeenCalled();
  });
});
