// The History view's words (ROADMAP 10.3a, ADR-0196): loaded with the view, not with the backoffice's start, whose budget
// counts every word it loads (ADR-0183), as the Actions view loads its own (ADR-0188).
import { wordsLoader } from '../actions/action-words';

/** Loads the History area's words for a user's language, once each; the view waits for them before it draws. */
export const loadHistoryWords = wordsLoader((code) =>
  code === 'de' ? import('../../lang/history.de') : import('../../lang/history.en'),
);
