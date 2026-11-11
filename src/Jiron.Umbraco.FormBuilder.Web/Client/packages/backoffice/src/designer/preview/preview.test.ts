// The preview's parts (ADR-0168): a draft's rich text copied without anything it could run or load, the canvas
// endpoint's answer as a look with its words, and the read-only preview the template gallery shows, drawn by the
// element's views for the eye only and said once to screen readers.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import { all, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { builtInTypes, englishLook } from './fixtures';
import { fieldLabels } from './form-preview.element';
import { lookOf, word } from './look';
import { escapeHtml, fragmentOf, safeHtml, showsText } from './rich-text';

const contact = JSON.parse(
  readFileSync(
    join(
      dirname(fileURLToPath(import.meta.url)),
      ...Array<string>(7).fill('..'),
      'Jiron.Umbraco.FormBuilder.Core',
      'Forms',
      'Templates',
      'contact.json',
    ),
    'utf8',
  ),
) as { definition: unknown };

describe("a draft's rich text", () => {
  it('keeps its text and the plain tags of its structure, without a single attribute', () => {
    expect(
      safeHtml(
        '<p class="lead" onclick="x()">Hi <strong style="color:red">there</strong>, <a href="javascript:alert(1)">read</a></p>',
      ),
    ).toBe('<p>Hi <strong>there</strong>, <a>read</a></p>');
    expect(safeHtml('<ul><li>One</li><li>Two<br>lines</li></ul>')).toBe('<ul><li>One</li><li>Two<br>lines</li></ul>');
  });

  it('drops what could run or load, with what it holds, and keeps the text of any other element', () => {
    expect(safeHtml('<p>a<script>alert(1)</script><img src=x onerror="alert(1)"><style>p{}</style>b</p>')).toBe(
      '<p>ab</p>',
    );
    expect(safeHtml('<div><span title="t">x</span><iframe src="/"></iframe></div><svg><text>y</text></svg>')).toBe('x');
    expect(safeHtml('<template><p>hidden</p></template>z')).toBe('z');
  });

  it('writes its text escaped, so markup in it stays text', () => {
    expect(safeHtml('<p>1 &lt; 2 &amp; "q" \'s\'</p>')).toBe('<p>1 &lt; 2 &amp; &quot;q&quot; &#39;s&#39;</p>');
    expect(safeHtml('&lt;img src=x onerror=alert(1)&gt;')).toBe('&lt;img src=x onerror=alert(1)&gt;');
    expect(escapeHtml('<b a="1">&\'')).toBe('&lt;b a=&quot;1&quot;&gt;&amp;&#39;');
  });

  it('is nothing without a text, and shows something only with text or a line break', () => {
    expect(safeHtml(undefined)).toBe('');
    expect(safeHtml('')).toBe('');
    expect(showsText('')).toBe(false);
    expect(showsText('<p></p>')).toBe(false);
    expect(showsText('<p>&nbsp; </p>')).toBe(false);
    expect(showsText('<p>x</p>')).toBe(true);
    expect(showsText('<br>')).toBe(true);
  });

  it('becomes nodes from what it wrote', () => {
    const nodes = fragmentOf(safeHtml('<p>a <em>b</em></p>'));
    expect([...nodes.childNodes].map((node) => node.nodeName)).toEqual(['P']);
    expect(nodes.textContent).toBe('a b');
  });
});

describe('the look', () => {
  it('is the canvas answer, its classes as the views need them', () => {
    expect(englishLook.culture).toBe('en-US');
    expect(englishLook.classes.control).toBe('jfb-control');
    expect(englishLook.classes.partLabel).toBeUndefined();
    expect(englishLook.marker).toBe('optional');
  });

  it('refuses an answer without a class the views write, and reads an unknown marker as optional', () => {
    const answer = {
      culture: 'en-US',
      words: {},
      classes: { ...englishLook.classes },
      stylesheets: [],
      requiredMarker: 'odd',
    };
    expect(lookOf(answer as never).marker).toBe('optional');
    const without = Object.fromEntries(Object.entries(answer.classes).filter(([slot]) => slot !== 'label'));
    expect(() => lookOf({ ...answer, classes: without } as never)).toThrow(/label/);
  });

  it('says a word with its values, and the name of a word it lacks', () => {
    expect(word(englishLook, 'counter', '255')).toBe('Up to 255 characters');
    expect(word(englishLook, 'step', '2', '3')).toBe('Page 2 of 3');
    expect(word(englishLook, 'noSuchWord')).toBe('noSuchWord');
  });
});

describe("a definition's field labels", () => {
  it("are the contact template's fields in order, in the user's language", () => {
    expect(fieldLabels(contact.definition, 'en-US')).toEqual(['Name', 'Email address', 'Message']);
    expect(fieldLabels(contact.definition, 'de')).toEqual(['Name', 'E-Mail-Adresse', 'Nachricht']);
  });

  it("list a repeatable group's fields after it, and skip fields without a label", () => {
    const definition = {
      pages: [
        {
          groups: [
            {
              fields: [
                {
                  type: 'repeater',
                  label: { '': 'Guests' },
                  fields: [{ label: { '': 'Guest name' } }, { type: 'hidden' }],
                },
                { label: { '': 'Notes' } },
              ],
            },
          ],
        },
      ],
    };
    expect(fieldLabels(definition, 'en')).toEqual(['Guests', 'Guest name', 'Notes']);
    expect(fieldLabels(null, 'en')).toEqual([]);
  });
});

describe('the read-only preview', () => {
  beforeAll(registerEnglish);

  afterEach(() => {
    document.body.replaceChildren();
  });

  it("draws a template's fields as the website does, for the eye only, and says them once for screen readers", async () => {
    const { FormPreviewElement } = await import('./form-preview.element');
    const element = new FormPreviewElement();
    element.look = englishLook;
    element.types = builtInTypes;
    element.definition = contact.definition;
    testHost().append(element);
    await settle(element);

    const form = shadow(element).querySelector('.jfb-form');
    expect(form?.classList.contains('jfb-theme-default')).toBe(true);
    expect(form?.getAttribute('aria-hidden')).toBe('true');
    expect(form?.hasAttribute('inert')).toBe(true);
    expect(all(element, '.jfb-page')).toHaveLength(1);
    expect(all(element, '.jfb-field .jfb-label').map((label) => text(label))).toEqual([
      'Name',
      'Email address',
      'Message',
    ]);
    expect(all(element, 'input.jfb-control[type="email"]')).toHaveLength(1);
    expect(all(element, 'textarea.jfb-control')).toHaveLength(1);
    expect(text(shadow(element).querySelector('.visually-hidden'))).toBe('Its fields: Name, Email address, Message.');
  });
});
