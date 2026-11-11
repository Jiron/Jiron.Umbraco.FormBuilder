// What a translator has left in the language shown (ROADMAP 14.1, ADR-0228): a text written in some language but not in
// this one, which visitors in it read in the default language. The canvas, the Settings view and the Actions view mark
// each element that holds one; the field's own test is the inspector model's `untranslated`. Pure, read from the draft.
import type { FieldSettingResponseModel } from '../api';
import type { DesignGroup, DesignPage, LocalizedText } from './definition';
import { dictionaryKeyOf } from './dictionary-texts';
import { keyIn } from './preview/field-models';

/** The texts of the Settings view that are a visitor's (ADR-0228): the three buttons' labels and the closed message. */
export const SETTING_TEXTS = [
  ['submission', 'submitLabel'],
  ['submission', 'nextLabel'],
  ['submission', 'backLabel'],
  ['availability', 'closedMessage'],
] as const;

export type SettingText = (typeof SETTING_TEXTS)[number][1];

const written = (texts: LocalizedText | null | undefined) =>
  Object.values(texts ?? {}).some((text) => typeof text === 'string' && text.trim() !== '');

/**
 * Whether a text lacks the language: it is written in some language, but neither in this one nor in a more general one
 * (`de` for `de-CH`), so visitors in it read the default's. A default that names a dictionary item (`#key`) lacks
 * nothing: the dictionary holds its text in each language, as the inspector's line under it says. Ask it only for a
 * language that is not the site's default, whose text is the default text (`''`) itself.
 */
export function lacks(texts: LocalizedText | null | undefined, culture: string): boolean {
  return written(texts) && keyIn(texts, culture) === '' && dictionaryKeyOf(texts?.['']) === null;
}

const textOf = (element: Readonly<Record<string, unknown>>, name: string) =>
  element[name] as LocalizedText | null | undefined;

/** Whether a page's title or description lacks the language. */
export const pageLacks = (page: DesignPage, culture: string): boolean =>
  lacks(page.title, culture) || lacks(textOf(page, 'description'), culture);

/** Whether a group's title or description lacks the language. */
export const groupLacks = (group: DesignGroup, culture: string): boolean =>
  lacks(group.title, culture) || lacks(textOf(group, 'description'), culture);

/** Whether an outcome's message lacks the language. */
export const outcomeLacks = (outcome: Readonly<Record<string, unknown>>, culture: string): boolean =>
  lacks(textOf(outcome, 'message'), culture);

/**
 * Whether one of an action's texts lacks the language: each setting its type declares a text (an email's subject and
 * body), nested settings included.
 */
export function actionLacks(
  settings: Readonly<Record<string, unknown>> | null | undefined,
  schema: readonly FieldSettingResponseModel[],
  culture: string,
): boolean {
  const values = settings ?? {};
  return schema.some((setting) => {
    const value = values[setting.name];
    if (setting.kind === 'localizedText' || setting.kind === 'localizedHtml')
      return lacks(value as LocalizedText | null | undefined, culture);
    return (
      setting.settings.length > 0 &&
      !!value &&
      typeof value === 'object' &&
      !Array.isArray(value) &&
      actionLacks(value as Readonly<Record<string, unknown>>, setting.settings, culture)
    );
  });
}

/** The Settings view's texts that lack the language, by name. */
export function settingsLacking(
  settings: Readonly<Record<string, unknown>> | null | undefined,
  culture: string,
): ReadonlySet<SettingText> {
  const lacking = new Set<SettingText>();
  for (const [group, name] of SETTING_TEXTS) {
    const values = (settings?.[group] ?? null) as Readonly<Record<string, unknown>> | null;
    if (values && lacks(textOf(values, name), culture)) lacking.add(name);
  }
  return lacking;
}
