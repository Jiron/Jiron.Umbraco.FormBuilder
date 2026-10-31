// The allowed folders and forms of a form picker's settings (ADR-0135): the chosen ones by name without any data
// type's limits, one the user cannot see any more, read-only, Remove, Choose through the sidebar in the matching mode,
// and a failed lookup. Its reads and the sidebar are replaced by hand-answered fakes.
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { UmbPropertyEditorConfigCollection } from '@umbraco-cms/backoffice/property-editor';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { all, deferred, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { FormPickerAllowedElement } from './form-picker-allowed.element';
import { FORM_PICKER_MODAL, type FormPickerModalValue } from './form-picker-modal.token';
import type { PickerItem } from './picker-data';

const reads = vi.hoisted(() => ({
  forms: vi.fn<(keys: readonly string[]) => Promise<PickerItem[] | undefined>>(),
  folders: vi.fn<(keys: readonly string[]) => Promise<PickerItem[] | undefined>>(),
  made: [] as (string | undefined)[],
}));
const sidebar = vi.hoisted(() => ({ open: vi.fn() }));

vi.mock('./picker-data', async (original) => ({
  ...(await original<typeof import('./picker-data')>()),
  formPickerData: (_host: unknown, dataTypeKey: string | undefined) => {
    reads.made.push(dataTypeKey);
    return reads;
  },
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: sidebar.open,
}));

const { MAX_ALLOWED } = await import('./form-picker-allowed.element');

const folder = (key: string, name: string, hasChildren = true): PickerItem => ({
  key,
  kind: 'folder',
  name,
  alias: null,
  hasChildren,
});
const form = (key: string, name: string, alias: string): PickerItem => ({
  key,
  kind: 'form',
  name,
  alias,
  hasChildren: false,
});
const events = folder('f-events', 'Events');
const archive = folder('f-archive', 'Archive', false);
const planned = folder('f-planned', 'Planned');
const contact = form('a-contact', 'Contact us', 'contact');
const spring = form('c-spring', 'Spring fair', 'springFair');

/** Answers a lookup with the items of those keys the user may see. */
const answer = (known: PickerItem[]) => (keys: readonly string[]) =>
  Promise.resolve(keys.flatMap((key) => known.filter((item) => item.key === key)));

interface Options {
  pick?: 'folders' | 'forms';
  value?: string[];
  readonly?: boolean;
}

let changes: number;

async function setting(options: Options = {}) {
  const host = testHost();
  const element = document.createElement('jfb-form-picker-allowed') as FormPickerAllowedElement;
  element.config = new UmbPropertyEditorConfigCollection([{ alias: 'pick', value: options.pick ?? 'folders' }]);
  element.value = options.value;
  element.readonly = options.readonly ?? false;
  changes = 0;
  element.addEventListener(UmbChangeEvent.TYPE, () => changes++);
  host.append(element);
  await settle(element);
  return element;
}

const nodes = (element: Element) => all(element, 'uui-ref-node');
const names = (element: Element) =>
  nodes(element).map((node) => node.getAttribute('name') ?? text(node.querySelector('[slot="name"]')));
const icons = (element: Element) => nodes(element).map((node) => node.querySelector('umb-icon')?.getAttribute('name'));
const choose = (element: Element) => shadow(element).querySelector<HTMLElement>('#choose');
const chooseButton = (element: Element) => must(choose(element), 'Choose');
const removes = (element: Element) => all(element, 'uui-button.remove');
const remove = (element: Element, index: number) => must(removes(element)[index], `Remove number ${index + 1}`);

beforeAll(registerEnglish);

beforeEach(() => {
  reads.forms.mockReset();
  reads.forms.mockImplementation(answer([contact, spring]));
  reads.folders.mockReset();
  reads.folders.mockImplementation(answer([events, archive, planned]));
  reads.made.length = 0;
  sidebar.open.mockReset();
});

afterEach(() => document.body.replaceChildren());

describe("the form picker's allowed folders", () => {
  it('shows the folders by name in the order chosen, without any data type limits, also one without a published form', async () => {
    const element = await setting({ value: ['f-planned', 'f-archive', 'f-events'] });

    expect(reads.made).toEqual([undefined]);
    expect(reads.folders).toHaveBeenCalledWith(['f-planned', 'f-archive', 'f-events']);
    expect(reads.forms).not.toHaveBeenCalled();
    expect(names(element)).toEqual(['Planned', 'Archive', 'Events']);
    expect(icons(element)).toEqual(['icon-folder', 'icon-folder', 'icon-folder']);
    expect(nodes(element).map((node) => node.getAttribute('detail'))).toEqual(['', '', '']);
    expect(nodes(element).some((node) => node.hasAttribute('standalone'))).toBe(false);
    expect(removes(element).map((button) => button.getAttribute('label'))).toEqual([
      'Remove Planned',
      'Remove Archive',
      'Remove Events',
    ]);
    expect(choose(element)?.getAttribute('look')).toBe('placeholder');
  });

  it('marks a folder the user cannot see any more as not available, with its own Remove', async () => {
    const element = await setting({ value: ['gone', 'f-events'] });

    expect(names(element)).toEqual(['Folder not available', 'Events']);
    const gone = must(nodes(element)[0], 'the first row');
    expect(gone.classList.contains('not-available')).toBe(true);
    expect(gone.getAttribute('detail')).toBe('Deleted, in the trash or not one you can see');
    expect(gone.querySelector('umb-icon')?.getAttribute('name')).toBe('icon-alert');
    expect(removes(element)[0]?.getAttribute('label')).toBe("Remove the folder that isn't available");
  });

  it('shows Choose on its own while no folder is allowed', async () => {
    const element = await setting({});

    expect(reads.folders).not.toHaveBeenCalled();
    expect(nodes(element)).toHaveLength(0);
    expect(choose(element)?.getAttribute('label')).toBe('Choose');
  });

  it('holds rows in their shape while the names load', async () => {
    const lookup = deferred<PickerItem[] | undefined>();
    reads.folders.mockReturnValueOnce(lookup.promise);
    const element = await setting({ value: ['f-events'] });

    expect(all(element, 'uui-ref-list .placeholder')).toHaveLength(1);
    lookup.resolve([events]);
    await settle(element);
    expect(names(element)).toEqual(['Events']);
  });

  it('opens the sidebar in its folder mode with the chosen folders, and writes the keys it answers in order', async () => {
    const element = await setting({ value: ['f-events'] });
    const value: FormPickerModalValue = { selection: ['f-events', 'f-planned'], items: [events, planned] };
    sidebar.open.mockResolvedValueOnce(value);

    chooseButton(element).click();
    await settle(element);
    expect(sidebar.open).toHaveBeenCalledWith(element, FORM_PICKER_MODAL, {
      data: { pick: 'folders', multiple: true, limited: false },
      value: { selection: ['f-events'], items: [events] },
    });
    expect(element.value).toEqual(['f-events', 'f-planned']);
    expect(changes).toBe(1);
    expect(names(element)).toEqual(['Events', 'Planned']);
    expect(reads.folders).toHaveBeenCalledOnce();
  });

  it('keeps the value when the sidebar is closed', async () => {
    const element = await setting({ value: ['f-events'] });
    sidebar.open.mockRejectedValueOnce(undefined);

    chooseButton(element).click();
    await settle(element);
    expect(element.value).toEqual(['f-events']);
    expect(changes).toBe(0);
  });

  it('removes a folder at once, and writes no value once none is left, with the focus on Choose', async () => {
    const element = await setting({ value: ['f-events', 'f-planned'] });
    const nextRemove = vi.spyOn(remove(element, 1), 'focus');

    remove(element, 0).click();
    await settle(element);
    expect(element.value).toEqual(['f-planned']);
    expect(nextRemove).toHaveBeenCalledOnce();

    const chooseFocus = vi.spyOn(chooseButton(element), 'focus');
    remove(element, 0).click();
    await settle(element);
    expect(element.value).toBeUndefined();
    expect(changes).toBe(2);
    expect(chooseFocus).toHaveBeenCalledOnce();
  });

  it('shows the rows only when it is read-only', async () => {
    const element = await setting({ value: ['f-events'], readonly: true });

    expect(names(element)).toEqual(['Events']);
    expect(removes(element)).toHaveLength(0);
    expect(choose(element)).toBeNull();
  });

  it('says when the folders cannot be loaded, and Try again loads them', async () => {
    reads.folders.mockResolvedValueOnce(undefined);
    const element = await setting({ value: ['f-events'] });

    const alert = shadow(element).querySelector('[role="alert"]');
    expect(text(alert?.querySelector('span'))).toBe("The allowed folders couldn't be loaded.");
    (alert?.querySelector('uui-button') as HTMLElement).click();
    await settle(element);
    expect(names(element)).toEqual(['Events']);
    expect(shadow(element).querySelector('[role="alert"]')).toBeNull();
  });

  it('reads keys only, each once, from a value written by hand', async () => {
    const element = await setting({ value: ['F-EVENTS', ' f-events ', 7, ''] as unknown as string[] });

    expect(reads.folders).toHaveBeenCalledWith(['f-events']);
    expect(names(element)).toEqual(['Events']);
  });
});

describe('the allowed keys written in another format', () => {
  it('are named, count once, and are written hyphenated', async () => {
    const key = '7b0c2f4e-1a2b-4c3d-8e9f-0a1b2c3d4e5f';
    const other = folder('1f2e3d4c-5b6a-4978-8695-a4b3c2d1e0f9', 'Other');
    reads.folders.mockImplementation(answer([folder(key, 'Written otherwise'), other]));
    const element = await setting({
      value: [key.replaceAll('-', ''), `{${key.toUpperCase()}}`, other.key],
    });

    expect(reads.folders).toHaveBeenCalledWith([key, other.key]);
    expect(all(element, 'uui-ref-node').map((node) => node.getAttribute('name'))).toEqual([
      'Written otherwise',
      'Other',
    ]);

    must(all(element, 'uui-button.remove')[1], 'the second Remove').click();
    await settle(element);
    expect(element.value).toEqual([key]);
  });
});

describe('the most a setting holds', () => {
  const keys = (count: number) => Array.from({ length: count }, (_, index) => `k-${index}`);

  it('says nothing at the most the queries carry, and says so past it, once', async () => {
    // A failed lookup leaves the rows out, which a thousand of would take this test's whole time to render.
    reads.folders.mockResolvedValue(undefined);
    const message = 'A picker can allow up to 1,000 of these. Remove some, or allow a folder that holds them.';
    const said = (element: Element) => all(element, '[role="alert"] span').map((alert) => text(alert));

    expect(said(await setting({ value: keys(MAX_ALLOWED) }))).not.toContain(message);
    expect(said(await setting({ value: keys(MAX_ALLOWED + 1) })).filter((alert) => alert === message)).toHaveLength(1);
  }, 60_000);

  it('is the number the server refuses past', () => {
    expect(MAX_ALLOWED).toBe(1000);
  });
});

describe("the form picker's allowed forms", () => {
  it('shows the forms by name and alias, without any data type limits', async () => {
    const element = await setting({ pick: 'forms', value: ['c-spring', 'gone'] });

    expect(reads.made).toEqual([undefined]);
    expect(reads.forms).toHaveBeenCalledWith(['c-spring', 'gone']);
    expect(reads.folders).not.toHaveBeenCalled();
    expect(names(element)).toEqual(['Spring fair', 'Form not available']);
    expect(nodes(element)[0]?.getAttribute('detail')).toBe('springFair');
    expect(icons(element)).toEqual(['icon-document', 'icon-alert']);
  });

  it('opens the sidebar for several forms, with the search, and writes what it answers', async () => {
    const element = await setting({ pick: 'forms' });
    sidebar.open.mockResolvedValueOnce({ selection: ['a-contact'], items: [contact] });

    chooseButton(element).click();
    await settle(element);
    expect(sidebar.open.mock.calls[0]?.[2]).toEqual({
      data: { pick: 'forms', multiple: true, limited: false },
      value: { selection: [], items: [] },
    });
    expect(element.value).toEqual(['a-contact']);
    expect(names(element)).toEqual(['Contact us']);
  });

  it('says when the forms cannot be loaded', async () => {
    reads.forms.mockResolvedValueOnce(undefined);
    const element = await setting({ pick: 'forms', value: ['a-contact'] });

    expect(text(shadow(element).querySelector('[role="alert"] span'))).toBe("The allowed forms couldn't be loaded.");
  });
});
