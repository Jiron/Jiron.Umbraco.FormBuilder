// Access's words (ROADMAP 12.4a, ADR-0218): loaded with Settings > Access and the Permissions sidebar, not with the
// backoffice's start, whose budget counts every word it loads (ADR-0183).
import { wordsLoader } from '../designer/actions/action-words';

/** Loads the Access area's words for a user's language, once each; a screen waits for them before it draws. */
export const loadAccessWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/access.de') : import('../lang/access.en'),
);
