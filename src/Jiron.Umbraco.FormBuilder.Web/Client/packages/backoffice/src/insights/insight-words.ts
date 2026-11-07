// The insights' words (ROADMAP 11.5, ADR-0211): loaded with the Insights view, the Overview's Forms at a glance and
// Privacy > Data collection, not with the backoffice's start, whose budget counts every word it loads (ADR-0183), as the
// Entries view loads its own.
import { wordsLoader } from '../designer/actions/action-words';

/** Loads the Insights area's words for a user's language, once each; each screen waits for them before it draws. */
export const loadInsightWords = wordsLoader((code) =>
  code === 'de' ? import('../lang/insights.de') : import('../lang/insights.en'),
);
