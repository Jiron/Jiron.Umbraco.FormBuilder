// The visitor texts that recall answers, written on the page (ROADMAP 6.4g, ADR-0115): a text as text, hidden while
// empty or showing its fallback; a placeholder as its control's attribute; a rich text by copying the texts and the
// values of the attributes its elements already have from an inert parse, never a node and never an `on...` attribute,
// and not at all when its elements differ, but for the line breaks a recalled answer brings; each element only when its
// text changes.
import { afterEach, describe, expect, it } from 'vitest';
import type { FormModel } from './model';
import { showTexts } from './texts';
import { ENGLISH } from './testing';

const MODEL: FormModel = {
  alias: 'f',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: { key: 'f', pages: [] },
  fields: {},
  consents: {},
  ...ENGLISH,
  mergeTags: {
    texts: [
      { owner: 'k', text: 'label', context: 'text' },
      { owner: 'k', text: 'placeholder', context: 'text' },
      { owner: 'k', text: 'settings.html', context: 'html' },
      { owner: 'p2', text: 'title', context: 'text' },
    ],
    values: {},
    recall: { pages: [] },
  },
};

const FORM = `
  <form>
    <label for="x"><span id="label" data-jfb-text="k label">Old</span> <span class="jfb-mark">*</span></label>
    <input id="x" placeholder="Old" data-jfb-text="k placeholder">
    <div id="html" data-jfb-text="k settings.html"><p title="old" onclick="first()">One <a href="/a">link</a> after</p></div>
    <ol><li><span id="step" data-jfb-text="p2 title" data-jfb-fallback="Page 2">Old</span></li></ol>
  </form>`;

const formOf = (): HTMLFormElement => {
  document.body.innerHTML = FORM;
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  return form;
};
const byId = (id: string): HTMLElement => {
  const element = document.getElementById(id);
  if (element === null) throw new Error(`no #${id}`);
  return element;
};
const show = (form: HTMLFormElement, texts: Record<string, string>): void =>
  showTexts(form, MODEL, new Map(Object.entries(texts)));

afterEach(() => {
  document.body.innerHTML = '';
});

describe('a text on the page', () => {
  it('is written as text, and is hidden while empty, the mark beside a label kept', () => {
    const form = formOf();
    show(form, { 'k label': '<b>Anna</b>' });
    expect(byId('label').textContent).toBe('<b>Anna</b>');
    expect(byId('label').children).toHaveLength(0);
    show(form, { 'k label': '' });
    expect([byId('label').textContent, byId('label').hidden]).toEqual(['', true]);
    expect(form.querySelector('label')?.textContent).toBe(' *');
    show(form, { 'k label': 'Ben' });
    expect([byId('label').textContent, byId('label').hidden]).toEqual(['Ben', false]);
  });

  it('shows its fallback while empty where it has one, as a progress step does', () => {
    const form = formOf();
    show(form, { 'p2 title': '' });
    expect([byId('step').textContent, byId('step').hidden]).toEqual(['Page 2', false]);
    show(form, { 'p2 title': 'Your booking, Anna' });
    expect(byId('step').textContent).toBe('Your booking, Anna');
  });

  it("is a placeholder's attribute, which an empty one removes", () => {
    const form = formOf();
    show(form, { 'k placeholder': 'Anna' });
    expect(byId('x').getAttribute('placeholder')).toBe('Anna');
    expect(byId('x').hidden).toBe(false);
    show(form, { 'k placeholder': '' });
    expect(byId('x').hasAttribute('placeholder')).toBe(false);
    expect(byId('x').hidden, 'a control is never hidden for its placeholder').toBe(false);
  });

  it('is left alone when the renderer gives it no text', () => {
    const form = formOf();
    show(form, { 'k placeholder': 'Anna' });
    expect(byId('label').textContent).toBe('Old');
  });
});

