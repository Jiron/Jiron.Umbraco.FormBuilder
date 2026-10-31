// The form picker's property editor (ADR-0134): the picked forms by name within the data type's limits, one form or
// several, a form the picker cannot show, read-only, Remove, Choose through the sidebar, a failed lookup and a
// mandatory picker. Its reads and the sidebar are replaced by hand-answered fakes.
import { UmbPropertyTypeBasedPropertyContext } from '@umbraco-cms/backoffice/content';
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { UmbPropertyEditorConfigCollection } from '@umbraco-cms/backoffice/property-editor';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { all, deferred, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { FormPickerElement } from './form-picker.element';
import { FORM_PICKER_MODAL, type FormPickerModalValue } from './form-picker-modal.token';
import type { PickerItem } from './picker-data';

const reads = vi.hoisted(() => ({
  children: vi.fn(),
  search: vi.fn(),
  forms: vi.fn<(keys: readonly string[]) => Promise<PickerItem[] | undefined>>(),
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

await import('./form-picker.element');

const form = (key: string, name: string, alias: string): PickerItem => ({
  key,
  kind: 'form',
  name,
  alias,
  hasChildren: false,
});
const contact = form('a-contact', 'Contact us', 'contact');
const newsletter = form('b-news', 'Newsletter', 'newsletter');
const spring = form('c-spring', 'Spring fair', 'springFair');
const all3 = [contact, newsletter, spring];

/** Answers a lookup with the forms of those keys the picker may show. */
const answer = (known: PickerItem[]) => (keys: readonly string[]) =>
  Promise.resolve(keys.flatMap((key) => known.filter((item) => item.key === key)));

interface Options {
  value?: string;
  multiple?: boolean;
  allowedForms?: string[];
  readonly?: boolean;
  mandatory?: boolean;
  /** The data type the content editor's property context gives, or none outside the content editor. */
  dataType?: string | null;
}

let changes: number;

async function picker(options: Options = {}) {
  const host = testHost();
  const dataType = options.dataType === undefined ? 'dt-1' : options.dataType;
  if (dataType) new UmbPropertyTypeBasedPropertyContext(host).setDataType({ unique: dataType });
  const element = document.createElement('jfb-form-picker') as FormPickerElement;
  element.config = new UmbPropertyEditorConfigCollection([
    { alias: 'multiple', value: options.multiple ?? false },
    { alias: 'allowedFolders', value: [] },
    { alias: 'allowedForms', value: options.allowedForms ?? [] },
  ]);
  element.value = options.value;
  element.readonly = options.readonly ?? false;
  element.mandatory = options.mandatory ?? false;
  changes = 0;
  element.addEventListener(UmbChangeEvent.TYPE, () => changes++);
  host.append(element);
  await settle(element);
  return element;
}

const nodes = (element: Element) => all(element, 'uui-ref-node');
const names = (element: Element) => nodes(element).map((node) => node.getAttribute('name'));
const choose = (element: Element) => shadow(element).querySelector<HTMLElement>('#choose');
const chooseButton = (element: Element) => must(choose(element), 'Choose');
const removes = (element: Element) => all(element, 'uui-button.remove');
const remove = (element: Element, index: number) => must(removes(element)[index], `Remove number ${index + 1}`);

beforeAll(registerEnglish);

beforeEach(() => {
  reads.forms.mockReset();
  reads.forms.mockImplementation(answer(all3));
  reads.made.length = 0;
  sidebar.open.mockReset();
});

afterEach(() => document.body.replaceChildren());

describe('the form picker property', () => {
  it("shows the picked form by name and alias, looked up within the data type's limits, and no Choose once it holds one", async () => {
    const element = await picker({ value: 'a-contact' });

    expect(reads.made).toEqual(['dt-1']);
    expect(reads.forms).toHaveBeenCalledWith(['a-contact']);
    expect(names(element)).toEqual(['Contact us']);
    expect(nodes(element)[0]?.getAttribute('detail')).toBe('contact');
    expect(nodes(element)[0]?.hasAttribute('readonly')).toBe(true);
    expect(nodes(element)[0]?.hasAttribute('standalone')).toBe(true);
    expect(nodes(element)[0]?.querySelector('umb-icon')?.getAttribute('name')).toBe('icon-document');
    expect(removes(element).map((remove) => remove.getAttribute('label'))).toEqual(['Remove Contact us']);
    expect(text(removes(element)[0])).toBe('Remove');
    expect(choose(element)).toBeNull();
  });

  it('shows several forms in the order they were picked, with Choose below them', async () => {
    const element = await picker({ value: 'c-spring,a-contact', multiple: true });

    expect(names(element)).toEqual(['Spring fair', 'Contact us']);
    expect(nodes(element).some((node) => node.hasAttribute('standalone'))).toBe(false);
    expect(choose(element)?.getAttribute('label')).toBe('Choose');
    expect(choose(element)?.getAttribute('look')).toBe('placeholder');
  });

  it('shows Choose on its own while nothing is picked', async () => {
    const element = await picker({});

    expect(reads.forms).not.toHaveBeenCalled();
    expect(nodes(element)).toHaveLength(0);
    expect(choose(element)).not.toBeNull();
  });

  it('marks a picked form the picker cannot show, as Umbraco marks an item it cannot read, and offers to remove it', async () => {
    const element = await picker({ value: 'gone,b-news', multiple: true });

    expect(names(element)).toEqual(['Form not available', 'Newsletter']);
    const gone = must(nodes(element)[0], 'the first row');
    expect(gone.classList.contains('not-available')).toBe(true);
    expect(gone.getAttribute('detail')).toBe(
      "It may have been deleted or unpublished, or it isn't one you can pick here. Remove it, or choose another form.",
    );
    expect(gone.querySelector('umb-icon')?.getAttribute('name')).toBe('icon-alert');
    expect(removes(element)[0]?.getAttribute('label')).toBe("Remove the form that isn't available");
  });

  it('holds rows in their shape while the names load', async () => {
    const lookup = deferred<PickerItem[] | undefined>();
    reads.forms.mockReturnValueOnce(lookup.promise);
    const element = await picker({ value: 'a-contact,b-news', multiple: true });

    expect(all(element, 'uui-ref-list .placeholder')).toHaveLength(2);
    expect(all(element, '.placeholder').every((row) => row.getAttribute('aria-hidden') === 'true')).toBe(true);
    lookup.resolve([contact, newsletter]);
    await settle(element);
    expect(all(element, '.placeholder')).toHaveLength(0);
    expect(names(element)).toEqual(['Contact us', 'Newsletter']);
  });

  it('shows the rows only when it is read-only: no Remove and no Choose', async () => {
    const element = await picker({ value: 'a-contact,gone', multiple: true, readonly: true });

    expect(names(element)).toEqual(['Contact us', 'Form not available']);
    expect(removes(element)).toHaveLength(0);
    expect(choose(element)).toBeNull();
  });

  it('removes a form at once, writes the value and moves the focus to the next Remove, then to Choose', async () => {
    // jsdom keeps no focus inside UUI's slotted buttons, so the test watches where the focus is asked to go; the
    // review of 6.7f follows the real focus in Edge.
    const element = await picker({ value: 'a-contact,b-news', multiple: true });
    const nextRemove = vi.spyOn(remove(element, 1), 'focus');
    const chooseFocus = vi.spyOn(chooseButton(element), 'focus');

    remove(element, 0).click();
    await settle(element);
    expect(element.value).toBe('b-news');
    expect(changes).toBe(1);
    expect(names(element)).toEqual(['Newsletter']);
    expect(nextRemove).toHaveBeenCalledOnce();

    remove(element, 0).click();
    await settle(element);
    expect(element.value).toBeUndefined();
    expect(changes).toBe(2);
    expect(chooseFocus).toHaveBeenCalledOnce();
    expect(reads.forms).toHaveBeenCalledOnce();
  });

  it("opens the sidebar with the picker's settings and picks, and writes what it answers without a second lookup", async () => {
    const element = await picker({ value: 'a-contact', multiple: true, allowedForms: ['a-contact', 'c-spring'] });
    const value: FormPickerModalValue = { selection: ['a-contact', 'c-spring'], items: [contact, spring] };
    sidebar.open.mockResolvedValueOnce(value);

    chooseButton(element).click();
    await settle(element);
    expect(sidebar.open).toHaveBeenCalledWith(element, FORM_PICKER_MODAL, {
      data: { multiple: true, limited: true, dataTypeKey: 'dt-1' },
      value: { selection: ['a-contact'], items: [contact] },
    });
    expect(element.value).toBe('a-contact,c-spring');
    expect(changes).toBe(1);
    expect(names(element)).toEqual(['Contact us', 'Spring fair']);
    expect(reads.forms).toHaveBeenCalledOnce();
  });

  it('keeps the value when the sidebar is closed', async () => {
    const element = await picker({});
    sidebar.open.mockRejectedValueOnce(undefined);

    chooseButton(element).click();
    await settle(element);
    expect(sidebar.open.mock.calls[0]?.[2]).toEqual({
      data: { multiple: false, limited: false, dataTypeKey: 'dt-1' },
      value: { selection: [], items: [] },
    });
    expect(element.value).toBeUndefined();
    expect(changes).toBe(0);
  });

  it('says when the picked forms cannot be loaded, and Try again loads them', async () => {
    reads.forms.mockResolvedValueOnce(undefined);
    const element = await picker({ value: 'a-contact' });

    const alert = shadow(element).querySelector('[role="alert"]');
    expect(text(alert?.querySelector('span'))).toBe("The picked forms couldn't be loaded.");
    expect(nodes(element)).toHaveLength(0);
    (alert?.querySelector('uui-button') as HTMLElement).click();
    await settle(element);
    expect(names(element)).toEqual(['Contact us']);
    expect(shadow(element).querySelector('[role="alert"]')).toBeNull();
  });

  it('limits nothing outside the content editor, where no data type is known', async () => {
    await picker({ value: 'a-contact', dataType: null });

    expect(reads.made).toEqual([undefined]);
  });

  it('is invalid when it is mandatory and holds no form, and valid once it holds one', async () => {
    const element = await picker({ mandatory: true });

    expect(element.checkValidity()).toBe(false);
    expect(element.validity.valueMissing).toBe(true);
    expect(element.validationMessage).toBe('#validation_invalidEmpty');

    element.value = 'a-contact';
    await settle(element);
    expect(element.checkValidity()).toBe(true);
  });
});
