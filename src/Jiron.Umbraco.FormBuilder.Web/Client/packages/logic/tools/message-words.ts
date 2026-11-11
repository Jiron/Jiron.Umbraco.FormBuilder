// The visitor words' TypeScript half (ROADMAP 14.2, ADR-0230): every word of every language's message table, written
// to tests/vectors/tables/message-words.json for the C# suite to compare with Core's VisitorMessageWords, so the two
// runtimes cannot drift apart in any word of any of the seven languages. The file is plain ASCII, every character above
// "~" a JSON escape, as the vector files are, one word per line, so a changed word shows as a one-line diff.
//   node tools/message-words.ts           write the file
//   node tools/message-words.ts --check   exit 1 when the file differs from the tables
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { da } from '../src/lang/messages.da.ts';
import { de } from '../src/lang/messages.de.ts';
import { en } from '../src/lang/messages.en.ts';
import { es } from '../src/lang/messages.es.ts';
import { fr } from '../src/lang/messages.fr.ts';
import { it } from '../src/lang/messages.it.ts';
import { nl } from '../src/lang/messages.nl.ts';
import type { MessageWords } from '../src/messages.ts';
import { vectorsFolder } from './repository.ts';

/** The format the file declares. */
export const WORDS_FORMAT = 'jfb-message-words/1';

/** Every language's table, English first, as Core's VisitorMessageWords lists them. */
export const MESSAGE_TABLES: Readonly<Record<string, MessageWords>> = { en, de, fr, it, nl, es, da };

/** Where the file is. */
export const wordsFile = (): string => join(vectorsFolder(), 'tables', 'message-words.json');

const BACKSLASH = String.fromCharCode(92);
const NEWLINE = String.fromCharCode(10);

/** JSON text with every code unit above "~" written as a JSON escape. */
function ascii(json: string): string {
  let out = '';
  for (let i = 0; i < json.length; i++) {
    const code = json.charCodeAt(i);
    out += code > 126 ? BACKSLASH + 'u' + code.toString(16).padStart(4, '0') : json[i];
  }
  return out;
}

/** A table as flat words: each word by its name, a counting word's two forms as `name.one` and `name.other`. */
export function flatWords(words: MessageWords): Record<string, string> {
  const flat: Record<string, string> = {};
  for (const [name, value] of Object.entries(words) as [string, unknown][]) {
    if (typeof value === 'string') flat[name] = value;
    else for (const [form, text] of Object.entries(value as Record<string, string>)) flat[`${name}.${form}`] = text;
  }
  return flat;
}

/** The file's text: each language, then its words, one per line, in the tables' order. */
export function wordsText(tables: Readonly<Record<string, MessageWords>>): string {
  const languages = Object.entries(tables).map(([language, words]) => {
    const lines = Object.entries(flatWords(words)).map(
      ([name, text]) => `      ${JSON.stringify(name)}: ${ascii(JSON.stringify(text))}`,
    );
    return `    "${language}": {` + NEWLINE + lines.join(',' + NEWLINE) + NEWLINE + '    }';
  });
  const head = ['{', `  "format": "${WORDS_FORMAT}",`, '  "languages": {'].join(NEWLINE);
  return head + NEWLINE + languages.join(',' + NEWLINE) + NEWLINE + '  }' + NEWLINE + '}' + NEWLINE;
}

/** The words of a written file, by language. */
export function readWords(text: string): Record<string, Record<string, string>> {
  const parsed = JSON.parse(text) as {
    readonly format: string;
    readonly languages: Record<string, Record<string, string>>;
  };
  if (parsed.format !== WORDS_FORMAT) throw new Error(`Not a ${WORDS_FORMAT} file`);
  return parsed.languages;
}

const isMain = process.argv[1] !== undefined && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const path = wordsFile();
  const text = wordsText(MESSAGE_TABLES);
  if (process.argv.includes('--check')) {
    if (!existsSync(path) || readFileSync(path, 'utf8') !== text) {
      console.error(`words: ${path} differs from the message tables; run npm run words`);
      process.exit(1);
    }
    console.log(`words: ${path} matches the message tables`);
  } else {
    writeFileSync(path, text);
    console.log(`words: wrote ${path}: ${String(Object.keys(MESSAGE_TABLES).length)} languages`);
  }
}
