// The email layout screens' words (ROADMAP 12.1, ADR-0215): loaded with the list and the workspace, not with the
// backoffice's start, whose budget counts every word it loads (ADR-0183).
import { wordsLoader } from '../designer/actions/action-words';

/** Loads the email layout screens' words for a user's language, once each; the screens wait for them. */
export const loadEmailLayoutWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/email-layouts.de') : import('../lang/email-layouts.en'),
);
