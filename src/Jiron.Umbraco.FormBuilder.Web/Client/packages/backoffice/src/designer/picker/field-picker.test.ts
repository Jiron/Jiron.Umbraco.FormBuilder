// The field picker and the Paste questions dialog (ROADMAP 9.3b and 22.1, ADR-0256): the picker's search with the focus,
// Layout, the types in their groups in the backoffice's words and the saved groups, in reading order; a search that
// narrows them, marks the first match and adds it with Enter, Arrow down to the first item, and what it says when
// nothing is found; what each pick answers; an unavailable type's reason read where it is, never picked; Paste
// questions... and Close; and the dialog's fields to add with their guessed types and its count.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { FieldTypesResponseModel } from '../../api';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import { builtInTypes } from '../preview/fixtures';
import { FieldPickerModalElement, folded } from './field-picker.modal.element';
import type { FieldPickerModalData, FieldPickerModalValue } from './field-picker.modal.token';
import { PasteQuestionsModalElement } from './paste-questions.modal.element';

const served = JSON.parse(
  readFileSync(
    join(dirname(fileURLToPath(import.meta.url)), ...Array<string>(8).fill('..'), 'docs', 'api', 'field-types.json'),
    'utf8',
  ),
) as FieldTypesResponseModel;

/** A modal context as Umbraco's holds the value. */
function modalContext() {
  const state = new UmbObjectState<Partial<FieldPickerModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: FieldPickerModalValue) => state.setValue(next),
    updateValue: (partial: Partial<FieldPickerModalValue>) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function pickerOf(extra: Partial<FieldPickerModalData> = {}) {
  const context = modalContext();
  const element = new FieldPickerModalElement();
  element.modalContext = context as never;
  element.data = { types: served.types, groups: served.paletteGroups, saved: [], ...extra };
  document.body.append(element);
  await settle(element);
  const root = shadow(element);
  const search = must(root.querySelector<HTMLInputElement>('uui-input'), 'the search');
  const type = async (query: string) => {
    search.value = query;
    search.dispatchEvent(new Event('input'));
    await settle(element);
  };
  const press = async (key: string) => {
    search.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, composed: true, cancelable: true }));
    await settle(element);
  };
  return { element, context, root, search, type, press };
}

