/**
 * What the runtime's tests give their forms besides their fields: the English formats and words for yes and no, as the
 * shared vectors pin them (tests/vectors/messages.json), so no test writes them down again.
 */
import type { CultureFormats, DisplayWords } from '@jfb/logic';
import messages from '../../../../../../tests/vectors/messages.json';

export const ENGLISH: { readonly formats: CultureFormats; readonly words: DisplayWords } = {
  formats: messages.defaults.cultures['en-US'],
  words: messages.defaults.words['en-US'],
};
