// The global settings' words (ROADMAP 12.4a, ADR-0218): loaded with the settings screens, not with the backoffice's
// start, whose budget counts every word it loads (ADR-0183), as the insights and the actions load their own.
import { wordsLoader } from '../designer/actions/action-words';

/** Loads the global settings' words for a user's language, once each; a screen waits for them before it draws. */
export const loadSettingsWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/global-settings.de') : import('../lang/global-settings.en'),
);
