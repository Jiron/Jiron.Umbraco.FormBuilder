// The gallery's preview of a template (ADR-0166): texts in the user's language, the fields screen readers hear, and a drawing
// that neither the keyboard nor a screen reader enters. The built-in contact template is the one drawn.
import { readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import { all, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { fieldLabels, textIn } from './template-preview.element';

const contact = JSON.parse(
  readFileSync(
    join(
      dirname(fileURLToPath(import.meta.url)),
      ...['..', '..', '..', '..', '..', '..'],
      'Jiron.Umbraco.FormBuilder.Core',
      'Forms',
      'Templates',
      'contact.json',
    ),
    'utf8',
  ),
) as { definition: unknown };

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

describe("a definition's field labels", () => {
  it("are the contact template's fields in order, in the user's language", () => {
    expect(fieldLabels(contact.definition, 'en-US')).toEqual(['Name', 'Email address', 'Message']);
    expect(fieldLabels(contact.definition, 'de')).toEqual(['Name', 'E-Mail-Adresse', 'Nachricht']);
  });

  it("list a repeater's children after it, and skip fields without a label", () => {
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

describe('the preview', () => {
  beforeAll(registerEnglish);

  afterEach(() => {
    document.body.replaceChildren();
  });

  it('draws the fields for the eye only, and says them once for screen readers', async () => {
    const { TemplatePreviewElement } = await import('./template-preview.element');
    const element = new TemplatePreviewElement();
    element.definition = contact.definition;
    testHost().append(element);
    await settle(element);

    const sheet = shadow(element).querySelector('.sheet');
    expect(sheet?.getAttribute('aria-hidden')).toBe('true');
    expect(sheet?.hasAttribute('inert')).toBe(true);
    expect(all(element, '.sheet .label').map((label) => text(label))).toEqual(['Name', 'Email address', 'Message']);
    expect(all(element, '.sheet .box--tall')).toHaveLength(1);
    expect(text(shadow(element).querySelector('.visually-hidden'))).toBe('Its fields: Name, Email address, Message.');
  });
});
