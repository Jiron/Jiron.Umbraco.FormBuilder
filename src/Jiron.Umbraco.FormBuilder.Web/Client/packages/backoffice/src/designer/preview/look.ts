// What the designer draws a form with (ADR-0168): the canvas endpoint's words in a language of the site, the default
// theme's classes and stylesheets, and the site's marker. The answer and the stylesheets are read once per backoffice
// session and language, and every preview shares them.
import type { ThemeClassesModel } from '@jfb/element/answer';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  canvasGet,
  fieldTypeList,
  type CanvasLanguageResponseModel,
  type CanvasResponseModel,
  type FieldTypeResponseModel,
} from '../../api';

/** How a form marks its fields: "(optional)" on those that need no answer, an asterisk on those that do, or nothing. */
export type Marker = 'optional' | 'required' | 'none';

/** One of the site's languages. */
export type SiteLanguage = CanvasLanguageResponseModel;

/** What a preview draws with. */
export interface CanvasLook {
  /** The site's language the words are in. */
  readonly culture: string;
  /** The site's languages, the default first; the designer edits a form's texts in each. */
  readonly languages: readonly SiteLanguage[];
  /** The words a rendered form brings of its own, by name. */
  readonly words: Readonly<Record<string, string>>;
  /** The default theme's classes, as the views write them. */
  readonly classes: ThemeClassesModel;
  /** The default theme's stylesheets on the site, the base rules first. */
  readonly stylesheets: readonly string[];
  /** The site's marker, for a form whose own is inherit. */
  readonly marker: Marker;
}

// The slots the views always write a class in; the others are left out when a theme adds none.
const SLOTS = [
  'page',
  'label',
  'help',
  'error',
  'control',
  'select',
  'choices',
  'choice',
  'switchChoice',
  'switchInput',
  'affix',
  'counter',
  'navigation',
  'button',
  'buttonPrimary',
  'buttonLink',
  'buttonIcon',
  'progressBar',
  'progressSteps',
  'errorSummary',
  'outcome',
] as const;

const MARKERS: readonly string[] = ['optional', 'required', 'none'];

/** The look of the canvas endpoint's answer; one without a class the views need is refused. */
export function lookOf(answer: CanvasResponseModel): CanvasLook {
  const missing = SLOTS.filter((slot) => typeof answer.classes[slot] !== 'string');
  if (missing.length > 0) throw new Error(`The canvas answer has no class for ${missing.join(', ')}.`);
  return {
    culture: answer.culture,
    languages: answer.languages,
    words: answer.words,
    classes: answer.classes as unknown as ThemeClassesModel,
    stylesheets: answer.stylesheets,
    marker: (MARKERS.includes(answer.requiredMarker) ? answer.requiredMarker : 'optional') as Marker,
  };
}

/** A word of the look by name, its `{0}`, `{1}` taking the values given; a word it lacks is its name. */
export function word(look: CanvasLook, name: string, ...values: string[]): string {
  const text = Object.hasOwn(look.words, name) ? (look.words[name] ?? name) : name;
  return text.replace(/\{(\d)\}/g, (all, at: string) => values[Number(at)] ?? all);
}

const looks = new Map<string, Promise<CanvasLook | undefined>>();

/** The look in a language of the site, the site's default language without one; undefined when it can't be read. */
export function readLook(host: UmbControllerHost, culture?: string): Promise<CanvasLook | undefined> {
  const wanted = culture ?? '';
  let found = looks.get(wanted);
  if (!found) {
    found = tryExecute(host, canvasGet({ query: culture ? { culture } : {} }), { disableNotifications: true }).then(
      ({ data }) => (data ? lookOf(data) : undefined),
    );
    // A failed read is tried again next time.
    void found.then((look) => {
      if (!look) looks.delete(wanted);
    });
    looks.set(wanted, found);
  }
  return found;
}

let types: Promise<Readonly<Record<string, FieldTypeResponseModel>> | undefined> | undefined;

/** The site's field types by alias, read once; undefined when they can't be read, and tried again next time. */
export function readTypes(
  host: UmbControllerHost,
): Promise<Readonly<Record<string, FieldTypeResponseModel>> | undefined> {
  types ??= tryExecute(host, fieldTypeList(), { disableNotifications: true }).then(({ data }) => {
    if (!data) types = undefined;
    return data ? Object.fromEntries(data.types.map((type) => [type.alias, type])) : undefined;
  });
  return types;
}

const sheets = new Map<string, Promise<CSSStyleSheet | undefined>>();

/**
 * The stylesheets, made constructable so that each preview's shadow root adopts the same ones; a stylesheet that can't
 * be read is left out, so the preview still shows its fields.
 */
export async function themeSheets(addresses: readonly string[]): Promise<CSSStyleSheet[]> {
  const read = addresses.map((address) => {
    let found = sheets.get(address);
    if (!found) {
      found = fetch(address, { credentials: 'same-origin' })
        .then((response) => (response.ok ? response.text() : Promise.reject(new Error(String(response.status)))))
        .then((text) => new CSSStyleSheet().replace(text))
        .catch(() => {
          sheets.delete(address);
          return undefined;
        });
      sheets.set(address, found);
    }
    return found;
  });
  return (await Promise.all(read)).filter((sheet): sheet is CSSStyleSheet => sheet !== undefined);
}