const headings = (element: Element) => all(element, 'section h4').map((heading) => text(heading));
const items = (element: Element) => all(element, '.item').map((item) => text(item));
const item = (element: Element, name: string) =>
  must(
    all<HTMLButtonElement>(element, '.item').find((one) => text(one).startsWith(name)),
    name,
  );

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the field picker', () => {
  it('is headed Add a field, lists Group, the types in their groups in their order, and has Paste questions and Close', async () => {
    const { element, root } = await pickerOf();
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Add a field');
    expect(headings(element)).toEqual([
      'Layout',
      'Text',
      'Numbers and dates',
      'Choices',
      'People',
      'Files',
      'Structure',
      'Content',
      'Protection',
    ]);
    // No Recently used, and no New page: a page is added at its tabs.
    expect(items(element).slice(0, 3)).toEqual(['Group', 'Short text', 'Long text']);
    expect(items(element)).toHaveLength(1 + Object.keys(builtInTypes).length);
    // As the other sidebars' footers: a button at the start, the actions at the end.
    const paste = root.querySelector('uui-button[slot="footer-info"]');
    expect([paste?.getAttribute('label'), paste?.getAttribute('look')]).toEqual(['Paste questions\u2026', 'outline']);
    expect(root.querySelector('[slot="actions"] uui-button')?.getAttribute('label')).toBe('Close');
    // In two columns where there is room, the lists stay in reading order for the Tab key.
    expect(root.querySelector('.groups section:first-child h4')?.textContent).toBe('Layout');
    // One level below the sidebar's own headline, which Umbraco's layout draws as an h3.
    expect(root.querySelector('.groups h3')).toBeNull();
  });

  it('gives the focus to its search as it opens, and says it is shown and gone', async () => {
    const opened = vi.fn();
    const closed = vi.fn();
    const { element, root } = await pickerOf({ opened, closed });
    expect(root.activeElement?.localName).toBe('uui-input');
    expect(opened).toHaveBeenCalledOnce();
    element.remove();
    expect(closed).toHaveBeenCalledOnce();
  });

  it('narrows to what the search finds by name or alias, any letter case and accent, and says when nothing is', async () => {
    const { element, root, type } = await pickerOf();
    await type('MAIL');
    expect(headings(element)).toEqual(['Text']);
    expect(items(element)).toEqual(['Email Enter']);
    await type('textarea');
    expect(items(element)).toEqual(['Long text Enter']);
    // An alias written in camel case is found by its words run together, in any letter case.
    const camel = await pickerOf({
      types: [{ ...served.types[0], alias: 'colourPicker', name: 'Colour' } as (typeof served.types)[number]],
    });
    await camel.type('ColourP');
    expect(items(camel.element)).toEqual(['Colour Enter']);
    await type('xyz');
    expect(items(element)).toEqual([]);
    expect(text(root.querySelector('.none'))).toBe('No field type matches "xyz".');
    expect(root.querySelector('.none')?.getAttribute('role')).toBe('status');
    expect(folded('Gro\u00dfe \u00c4pfel!')).toBe('gro\u00dfe apfel');
  });

  it('marks the first match that can be picked while a search is typed, and Enter picks it', async () => {
    const shortless = served.types.map((each) =>
      each.alias === 'text' ? { ...each, availability: { isAvailable: false, reason: 'Not here.' } } : each,
    );
    const { element, root, context, type, press } = await pickerOf({ types: shortless });
    expect(all(element, '.item[data-first]')).toEqual([]);
    await type('text');
    // Short text comes first but can't be picked: Long text is the first match.
    expect(items(element).slice(0, 2)).toEqual(['Short text', 'Long text Enter']);
    expect(text(must(root.querySelector('.item[data-first]'), 'the first match'))).toBe('Long text Enter');
    expect(text(root.querySelector('[aria-live="polite"]'))).toBe('Enter adds Long text.');
    await press('Enter');
    expect(context.getValue()).toEqual({ pick: { kind: 'field', type: 'textarea' } });
    expect(context.submit).toHaveBeenCalledOnce();
  });

  it('picks nothing with Enter before a search is typed or where nothing matches, and Arrow down goes to the first item', async () => {
    const { root, context, type, press } = await pickerOf();
    await press('Enter');
    await type('   ');
    await press('Enter');
    await type('xyz');
    await press('Enter');
    expect(context.submit).not.toHaveBeenCalled();
    await type('');
    await press('ArrowDown');
    expect(text(root.activeElement)).toBe('Group');
  });

  it("lists the site's saved groups after the types, finds them by name, and picks one's copy", async () => {
    const address = { key: 't1', name: 'Address block', group: { key: 'g1', fields: [] }, checks: [] };
    const consent = { key: 't2', name: 'Standard consent', group: { key: 'g2', fields: [] }, checks: [] };
    const { element, context, type } = await pickerOf({ saved: [address, consent] });
    expect(headings(element).at(-1)).toBe('Saved groups');
    expect(items(element).slice(-2)).toEqual(['Address block', 'Standard consent']);
    await type('standard');
    expect(headings(element)).toEqual(['Saved groups']);
    item(element, 'Standard consent').click();
    expect(context.getValue()).toEqual({ pick: { kind: 'savedGroup', saved: consent } });
  });

  it('shows no Saved groups while the site has none', async () => {
    const { element } = await pickerOf();
    expect(headings(element)).not.toContain('Saved groups');
  });

  it('picks a field of a type, a group and Paste questions, each answering the view at once', async () => {
    for (const [name, pick] of [
      ['Email', { kind: 'field', type: 'email' }],
      ['Group', { kind: 'group' }],
    ] as const) {
      const { element, context } = await pickerOf();
      item(element, name).click();
      expect(context.getValue()).toEqual({ pick });
      expect(context.submit).toHaveBeenCalledOnce();
    }
    const { root, context } = await pickerOf();
    root.querySelector('uui-button[slot="footer-info"]')?.dispatchEvent(new Event('click'));
    expect(context.getValue()).toEqual({ pick: { kind: 'paste' } });
  });

  it('keeps an unavailable type in its place with its reason under its name, which describes it, and never picks it', async () => {
    const captchaless = served.types.map((each) =>
      each.alias === 'captcha'
        ? { ...each, availability: { isAvailable: false, reason: 'No captcha provider has its keys.' } }
        : each,
    );
    const { element, root, context } = await pickerOf({ types: captchaless });
    const captcha = item(element, 'Captcha');
    expect(captcha.getAttribute('aria-disabled')).toBe('true');
    expect(captcha.hasAttribute('disabled')).toBe(false);
    const why = must(root.getElementById(captcha.getAttribute('aria-describedby') ?? ''), 'the reason');
    expect(text(why)).toBe('No captcha provider has its keys.');
    expect(captcha.parentElement?.contains(why)).toBe(true);
    captcha.click();
    expect(context.submit).not.toHaveBeenCalled();
  });

  it('closes without a pick with Close, and with Escape in its search at once, whatever is typed', async () => {
    const { root, context, type, press } = await pickerOf();
    root.querySelector('[slot="actions"] uui-button')?.dispatchEvent(new Event('click'));
    expect(context.reject).toHaveBeenCalledOnce();
    await type('em');
    await press('Escape');
    expect(context.reject).toHaveBeenCalledTimes(2);
    expect(context.getValue()).toEqual({});
  });
});

