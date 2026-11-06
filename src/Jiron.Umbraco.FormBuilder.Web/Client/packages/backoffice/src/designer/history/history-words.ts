// The History view's words (ROADMAP 10.3a, ADR-0196): loaded with the view, not with the backoffice's start, whose budget
// counts every word it loads (ADR-0183), as the Actions view loads its own (ADR-0188).
import { umbLocalizationManager } from '@umbraco-cms/backoffice/localization-api';
import { flatten, languagesFor } from '../actions/action-words';

/** The words of each language asked for, loaded once. */
const loaded = new Map<string, Promise<void>>();

/** Loads the History area's words for a user's language, once each; the view waits for them before it draws. */
export function loadHistoryWords(lang: string): Promise<void> {
  const languages = languagesFor(lang);
  const key = languages.join('+');
  let loading = loaded.get(key);
  if (!loading) {
    loading = Promise.all(
      languages.map(async (code) =>
        flatten(
          code,
          (code === 'de' ? await import('../../lang/history.de') : await import('../../lang/history.en')).default,
        ),
      ),
    ).then((sets) => umbLocalizationManager.registerManyLocalizations(sets));
    // A load that failed is tried again by the next view.
    loading.catch(() => loaded.delete(key));
    loaded.set(key, loading);
  }
  return loading;
}
