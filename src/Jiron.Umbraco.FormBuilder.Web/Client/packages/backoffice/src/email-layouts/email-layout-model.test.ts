// An email layout as its editor holds it (ROADMAP 12.1, ADR-0215): the site's default language's texts under the empty
// culture, a blank text taken out; a colour kept in lowercase, a blank one or mail's own taken out; the count of a
// scheme's colours; and the alias a name suggests.
import { describe, expect, it } from 'vitest';
import {
  coloursSet,
  isLayoutAlias,
  layoutAliasOf,
  NEW_DESIGN,
  textKey,
  withColour,
  withText,
} from './email-layout-model';

describe('an email layout as its editor holds it', () => {
  it("keeps the site's default language's texts under the empty culture, another's under its culture", () => {
    expect(textKey('en-US', true)).toBe('');
    expect(textKey('de-DE', false)).toBe('de-DE');
  });

  it("changes one language's text and takes a blank one out", () => {
    const texts = withText({ '': '<p>Hello</p>' }, 'de-DE', '<p>Hallo</p>');
    expect(texts).toEqual({ '': '<p>Hello</p>', 'de-DE': '<p>Hallo</p>' });
    expect(withText(texts, '', '  ')).toEqual({ 'de-DE': '<p>Hallo</p>' });
    expect(withText(texts, 'de-DE', '<p>Servus</p>')).toEqual({ '': '<p>Hello</p>', 'de-DE': '<p>Servus</p>' });
  });

  it("keeps a colour in lowercase, and takes a blank one or one equal to mail's own out", () => {
    const colours = withColour({}, 'accent', '#1F4FBF', '#0b5cad');
    expect(colours).toEqual({ accent: '#1f4fbf' });
    expect(withColour(colours, 'accent', '', '#0b5cad')).toEqual({});
    expect(withColour(colours, 'accent', '#0B5CAD', '#0b5cad')).toEqual({});
    expect(withColour(colours, 'page', '#f4f5f7', null)).toEqual({ accent: '#1f4fbf', page: '#f4f5f7' });
  });

  it("counts the colours a scheme sets, and starts a new layout on the card look with mail's own colours", () => {
    expect(coloursSet({ accent: '#1f4fbf', text: '#111111' })).toBe(2);
    expect(coloursSet(NEW_DESIGN.light)).toBe(0);
    expect(NEW_DESIGN).toMatchObject({ look: 'card', logoMedia: null, logoUrl: null, header: {}, footer: {} });
  });

  it('suggests an alias of the name and accepts only an alias a layout may have', () => {
    expect(layoutAliasOf('Brand mail')).toBe('brandMail');
    expect(isLayoutAlias(layoutAliasOf('Brand mail'))).toBe(true);
    expect(isLayoutAlias('Brand')).toBe(false);
    expect(isLayoutAlias('brand-mail')).toBe(false);
    expect(isLayoutAlias(`a${'b'.repeat(64)}`)).toBe(false);
  });
});
