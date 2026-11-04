// Where a Fix it puts the focus in the inspector (ROADMAP 9.5f, ADR-0181): the item a problem names among those the
// inspector draws, its boxes in the order the focus tries them, and the first box that takes the focus.
import { afterEach, describe, expect, it } from 'vitest';
import { builtInTypes } from '../preview/fixtures';
import { fieldSections, type Item } from './inspector-model';
import { boxesOf, focusableIn, isItem } from './item-focus';

const items = (alias: string): Item[] =>
  fieldSections(builtInTypes[alias] ?? (undefined as never), 2).flatMap((section) => section.items);

afterEach(() => {
  document.body.replaceChildren();
});

describe("a problem's item in the inspector", () => {
  it('is found among the items drawn by its kind and the property, setting or message it edits', () => {
    const file = items('file');
    expect(file.filter((item) => isItem(item, { kind: 'setting', name: 'maxBytes' }))).toHaveLength(1);
    expect(file.filter((item) => isItem(item, { kind: 'setting', name: 'nothing' }))).toHaveLength(0);
    expect(file.filter((item) => isItem(item, { kind: 'text', property: 'label' }))).toHaveLength(1);
    expect(file.filter((item) => isItem(item, { kind: 'text', property: 'title' }))).toHaveLength(0);
    expect(file.filter((item) => isItem(item, { kind: 'switch', property: 'required' }))).toHaveLength(1);
    expect(file.filter((item) => isItem(item, { kind: 'alias' }))).toHaveLength(1);
    const text = items('text');
    expect(text.filter((item) => isItem(item, { kind: 'message', message: 'required' }))).toHaveLength(1);
    expect(text.filter((item) => isItem(item, { kind: 'plain', property: 'cssClass' }))).toHaveLength(1);
    expect(items('dropdown').filter((item) => isItem(item, { kind: 'options' }))).toHaveLength(1);
  });

  it('names its boxes by the ids the inspector gives them, a nested setting before its parent', () => {
    expect(boxesOf({ kind: 'text', property: 'help' })).toEqual(['prop-help']);
    expect(boxesOf({ kind: 'switch', property: 'hideLabel' })).toEqual(['prop-hideLabel']);
    expect(boxesOf({ kind: 'message', message: 'maxLength' })).toEqual(['prop-message-maxLength']);
    expect(boxesOf({ kind: 'setting', name: 'maxBytes' })).toEqual(['setting-maxBytes']);
    expect(boxesOf({ kind: 'setting', name: 'address', inner: 'street' })).toEqual([
      'setting-address-street',
      'setting-address',
    ]);
    expect(boxesOf({ kind: 'prefill' })).toEqual(['prop-prefill-query']);
    expect(boxesOf({ kind: 'options' })).toEqual(['prop-options']);
  });

  it('takes the box itself where it takes the focus, else the first in it that does, into shadow roots', () => {
    const input = document.createElement('input');
    expect(focusableIn(input)).toBe(input);
    const host = document.createElement('div');
    const root = host.attachShadow({ mode: 'open' });
    root.innerHTML = '<span>Media</span><button disabled>Off</button><div><button id="pick">Choose</button></div>';
    expect(focusableIn(host)?.id).toBe('pick');
    const editor = document.createElement('section');
    editor.innerHTML = '<p>Nothing to take the focus</p>';
    expect(focusableIn(editor)).toBeNull();
    const hidden = document.createElement('input');
    hidden.type = 'hidden';
    expect(focusableIn(hidden)).toBeNull();
  });
});
