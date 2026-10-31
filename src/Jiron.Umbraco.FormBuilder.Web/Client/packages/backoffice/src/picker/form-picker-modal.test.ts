// The form picker's sidebar (ADR-0134): the library level by level within the data type's limits, the search, one
// form or several, Show more, and the states of each level. Its reads are replaced by hand-answered fakes.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { all, deferred, must, registerEnglish, settle, shadow, text } from '../testing';
import { SEARCH_DELAY, type FormPickerModalElement } from './form-picker-modal.element';
import type { FormPickerModalData, FormPickerModalValue } from './form-picker-modal.token';
import type { PickerItem, PickerPage } from './picker-data';

const reads = vi.hoisted(() => ({
  children: vi.fn<(parentKey: string | undefined, skip: number) => Promise<PickerPage | undefined>>(),
  search: vi.fn<(text: string, skip: number) => Promise<PickerPage | undefined>>(),
  forms: vi.fn<(keys: readonly string[]) => Promise<PickerItem[] | undefined>>(),
  made: [] as (string | undefined)[],
}));

vi.mock('./picker-data', async (original) => ({
  ...(await original<typeof import('./picker-data')>()),
  formPickerData: (_host: unknown, dataTypeKey: string | undefined) => {
    reads.made.push(dataTypeKey);
    return reads;
  },
}));

await import('./form-picker-modal.element');

const folder = (key: string, name: string): PickerItem => ({
  key,
  kind: 'folder',
  name,
  alias: null,
  hasChildren: true,
});
const form = (key: string, name: string, alias: string): PickerItem => ({
  key,
  kind: 'form',
  name,
  alias,
  hasChildren: false,
});
const page = (items: PickerItem[], total = items.length): PickerPage => ({ items, total });

const events = folder('f-events', 'Events');
const contact = form('a-contact', 'Contact us', 'contact');
const newsletter = form('b-news', 'Newsletter', 'newsletter');
const spring = form('c-spring', 'Spring fair', 'springFair');

