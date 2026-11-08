// The theme screens' words (ROADMAP 12.1, ADR-0215): loaded with the list and the workspace, not with the backoffice's
// start, whose budget counts every word it loads (ADR-0183).
import { wordsLoader } from '../designer/actions/action-words';

/** Loads the theme screens' words for a user's language, once each; the screens wait for them. */
export const loadThemeWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/themes.de') : import('../lang/themes.en'),
);