describe('the Paste questions dialog', () => {
  async function dialogOf(questions: string) {
    const element = new PasteQuestionsModalElement();
    element.data = { types: { text: 'Short text', email: 'Email', yesno: 'Yes or no', textarea: 'Long text' } };
    document.body.append(element);
    await settle(element);
    const area = must(shadow(element).querySelector<HTMLTextAreaElement>('textarea#questions'), 'the questions');
    area.value = questions;
    area.dispatchEvent(new Event('input'));
    await settle(element);
    return { element, root: shadow(element) };
  }

  it('labels its box and gives it its help, then lists the fields it will add with their types and how many', async () => {
    const { root } = await dialogOf(
      'Your name\nEmail address\n\nDo you need step-free access?\nAnything else we should know?',
    );
    expect(
      [...root.querySelectorAll('tbody tr')].map((row) => [...row.querySelectorAll('td')].map((cell) => text(cell))),
    ).toEqual([
      ['Your name', 'Short text'],
      ['Email address', 'Email'],
      ['Do you need step-free access?', 'Yes or no'],
      ['Anything else we should know?', 'Long text'],
    ]);
    const area = must(root.querySelector('textarea'), 'the questions');
    expect([text(root.querySelector(`label[for="${area.id}"]`)), area.getAttribute('aria-describedby')]).toEqual([
      'Your questions',
      'questions-help',
    ]);
    expect(text(root.querySelector('#questions-help'))).toMatch(/^One question per line/);
    const add = must(root.querySelector('uui-button[look="primary"]'), 'Add');
    expect([add.getAttribute('label'), add.hasAttribute('disabled')]).toEqual(['Add 4 fields', false]);
  });

  it('adds nothing without a question, and a guess the site does not offer is a short text', async () => {
    const empty = await dialogOf('  \n ');
    expect(empty.root.querySelector('table')).toBeNull();
    expect(empty.root.querySelector('uui-button[look="primary"]')?.hasAttribute('disabled')).toBe(true);
    const phone = await dialogOf('Phone number');
    expect(text(phone.root.querySelector('tbody td:last-child'))).toBe('Short text');
  });
});