/** A modal context as Umbraco's holds the value: the sidebar reads it and writes each change back. */
function modalContext(value: FormPickerModalValue) {
  const state = new UmbObjectState<FormPickerModalValue>(value);
  return {
    value: state.asObservable(),
    setValue: (next: FormPickerModalValue) => state.setValue(next),
    updateValue: (partial: Partial<FormPickerModalValue>) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

let context: ReturnType<typeof modalContext>;

/** Opens the sidebar as the property does: for the data type dt-1 unless told there is none. */
async function open(
  data: Partial<FormPickerModalData> & { withoutDataType?: boolean } = {},
  value: FormPickerModalValue = { selection: [], items: [] },
) {
  const { withoutDataType, ...given } = data;
  context = modalContext(value);
  const element = document.createElement('jfb-form-picker-modal') as FormPickerModalElement;
  element.modalContext = context as never;
  element.data = { multiple: false, limited: false, ...(withoutDataType ? {} : { dataTypeKey: 'dt-1' }), ...given };
  document.body.append(element);
  await settle(element);
  return element;
}

const rows = (element: Element) => all<HTMLButtonElement>(element, 'button.row');
const findRow = (element: Element, name: string) =>
  rows(element).find((button) => text(button.querySelector('.name')) === name);
const row = (element: Element, name: string) => must(findRow(element, name), `the row ${name}`);
const findButton = (element: Element, label: string) =>
  all(element, 'uui-button').find((candidate) => candidate.getAttribute('label') === label);
const button = (element: Element, label: string) => must(findButton(element, label), `the button ${label}`);
const choose = (element: Element) => button(element, 'Choose') as HTMLElement & { disabled: boolean };
const names = (element: Element) => rows(element).map((candidate) => text(candidate.querySelector('.name')));

async function type(element: FormPickerModalElement, value: string) {
  const field = must(shadow(element).querySelector<HTMLInputElement>('#search'), 'the search field');
  field.value = value;
  field.dispatchEvent(new Event('input', { bubbles: true, composed: true }));
  await element.updateComplete;
}

beforeAll(registerEnglish);

beforeEach(() => {
  reads.children.mockReset();
  reads.search.mockReset();
  reads.forms.mockReset();
  reads.made.length = 0;
});

afterEach(() => document.body.replaceChildren());

describe('the form picker sidebar', () => {
  it("opens on the top level within the data type's limits, with rows in its shape while it loads", async () => {
    const top = deferred<PickerPage | undefined>();
    reads.children.mockReturnValueOnce(top.promise);
    const element = await open();

    expect(reads.made).toEqual(['dt-1']);
    expect(reads.children).toHaveBeenCalledWith(undefined, 0);
    expect(all(element, '.placeholder')).toHaveLength(3);
    expect(shadow(element).querySelector('.placeholders')?.getAttribute('aria-hidden')).toBe('true');
    expect(shadow(element).querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Choose a form');
    expect(shadow(element).activeElement?.id).toBe('search');

    top.resolve(page([events, contact]));
    await settle(element);
    expect(all(element, '.placeholder')).toHaveLength(0);
    expect(rows(element).map((candidate) => text(candidate))).toEqual(['Events', 'Contact us contact']);
    expect(row(element, 'Events').getAttribute('aria-expanded')).toBe('false');
    expect(row(element, 'Contact us').getAttribute('aria-pressed')).toBe('false');
    expect(choose(element).disabled).toBe(true);
  });

  it('opens a folder in place, loading its level once, and closes it again', async () => {
    reads.children.mockResolvedValueOnce(page([events, contact])).mockResolvedValueOnce(page([spring]));
    const element = await open();

    row(element, 'Events').click();
    await settle(element);
    expect(reads.children).toHaveBeenLastCalledWith('f-events', 0);
    expect(row(element, 'Events').getAttribute('aria-expanded')).toBe('true');
    const nested = row(element, 'Spring fair');
    expect(nested.closest('li')?.parentElement?.closest('li')?.contains(row(element, 'Events'))).toBe(true);
    expect(nested.style.getPropertyValue('--jfb-depth')).toBe('1');

    row(element, 'Events').click();
    await settle(element);
    expect(findRow(element, 'Spring fair')).toBeUndefined();
    expect(row(element, 'Events').getAttribute('aria-expanded')).toBe('false');

    row(element, 'Events').click();
    await settle(element);
    expect(findRow(element, 'Spring fair')).toBeDefined();
    expect(reads.children).toHaveBeenCalledTimes(2);
  });

  it('chooses one form at a time when the picker takes one, and nothing when it is pressed again', async () => {
    reads.children.mockResolvedValueOnce(page([contact, newsletter]));
    const element = await open();

    row(element, 'Contact us').click();
    await settle(element);
    expect(row(element, 'Contact us').getAttribute('aria-pressed')).toBe('true');
    expect(row(element, 'Contact us').querySelector('.check')).not.toBeNull();
    expect(choose(element).disabled).toBe(false);
    expect(context.getValue()).toEqual({ selection: ['a-contact'], items: [contact] });

    row(element, 'Newsletter').click();
    await settle(element);
    expect(row(element, 'Contact us').getAttribute('aria-pressed')).toBe('false');
    expect(context.getValue()).toEqual({ selection: ['b-news'], items: [newsletter] });

    row(element, 'Newsletter').click();
    await settle(element);
    expect(context.getValue().selection).toEqual([]);
    expect(choose(element).disabled).toBe(true);
  });

  it("toggles each form when the picker takes several, in the order picked, keeping the property's picks", async () => {
    reads.children.mockResolvedValueOnce(page([contact, newsletter]));
    const element = await open({ multiple: true }, { selection: ['b-news'], items: [newsletter] });

    expect(shadow(element).querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Choose forms');
    expect(row(element, 'Newsletter').getAttribute('aria-pressed')).toBe('true');
    row(element, 'Contact us').click();
    await settle(element);
    expect(context.getValue()).toEqual({ selection: ['b-news', 'a-contact'], items: [newsletter, contact] });

    row(element, 'Newsletter').click();
    await settle(element);
    expect(context.getValue()).toEqual({ selection: ['a-contact'], items: [contact] });

    button(element, 'Choose').click();
    await settle(element);
    expect(context.submit).toHaveBeenCalledOnce();
  });

  it('searches forms by name or alias after a pause, in place of the levels, and drops an older answer', async () => {
    reads.children.mockResolvedValueOnce(page([events, contact]));
    const older = deferred<PickerPage | undefined>();
    reads.search.mockReturnValueOnce(older.promise).mockResolvedValueOnce(page([contact, newsletter], 2));
    const element = await open();

    await type(element, 'co');
    expect(reads.search).not.toHaveBeenCalled();
    expect(shadow(element).querySelector('uui-loader-circle')).not.toBeNull();
    await new Promise((done) => setTimeout(done, SEARCH_DELAY + 50));
    expect(reads.search).toHaveBeenLastCalledWith('co', 0);

    await type(element, 'con');
    await new Promise((done) => setTimeout(done, SEARCH_DELAY + 50));
    await settle(element);
    older.resolve(page([spring]));
    await settle(element);

    expect(reads.search).toHaveBeenLastCalledWith('con', 0);
    expect(names(element)).toEqual(['Contact us', 'Newsletter']);
    expect(findRow(element, 'Events')).toBeUndefined();
    expect(text(shadow(element).querySelector('[role="status"]'))).toBe('2 forms found');
    expect(shadow(element).querySelector('uui-loader-circle')).toBeNull();
  });

  it('says when a search finds nothing, and Clear search brings the levels back with the focus on the field', async () => {
    reads.children.mockResolvedValueOnce(page([events, contact]));
    reads.search.mockResolvedValueOnce(page([]));
    const element = await open();

    await type(element, 'zzz');
    await new Promise((done) => setTimeout(done, SEARCH_DELAY + 50));
    await settle(element);
    expect(text(shadow(element).querySelector('[role="status"]'))).toBe('No forms match "zzz".');
    expect(text(shadow(element).querySelector('uui-box .message span'))).toBe('No forms match "zzz".');

    must(
      all(element, 'uui-box uui-button').find((candidate) => candidate.getAttribute('label') === 'Clear search'),
      'Clear search in the list',
    ).click();
    await settle(element);
    expect(names(element)).toEqual(['Events', 'Contact us']);
    expect(shadow(element).activeElement?.id).toBe('search');
    expect(reads.children).toHaveBeenCalledOnce();
  });

  it('loads a page more with Show more and moves the focus to the first form it brought', async () => {
    reads.children.mockResolvedValueOnce(page([contact, newsletter], 3)).mockResolvedValueOnce(page([spring], 3));
    const element = await open();

    button(element, 'Show more').click();
    await settle(element);
    expect(reads.children).toHaveBeenLastCalledWith(undefined, 2);
    expect(names(element)).toEqual(['Contact us', 'Newsletter', 'Spring fair']);
    expect(shadow(element).activeElement).toBe(row(element, 'Spring fair'));
    expect(findButton(element, 'Show more')).toBeUndefined();
  });

  it('says when a level cannot be loaded, and Try again loads it', async () => {
    reads.children.mockResolvedValueOnce(undefined).mockResolvedValueOnce(page([contact]));
    const element = await open();

    const alert = shadow(element).querySelector('[role="alert"]');
    expect(text(alert?.querySelector('span'))).toBe("The forms couldn't be loaded.");
    (alert?.querySelector('uui-button') as HTMLElement).click();
    await settle(element);
    expect(names(element)).toEqual(['Contact us']);
    expect(shadow(element).querySelector('[role="alert"]')).toBeNull();
  });

  it('tells an empty library from a data type whose allowed forms are not published yet', async () => {
    reads.children.mockResolvedValue(page([]));
    const library = await open({ limited: false });
    expect(text(shadow(library).querySelector('uui-box .message'))).toBe('There are no published forms to pick yet.');
    library.remove();

    const limited = await open({ limited: true });
    expect(text(shadow(limited).querySelector('uui-box .message'))).toBe(
      'None of the forms allowed here is published yet.',
    );
  });

  it('reads the whole library without a data type, and Close changes nothing', async () => {
    reads.children.mockResolvedValueOnce(page([contact]));
    const element = await open({ withoutDataType: true });
    expect(reads.made).toEqual([undefined]);

    button(element, 'Close').click();
    await settle(element);
    expect(context.reject).toHaveBeenCalledOnce();
    expect(context.submit).not.toHaveBeenCalled();
  });
});
