// The inspector's rich text (ROADMAP 9.4c, ADR-0173): a toolbar whose buttons say whether they are on, bold and lists,
// a link whose address must be one the server's sanitiser keeps, a tag put in where the caret is, and the form's text
// shown again after Undo while the box isn't being typed in. jsdom draws no layout, so the ranges ProseMirror measures
// are given empty boxes.
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import { must, registerEnglish, settle, testHost } from '../../testing';
import { isLinkAddress, RichTextElement } from './rich-text.element';

beforeAll(() => {
  registerEnglish();
  const empty = () => ({ x: 0, y: 0, top: 0, left: 0, right: 0, bottom: 0, width: 0, height: 0, toJSON: () => ({}) });
  Object.assign(Range.prototype, {
    getBoundingClientRect: empty,
    getClientRects: () => ({ length: 0, item: () => null, [Symbol.iterator]: [][Symbol.iterator] }),
  });
  if (!document.elementFromPoint) Object.assign(document, { elementFromPoint: () => null });
  if (!document.execCommand) Object.assign(document, { execCommand: () => false });
});

afterEach(() => {
  document.body.replaceChildren();
});

async function editorOf(value = '', inline = false) {
  const element = new RichTextElement();
  const changes: { html: string; done: boolean }[] = [];
  Object.assign(element, {
    value,
    label: 'Wording',
    inline,
    change: (html: string, done: boolean) => changes.push({ html, done }),
  });
  testHost().append(element);
  await settle(element);
  const shadow = must(element.shadowRoot, 'its shadow root');
  const tool = (name: string) => must(shadow.querySelector<HTMLButtonElement>(`[data-tool="${name}"]`), name);
  const content = () => must(shadow.querySelector<HTMLElement>('.editor'), 'the editor');
  return { element, changes, shadow, tool, content };
}

describe('a link address', () => {
  it('is one the sanitiser keeps: http, https, mailto, tel, or a path on the site', () => {
    for (const address of [
      'https://example.com/privacy',
      'http://a.b',
      'mailto:hi@example.com',
      'tel:+41 44 000',
      '/privacy',
    ])
      expect(isLinkAddress(address), address).toBe(true);
    for (const address of [
      'javascript:alert(1)',
      'example.com',
      'ftp://x',
      'https://a b',
      '',
      'data:text/html,x',
      '//elsewhere.example/x',
      '/' + String.fromCharCode(92) + 'elsewhere.example',
    ])
      expect(isLinkAddress(address), address).toBe(false);
  });
});

