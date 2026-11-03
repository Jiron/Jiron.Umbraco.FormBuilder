// The designer's fields held to the server (ADR-0168): for every field of the sink, a field of each built-in type with
// a repeatable group holding one of each, the view model the designer builds from the draft, written by the element's
// views, has the server's first render's elements and classes; and a field the server leaves out, the designer does
// too. What a visitor can't see takes no part: an element written hidden, a hidden input, a template, an option of a
// closed list and every text, since the designer shows no answers, errors or places.
import { writeField } from '@jfb/element/views';
import { describe, expect, it } from 'vitest';
import type { DesignField } from '../definition';
import { asEnhanced, fieldModel, sliderStart, textIn, type PreviewContext } from './field-models';
import { builtInTypes, englishLook, sink, sinkMarkup } from './fixtures';

const context: PreviewContext = {
  look: englishLook,
  types: builtInTypes,
  culture: 'en-US',
  marker: englishLook.marker,
  layout: 'vertical',
  captcha: 'hcaptcha',
  prefix: 'jfb-1',
};

/** An element's visible structure: each element's tag, the attributes that change how it looks, and its classes. */
function skeleton(element: Element, depth = 0): string[] {
  const tag = element.localName;
  const shown = ['type', 'rows', 'multiple']
    .filter((name) => element.hasAttribute(name) && (tag === 'input' || tag === 'textarea' || tag === 'select'))
    .map((name) => `[${name}=${element.getAttribute(name)}]`)
    .join('');
  const classes = [...element.classList].map((name) => `.${name}`).join('');
  const lines = [`${'  '.repeat(depth)}${tag}${shown}${classes}`];
  for (const child of element.children) {
    if (
      child.hasAttribute('hidden') ||
      (child.localName === 'input' && child.getAttribute('type') === 'hidden') ||
      ['template', 'option', 'script'].includes(child.localName)
    )
      continue;
    lines.push(...skeleton(child, depth + 1));
  }
  return lines;
}

const server = new DOMParser().parseFromString(sinkMarkup, 'text/html');
const fields = sink.pages.flatMap((page) => page.groups.flatMap((group) => group.fields));

describe("the sink's fields", () => {
  it('are every built-in type', () => {
    expect(new Set(fields.map((field) => field.type))).toEqual(new Set(Object.keys(builtInTypes)));
  });

  it.each(fields.map((field) => [`${field.type} ${field.alias ?? ''}`.trim(), field] as const))(
    '%s: the designer writes the server markup',
    (_name, field: DesignField) => {
      const shownByServer = server.querySelector(`[data-jfb-field="${field.key}"]`);
      const group = sink.pages[0]?.groups.find((each) => each.fields.includes(field));
      const model = fieldModel(field, context, Number(group?.['columns'] ?? 1));
      if (!model) {
        expect(shownByServer, 'the server writes it too').toBeNull();
        return;
      }
      expect(shownByServer, 'the server writes it').not.toBeNull();
      const written = writeField({ document, api: '/jfb/api/v1' }, englishLook.classes, model);
      expect(skeleton(written).join('\n')).toBe(skeleton(shownByServer as Element).join('\n'));
    },
  );
});

describe('a drawing as visitors with JavaScript see it', () => {
  const drawn = (type: string) => {
    const field = fields.find((each) => each.type === type) as DesignField;
    const written = writeField({ document, api: '' }, englishLook.classes, fieldModel(field, context) ?? fail());
    asEnhanced(written);
    return written;
  };
  const fail = (): never => {
    throw new Error('no model');
  };

  it('shows an upload as its drop frame', () => {
    expect(drawn('file').querySelector('.jfb-upload')?.hasAttribute('data-jfb-enhanced')).toBe(true);
  });

  it('shows a signature as its empty pad, without the typed name or a Clear', () => {
    const box = drawn('signature').querySelector('.jfb-signature');
    expect(box?.hasAttribute('data-jfb-enhanced')).toBe(true);
    expect(box?.querySelector('[data-jfb-drawn]')?.hasAttribute('hidden')).toBe(false);
    expect(box?.querySelector('[data-jfb-typed]')?.hasAttribute('hidden')).toBe(true);
    expect(box?.querySelector('[data-jfb-clear]')?.hasAttribute('hidden')).toBe(true);
    expect(box?.querySelector('[data-jfb-to-typed]')?.hasAttribute('hidden')).toBe(false);
  });

  it('leaves every other field as written', () => {
    for (const field of fields.filter((each) => !['file', 'signature', 'repeater'].includes(each.type))) {
      const model = fieldModel(field, context);
      if (!model) continue;
      const before = writeField({ document, api: '' }, englishLook.classes, model);
      const after = before.cloneNode(true) as Element;
      asEnhanced(after);
      expect(after.outerHTML, field.type).toBe(before.outerHTML);
    }
  });
});

describe('a text of a definition', () => {
  const texts = { '': 'Email address', de: 'E-Mail-Adresse', 'fr-CH': 'Adresse e-mail' };

  it('is the culture, then its language, then the default, in any letter case', () => {
    expect(textIn(texts, 'de')).toBe('E-Mail-Adresse');
    expect(textIn(texts, 'de-AT')).toBe('E-Mail-Adresse');
    expect(textIn(texts, 'FR-ch')).toBe('Adresse e-mail');
    expect(textIn(texts, 'fr')).toBe('Adresse e-mail');
    expect(textIn(texts, 'nl')).toBe('Email address');
  });

  it('is empty without texts or a default', () => {
    expect(textIn(undefined, 'en')).toBe('');
    expect(textIn({ de: 'Name' }, 'en')).toBe('');
  });
});

describe('a slider', () => {
  it('starts in the middle of its range, on its step, the higher of two steps as near', () => {
    expect(sliderStart('0', '100', '1')).toBe('50');
    expect(sliderStart('0', '60', '5')).toBe('30');
    expect(sliderStart('0', '10', '4')).toBe('4');
    expect(sliderStart('0', '12', '8')).toBe('8');
    // Two steps exactly as near: the higher, as a browser's range input takes.
    expect(sliderStart('0', '4', '4')).toBe('4');
    expect(sliderStart('0', '30', '10')).toBe('20');
    expect(sliderStart('0.5', '2.5', '0.25')).toBe('1.50');
    expect(sliderStart('0.1', '0.3', '0.1')).toBe('0.2');
  });
});
