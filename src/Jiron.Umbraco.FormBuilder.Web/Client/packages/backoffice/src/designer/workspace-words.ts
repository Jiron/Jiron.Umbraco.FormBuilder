// The form workspace's words (ADR-0201): the inspector's, the rules', the Settings view's and the form check's, which
// only the form's workspace shows, and an option list's workspace, whose editor is the inspector's (ADR-0214). They
// load before either editor does, not with the backoffice's start, whose budget counts every word it loads (ADR-0183),
// as the Actions and History views load their own.
import { wordsLoader } from './actions/action-words';

/** Loads the form workspace's words for a user's language, once each; the workspace's editor waits for them. */
export const loadWorkspaceWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/workspace.de') : import('../lang/workspace.en'),
);