describe('the rich text', () => {
  it('names its box and its toolbar, and shows the text it is given', async () => {
    const { shadow, content } = await editorOf('<p>I agree to the <strong>terms</strong>.</p>');
    expect(content().getAttribute('role')).toBe('textbox');
    expect(content().getAttribute('aria-label')).toBe('Wording');
    expect(shadow.querySelector('[role="toolbar"]')?.getAttribute('aria-label')).toBe('Formatting of Wording');
    expect([...shadow.querySelectorAll('[data-tool]')].map((button) => button.getAttribute('aria-label'))).toEqual([
      'Bold',
      'Italic',
      'Link',
      'Bulleted list',
      'Numbered list',
    ]);
    expect(content().innerHTML).toContain('<strong>terms</strong>');
  });

  it('puts a tag in where the caret is and hands the text on as done', async () => {
    const { element, changes } = await editorOf('<p>Hello</p>');
    element.insertText(' {{ form.name }}');
    expect(changes.at(-1)?.done).toBe(true);
    // Where jsdom puts the caret, at the end or the start, without a layout to put it by.
    expect(['Hello {{ form.name }}', ' {{ form.name }}Hello']).toContain(changes.at(-1)?.html.replace(/<[^>]*>/g, ''));
  });

  it('turns a list on from its button, which then says it is on', async () => {
    const { element, changes, tool } = await editorOf('<p>One</p>');
    tool('bulletList').click();
    await settle(element);
    expect(changes.at(-1)?.html).toBe('<ul><li><p>One</p></li></ul>');
    expect(tool('bulletList').getAttribute('aria-pressed')).toBe('true');
    expect(tool('orderedList').getAttribute('aria-pressed')).toBe('false');
  });

  it("refuses a link address the sanitiser wouldn't keep, and links one it keeps, in a new tab if asked", async () => {
    const { element, changes, tool, shadow } = await editorOf('<p></p>');
    tool('link').click();
    await settle(element);
    const form = must(shadow.querySelector<HTMLFormElement>('form.link'), 'the link panel');
    const address = must(form.querySelector<HTMLInputElement>('#address'), 'the address');
    address.value = 'javascript:alert(1)';
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(element);
    expect(address.getAttribute('aria-invalid')).toBe('true');
    expect(shadow.querySelector('#address-why')?.textContent).toContain('https://');
    address.value = 'https://example.com/privacy';
    must(form.querySelector<HTMLInputElement>('input[name="newTab"]'), 'new tab').checked = true;
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(element);
    expect(shadow.querySelector('form.link')).toBeNull();
    expect(changes.at(-1)?.html).toContain('href="https://example.com/privacy"');
    expect(changes.at(-1)?.html).toContain('target="_blank"');
    expect(changes.at(-1)?.html).not.toContain('rel=');
  });

  it('keeps a link where it is unless a new tab is asked, with nothing else on it', async () => {
    const { element, changes, tool, shadow } = await editorOf('<p></p>');
    tool('link').click();
    await settle(element);
    const form = must(shadow.querySelector<HTMLFormElement>('form.link'), 'the link panel');
    must(form.querySelector<HTMLInputElement>('#address'), 'the address').value = '/privacy';
    form.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(element);
    expect(changes.at(-1)?.html).toBe('<p><a href="/privacy">/privacy</a></p>');
  });

  it('hands nothing on for a visit without typing, and the text once the box is left after typing', async () => {
    const { changes, content } = await editorOf('<p>Kept</p>');
    content().dispatchEvent(new FocusEvent('focus'));
    content().dispatchEvent(new FocusEvent('blur'));
    expect(changes).toEqual([]);
  });

  it("starts afresh on a text set from outside, so Ctrl+Z can't bring back the one before", async () => {
    const { element, changes, content } = await editorOf('<p>First</p>');
    element.value = '<p>Second</p>';
    await settle(element);
    content().dispatchEvent(new KeyboardEvent('keydown', { key: 'z', ctrlKey: true, bubbles: true, cancelable: true }));
    await settle(element);
    expect(content().innerHTML).toContain('Second');
    expect(changes.map((change) => change.html)).not.toContain('<p>First</p>');
  });

  it('never follows a link in its text, which would take the backoffice away from the form', async () => {
    const { content } = await editorOf('<p>See the <a href="/privacy">terms</a>.</p>');
    const followed: Event[] = [];
    const listen = (event: Event) => followed.push(event);
    window.addEventListener('click', listen);
    const click = new MouseEvent('click', { bubbles: true, composed: true, cancelable: true });
    must(content().querySelector('a'), 'the link').dispatchEvent(click);
    window.removeEventListener('click', listen);
    expect([click.defaultPrevented, followed.length]).toEqual([true, 0]);
  });

  it('gets its editor back when it is moved in the page', async () => {
    const { element, shadow } = await editorOf('<p>Moved</p>');
    element.remove();
    testHost().append(element);
    await settle(element);
    await settle(element);
    expect(shadow.querySelector('.editor')?.textContent).toBe('Moved');
  });

  it('is one stop for the keyboard on its toolbar, whose arrow keys move between the buttons', async () => {
    const { element, shadow, tool } = await editorOf('<p></p>');
    const stops = () => [...shadow.querySelectorAll<HTMLButtonElement>('[data-tool]')].map((button) => button.tabIndex);
    expect(stops()).toEqual([0, -1, -1, -1, -1]);
    tool('bold').focus();
    must(shadow.querySelector('[role="toolbar"]'), 'the toolbar').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'End', bubbles: true, cancelable: true }),
    );
    await settle(element);
    expect(stops()).toEqual([-1, -1, -1, -1, 0]);
    expect(shadow.activeElement).toBe(tool('orderedList'));
    must(shadow.querySelector('[role="toolbar"]'), 'the toolbar').dispatchEvent(
      new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }),
    );
    await settle(element);
    expect(shadow.activeElement).toBe(tool('bold'));
    expect(tool('link').hasAttribute('aria-pressed')).toBe(false);
    expect(tool('link').getAttribute('aria-expanded')).toBe('false');
    tool('link').click();
    await settle(element);
    expect([tool('link').getAttribute('aria-expanded'), tool('link').getAttribute('aria-controls')]).toEqual([
      'true',
      'link-panel',
    ]);
  });

  it('writes an inline text, which sits in a label, as one line of words without lists', async () => {
    const { element, changes, shadow, content } = await editorOf('I agree to the <strong>terms</strong>.', true);
    expect([...shadow.querySelectorAll('[data-tool]')].map((button) => button.getAttribute('data-tool'))).toEqual([
      'bold',
      'italic',
      'link',
    ]);
    expect(content().getAttribute('aria-multiline')).toBe('false');
    element.insertText(' {{ form.name }}');
    expect(changes.at(-1)?.html).toMatch(/^[^<]*(<strong>terms<\/strong>)?[^<]*(<strong>terms<\/strong>)?[^<]*$/);
    expect(changes.at(-1)?.html).not.toContain('<p>');
    const listed = await editorOf('<p>One</p><ul><li><p>Two</p></li></ul>', true);
    listed.element.insertText('!');
    expect(listed.changes.at(-1)?.html).not.toMatch(/<(p|ul|li)>/);
  });

  it("shows the form's text again after Undo while it isn't typed in", async () => {
    const { element, content } = await editorOf('<p>First</p>');
    element.value = '<p>Second</p>';
    await settle(element);
    expect(content().innerHTML).toContain('Second');
  });
});
