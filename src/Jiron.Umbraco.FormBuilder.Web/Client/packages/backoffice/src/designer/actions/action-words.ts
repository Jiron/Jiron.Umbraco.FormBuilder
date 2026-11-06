// The Actions view's words (ROADMAP 10.2a, ADR-0188): loaded with the view, not with the backoffice's start, whose
// budget counts every word it loads (ADR-0183). They are registered with Umbraco's localization manager, which merges a
// language's words with those it has, before the view draws, so no key ever shows: English, which every language falls
// back to, and German for a user who reads it.
import {
  umbLocalizationManager,
  type UmbLocalizationDictionary,
  type UmbLocalizationSet,
} from '@umbraco-cms/backoffice/localization-api';

/** A dictionary's words as Umbraco looks them up: `<area>_<key>`, for one language. */
export function flatten(code: string, dictionary: UmbLocalizationDictionary): UmbLocalizationSet {
  const set: UmbLocalizationSet = { $code: code, $dir: 'ltr' };
  for (const [area, words] of Object.entries(dictionary))
    for (const [key, word] of Object.entries(words)) set[`${area}_${key}`] = word;
  return set;
}

/** The languages whose words a user of this language reads: English always, German for German. */
export const languagesFor = (lang: string): readonly ('en' | 'de')[] =>
  lang.toLowerCase().split('-')[0] === 'de' ? ['en', 'de'] : ['en'];

/**
 * A loader of some words not at the backoffice's start: for a user's language it loads the words of each language they
 * read, once, and registers them; a load that failed is tried again by the next caller.
 */
export function wordsLoader(
  wordsOf: (code: 'en' | 'de') => Promise<{ readonly default: UmbLocalizationDictionary }>,
): (lang: string) => Promise<void> {
  const loaded = new Map<string, Promise<void>>();
  return (lang) => {
    const languages = languagesFor(lang);
    const key = languages.join('+');
    let loading = loaded.get(key);
    if (!loading) {
      loading = Promise.all(languages.map(async (code) => flatten(code, (await wordsOf(code)).default))).then((sets) =>
        umbLocalizationManager.registerManyLocalizations(sets),
      );
      loading.catch(() => loaded.delete(key));
      loaded.set(key, loading);
    }
    return loading;
  };
}

/** Loads the Actions area's words for a user's language, once each; the view waits for them before it draws. */
export const loadActionWords = wordsLoader((code) =>
  code === 'de' ? import('../../lang/actions.de') : import('../../lang/actions.en'),
);
