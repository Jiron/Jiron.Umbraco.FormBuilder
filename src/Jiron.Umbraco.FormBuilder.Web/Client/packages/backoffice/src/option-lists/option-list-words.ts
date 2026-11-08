// The option list screens' words (ROADMAP 12.1, ADR-0214): loaded with the list and the workspace, not with the
// backoffice's start, whose budget counts every word it loads (ADR-0183), as the form workspace's are (ADR-0201).
import { wordsLoader } from '../designer/actions/action-words';

/** Loads the option list screens' words for a user's language, once each; the screens wait for them. */
export const loadOptionListWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/option-lists.de') : import('../lang/option-lists.en'),
);
