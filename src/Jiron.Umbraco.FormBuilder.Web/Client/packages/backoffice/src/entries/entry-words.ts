// The Entries view's words (ROADMAP 11.1, ADR-0207): loaded with the view, not with the backoffice's start, whose budget
// counts every word it loads (ADR-0183), as the Actions and History views load theirs.
import { wordsLoader } from '../designer/actions/action-words';

/** Loads the Entries area's words for a user's language, once each; the view waits for them before it draws. */
export const loadEntryWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/entries.de') : import('../lang/entries.en'),
);
