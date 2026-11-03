// The Move to... and Keyboard shortcuts dialogs (ROADMAP 9.3c): the places as the browser's own radio buttons by page,
// each refused with why and not to be chosen, Move only once a place is chosen, Enter on it; and the shortcuts with
// their keys as this keyboard has them, and Close.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, text } from '../../testing';
import { MoveToModalElement } from './move-to.modal.element';
import type { MoveToModalData, MoveToModalValue } from './move-to.modal.token';
import { ShortcutsModalElement } from './shortcuts.modal.element';

/** A modal context as Umbraco's holds the value. */
function modalContext() {
  const state = new UmbObjectState<Partial<MoveToModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: MoveToModalValue) => state.setValue(next),
    updateValue: (partial: Partial<MoveToModalValue>) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

const DATA: MoveToModalData = {
  name: 'Email address',
  sections: [
    {
      heading: 'Page 1: About you',
      places: [
        { key: 'g1', name: 'Contact (where it is now)', refusal: null, disabled: false, inside: false },
        {
          key: 'kids',
          name: 'Children, inside Contact',
          refusal: "A repeatable group can't hold that.",
          disabled: true,
          inside: true,
        },
      ],
    },
    { heading: 'Page 2', places: [{ key: 'g2', name: 'Group 1', refusal: null, disabled: false, inside: false }] },
  ],
};

async function moveToOf(data: MoveToModalData = DATA) {
  const context = modalContext();
  const element = new MoveToModalElement();
  element.modalContext = context as never;
  element.data = data;
  document.body.append(element);
  await settle(element);
  return { element, context, root: shadow(element) };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the Move to... dialog', () => {
  it('names the element and lists the places by page as radio buttons, a refused one with why', async () => {
    const { root } = await moveToOf();
    expect(root.querySelector('uui-dialog-layout')?.getAttribute('headline')).toBe('Move Email address');
    expect(text(root.querySelector('legend'))).toBe('To the end of');
    const groups = [...root.querySelectorAll('[role="group"]')];
    expect(groups.map((group) => text(root.getElementById(group.getAttribute('aria-labelledby') ?? '')))).toEqual([
      'Page 1: About you',
      'Page 2',
    ]);
    const radios = [...root.querySelectorAll<HTMLInputElement>('input[type="radio"]')];
    expect(radios.map((radio) => [text(radio.closest('label')?.querySelector('.name')), radio.disabled])).toEqual([
      ['Contact (where it is now)', false],
      ['Children, inside Contact', true],
      ['Group 1', false],
    ]);
    const refused = must(radios[1], 'the refused place');
    expect(text(root.getElementById(refused.getAttribute('aria-describedby') ?? ''))).toBe(
      "A repeatable group can't hold that.",
    );
    expect(refused.closest('label')?.classList.contains('inside')).toBe(true);
    // The reason is the place's description, and no part of its name.
    expect(root.getElementById(refused.getAttribute('aria-describedby') ?? '')?.getAttribute('aria-hidden')).toBe(
      'true',
    );
    expect(radios[0]?.hasAttribute('aria-describedby')).toBe(false);
    expect(text(root.querySelector('.help'))).toBe('Alt+Up and Alt+Down then move it one place at a time.');
  });

  it('gives the focus to the first place that can be chosen once it is open', async () => {
    const { root } = await moveToOf({
      name: 'Address',
      sections: [
        {
          heading: null,
          places: [
            { key: 'p1', name: 'Page 1 (where it is now)', refusal: null, disabled: true, inside: false },
            { key: 'p2', name: 'Page 2', refusal: null, disabled: false, inside: false },
          ],
        },
      ],
    });
    expect(root.querySelector('[role="group"]')).toBeNull();
    await new Promise((done) => requestAnimationFrame(() => done(undefined)));
    expect((root.activeElement as HTMLInputElement | null)?.value).toBe('p2');
  });

  it('moves only once a place is chosen, with Move or with Enter', async () => {
    const { element, context, root } = await moveToOf();
    const move = must(root.querySelector('uui-button[look="primary"]'), 'Move');
    expect([move.getAttribute('label'), move.hasAttribute('disabled')]).toEqual(['Move', true]);
    const fieldset = must(root.querySelector('fieldset'), 'the places');
    fieldset.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(context.submit).not.toHaveBeenCalled();
    const radio = must(root.querySelector<HTMLInputElement>('input[value="g2"]'), 'Group 1');
    radio.click();
    await settle(element);
    expect(move.hasAttribute('disabled')).toBe(false);
    fieldset.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(context.getValue()).toEqual({ key: 'g2' });
    expect(context.submit).toHaveBeenCalledTimes(1);
    move.dispatchEvent(new Event('click'));
    expect(context.submit).toHaveBeenCalledTimes(2);
    must(root.querySelector('uui-button[label="Cancel"]'), 'Cancel').dispatchEvent(new Event('click'));
    expect(context.reject).toHaveBeenCalledTimes(1);
  });
});

describe('the Keyboard shortcuts dialog', () => {
  it('lists each shortcut with its keys, Ctrl on this keyboard, and only Close', async () => {
    const context = modalContext();
    const element = new ShortcutsModalElement();
    element.modalContext = context as never;
    document.body.append(element);
    await settle(element);
    const root = shadow(element);
    expect(root.querySelector('uui-dialog-layout')?.getAttribute('headline')).toBe('Keyboard shortcuts');
    expect(text(root.querySelector('.lead'))).toBe('They act on the field, group or page you are on.');
    // Between two keys that do it either way, a screen reader hears "or"; the "+" between keys held together is not said.
    const either = [...root.querySelectorAll('dt')][4];
    expect([...(either?.querySelectorAll('.visually-hidden') ?? [])].map((word) => text(word))).toEqual(['or']);
    expect([...(either?.querySelectorAll('[aria-hidden="true"]') ?? [])].map((sign) => text(sign))).toEqual(['+', '/']);
    const terms = [...root.querySelectorAll('dt')].map((term) =>
      [...term.querySelectorAll('kbd')].map((key) => text(key)).join(' '),
    );
    expect(terms).toEqual([
      'Ctrl Z',
      'Ctrl Y',
      'Ctrl D',
      'Delete',
      'Alt \u2191 \u2193',
      '\u2191 \u2193',
      'F2',
      'Esc',
      '?',
    ]);
    expect([...root.querySelectorAll('dd')].map((text_) => text(text_))).toEqual([
      'Undo the last change',
      'Redo what was undone',
      'Duplicate it',
      'Delete it; the message that follows offers Undo',
      'Move it one place up or down, at the end into the next group or page',
      'Go up or down the page',
      "Edit a field's label in place",
      'Clear the selection, close the palette, or stop a drag',
      'Show these shortcuts',
    ]);
    const buttons = [...root.querySelectorAll('uui-button')];
    expect(buttons.map((button) => button.getAttribute('label'))).toEqual(['Close']);
    buttons[0]?.dispatchEvent(new Event('click'));
    expect(context.reject).toHaveBeenCalledTimes(1);
  });
});
