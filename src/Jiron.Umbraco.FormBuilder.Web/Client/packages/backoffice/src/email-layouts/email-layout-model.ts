// An email layout as its editor holds it (ROADMAP 12.1, IA 7, ADR-0215): its look, logo, colours of both schemes, and
// its header and footer per language, the site's default language under the empty culture as mail reads them. Pure, so
// it is tested without the DOM.
import { aliasOf } from '../designer/new-elements';
import type { LibraryItemModel } from '../library/items/library-items';

export type Look = 'card' | 'letter';
export type Scheme = 'light' | 'dark';

/** A layout's colours by name, each a hex colour; one it leaves out is mail's own. */
export type Colours = Readonly<Partial<Record<Colour, string>>>;
export type Colour = 'accent' | 'page' | 'surface' | 'text' | 'muted' | 'rule';
export const COLOURS: readonly Colour[] = ['accent', 'page', 'surface', 'text', 'muted', 'rule'];

/** A text per culture, the site's default language under the empty culture. */
export type Texts = Readonly<Record<string, string>>;

export interface EmailLayoutDesign {
  readonly look: Look;
  readonly logoMedia: string | null;
  readonly logoUrl: string | null;
  readonly logoAlt: Texts;
  readonly light: Colours;
  readonly dark: Colours;
  readonly header: Texts;
  readonly footer: Texts;
}

export interface EmailLayoutDetailModel extends LibraryItemModel {
  design: EmailLayoutDesign;
}

/** A layout's alias: a lowercase letter, then letters and digits, up to 64. */
export const isLayoutAlias = (alias: string): boolean => /^[a-z][A-Za-z0-9]{0,63}$/.test(alias);
export const layoutAliasOf = (name: string): string => aliasOf(name, 'layout');

/** A new layout: the card look, mail's own colours, no logo and no texts. */
export const NEW_DESIGN: EmailLayoutDesign = {
  look: 'card',
  logoMedia: null,
  logoUrl: null,
  logoAlt: {},
  light: {},
  dark: {},
  header: {},
  footer: {},
};

/** The key a language's text is held under: the empty culture for the site's default language. */
export const textKey = (culture: string, isDefault: boolean): string => (isDefault ? '' : culture);

/** The texts with one language's changed; a blank text is taken out. */
export function withText(texts: Texts, key: string, text: string): Texts {
  const kept: Record<string, string> = Object.fromEntries(Object.entries(texts).filter(([one]) => one !== key));
  if (text.trim() !== '') kept[key] = text;
  return kept;
}

/** The colours with one changed; a blank one, or one that equals mail's own, is taken out. */
export function withColour(colours: Colours, name: Colour, value: string, own: string | null | undefined): Colours {
  const kept: Partial<Record<Colour, string>> = Object.fromEntries(
    Object.entries(colours).filter(([one]) => one !== name),
  );
  const next = value.trim().toLowerCase();
  if (next !== '' && next !== own?.toLowerCase()) kept[name] = next;
  return kept;
}

/** How many of a scheme's colours the layout sets. */
export const coloursSet = (colours: Colours): number => COLOURS.filter((name) => colours[name] !== undefined).length;