describe('a rich text on the page', () => {
  it("takes the rendering's texts and attribute values, keeping its own elements", () => {
    const form = formOf();
    const link = byId('html').querySelector('a');
    show(form, { 'k settings.html': '<p title="new">Two <a href="/b?for=Anna">link</a> after, Anna</p>' });
    expect(byId('html').innerHTML).toBe(
      '<p title="new" onclick="first()">Two <a href="/b?for=Anna">link</a> after, Anna</p>',
    );
    expect(byId('html').querySelector('a'), 'the same element, moved or not').toBe(link);
  });

  it('takes a text where the page had none, and drops one the rendering leaves out', () => {
    const form = formOf();
    show(form, { 'k settings.html': '<p title="old"><a href="/a">link</a></p>' });
    expect(byId('html').innerHTML).toBe('<p title="old" onclick="first()"><a href="/a">link</a></p>');
    show(form, { 'k settings.html': '<p title="old">Hi <a href="/a">link</a>!</p>' });
    expect(byId('html').innerHTML).toBe('<p title="old" onclick="first()">Hi <a href="/a">link</a>!</p>');
  });

  it('never takes an element, an attribute the page lacks, or an event handler from the rendering', () => {
    const form = formOf();
    show(form, {
      'k settings.html':
        '<p title="old" onclick="second()" class="x">One <a href="/a" onmouseover="x()">link</a> after</p>',
    });
    const paragraph = byId('html').querySelector('p');
    expect(paragraph?.getAttribute('onclick')).toBe('first()');
    expect(paragraph?.hasAttribute('class')).toBe(false);
    expect(byId('html').querySelector('a')?.hasAttribute('onmouseover')).toBe(false);
    const before = byId('html').innerHTML;
    show(form, { 'k settings.html': '<p title="old">One <img src="x" onerror="alert(1)"> after</p>' });
    expect(byId('html').innerHTML, 'nothing of it').toBe(before);
    expect(
      byId('html').querySelector('img'),
      'a rendering whose elements differ is left as the page has it',
    ).toBeNull();
    expect(byId('html').querySelector('a')).not.toBeNull();
  });

  it('follows a recalled answer as it gains and loses line breaks, and takes no other element (6.8b)', () => {
    const form = formOf();
    const link = byId('html').querySelector('a');
    const shown = (): string => byId('html').innerHTML;
    show(form, { 'k settings.html': '<p title="old">One<br>two <a href="/a">link</a> after</p>' });
    expect(shown()).toBe('<p title="old" onclick="first()">One<br>two <a href="/a">link</a> after</p>');
    show(form, {
      'k settings.html': '<p title="old">One<br>two<br class="x" onclick="x()">three <a href="/a">link</a> after</p>',
    });
    expect(shown(), "a break made anew, nothing of the rendering's").toBe(
      '<p title="old" onclick="first()">One<br>two<br>three <a href="/a">link</a> after</p>',
    );
    show(form, { 'k settings.html': '<p title="old">Just one <a href="/a">link</a> after</p>' });
    expect(shown()).toBe('<p title="old" onclick="first()">Just one <a href="/a">link</a> after</p>');
    expect(byId('html').querySelector('a'), 'the same link throughout').toBe(link);
    show(form, {
      'k settings.html': '<p title="old">One<br><img src="x" onerror="alert(1)"> <a href="/a">link</a> after</p>',
    });
    expect(shown(), 'another element: left as the page has it').toBe(
      '<p title="old" onclick="first()">Just one <a href="/a">link</a> after</p>',
    );
  });

  it('is hidden while it renders empty', () => {
    const form = formOf();
    show(form, { 'k settings.html': '' });
    expect(byId('html').hidden).toBe(true);
  });
});

describe('writing', () => {
  it('changes an element only when its text changes', async () => {
    const form = formOf();
    const texts = {
      'k label': 'Anna',
      'k placeholder': 'Old',
      'k settings.html': '<p title="old">One <a href="/a">link</a> after</p>',
    };
    show(form, texts);
    const changes: MutationRecord[] = [];
    const observer = new MutationObserver((records) => changes.push(...records));
    observer.observe(form, { subtree: true, childList: true, attributes: true, characterData: true });
    show(form, texts);
    show(form, { ...texts });
    await Promise.resolve();
    observer.disconnect();
    expect(changes).toEqual([]);
  });

  it('writes nothing on a first pass that renders what the server wrote', async () => {
    const form = formOf();
    const changes: MutationRecord[] = [];
    const observer = new MutationObserver((records) => changes.push(...records));
    observer.observe(form, { subtree: true, childList: true, attributes: true, characterData: true });
    show(form, {
      'k label': 'Old',
      'k placeholder': 'Old',
      'k settings.html': '<p title="old">One <a href="/a">link</a> after</p>',
      'p2 title': 'Old',
    });
    await Promise.resolve();
    observer.disconnect();
    expect(changes.map((change) => change.type)).toEqual([]);
  });
});
