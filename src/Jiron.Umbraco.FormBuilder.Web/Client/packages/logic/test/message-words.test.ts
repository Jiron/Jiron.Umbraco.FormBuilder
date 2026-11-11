// The visitor words in seven languages (ROADMAP 14.2, ADR-0230): every language has every word English has, each
// counting word both forms, none empty or broken over lines, and each with the same values in braces as English's, so
// no language drops a value or names one the message never gives. The words file the C# suite compares Core with is
// what the tables say, so a word changed here fails until `npm run words` writes the file again.
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { en } from '../src/lang/messages.en.ts';
import type { MessageWords } from '../src/messages.ts';
import { flatWords, MESSAGE_TABLES, readWords, wordsFile, wordsText } from '../tools/message-words.ts';

const NEWLINE = String.fromCharCode(10);

/** The values a word names in braces, in order of their names. */
const named = (text: string): string =>
  [...text.matchAll(/\{([a-z]+)\}/gi)]
    .map((match) => match[1] ?? '')
    .sort()
    .join();

/** What a table lacks or holds wrong against English's, as `language.word: why`. */
function problems(language: string, words: MessageWords): string[] {
  const english = flatWords(en);
  const own = flatWords(words);
  const found: string[] = [];
  for (const [name, text] of Object.entries(english)) {
    const mine = own[name];
    if (mine === undefined) found.push(`${language}.${name}: missing`);
    else if (mine.trim().length === 0 || mine.includes(NEWLINE)) found.push(`${language}.${name}: empty or broken`);
    else if (named(mine) !== named(text)) found.push(`${language}.${name}: values ${named(mine)} for ${named(text)}`);
  }
  for (const name of Object.keys(own))
    if (!(name in english)) found.push(`${language}.${name}: English has no such word`);
  return found;
}

describe('the visitor words', () => {
  it('come in seven languages', () => {
    expect(Object.keys(MESSAGE_TABLES)).toEqual(['en', 'de', 'fr', 'it', 'nl', 'es', 'da']);
  });

  it('have every word of English in every language, with the same values', () => {
    expect(Object.entries(MESSAGE_TABLES).flatMap(([language, words]) => problems(language, words))).toEqual([]);
  });

  it('find a word a language lacks, one left empty, a value dropped and a word English lacks', () => {
    const lacking = Object.fromEntries(
      Object.entries(MESSAGE_TABLES['fr'] as MessageWords).filter(([name]) => name !== 'answer'),
    );
    const broken = {
      ...lacking,
      email: ' ',
      minLength: { one: 'Saisissez au moins un caract\u00e8re', other: 'Saisissez au moins {min} caract\u00e8res' },
      extra: 'x',
    } as unknown as MessageWords;
    expect(problems('fr', broken)).toEqual([
      'fr.answer: missing',
      'fr.email: empty or broken',
      'fr.minLength.one: values  for min',
      'fr.extra: English has no such word',
    ]);
  });

  it('are written to the file the C# suite compares with', () => {
    expect(readFileSync(wordsFile(), 'utf8')).toBe(wordsText(MESSAGE_TABLES));
    expect(readWords(wordsText(MESSAGE_TABLES))['fr']?.['summaryEntry']).toBe(
      '{label}' + String.fromCharCode(0xa0) + ': {message}',
    );
  });
});
