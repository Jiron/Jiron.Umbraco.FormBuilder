// Every element of the backoffice takes the section's tokens (styles/tokens.ts): the few values UUI's own do not cover
// at AA in Umbraco's three themes, a filled badge's or button's white label in the dark theme among them. The Inbox's
// menu item lacked them, so its badge said its count at under 4.5:1 in the dark theme (16.6's review).
import { readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { sourceFiles } from '../source-files';

const src = join(dirname(fileURLToPath(import.meta.url)), '..');
// A folder whose name starts with a dot is another test's scratch (the API check's), which may go while this one reads.
const sources = sourceFiles(src)
  .filter((file) => !file.split(/[\\/]/).some((part) => part.startsWith('.')))
  .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts') && !file.startsWith('api'))
  .map((file) => ({
    file: relative(src, join(src, file)).split('\\').join('/'),
    text: readFileSync(join(src, file), 'utf8'),
  }));

describe("the section's tokens", () => {
  it('are in the styles of every element that has styles, its own or those of a screen it extends', () => {
    const missing = sources
      .filter(({ text }) => /static override styles = /.test(text))
      .filter(({ text }) => !text.includes('jfbBackofficeTokens') && !/\.\.\.\w+\.styles/.test(text))
      .map(({ file }) => file);
    expect(missing).toEqual([]);
  });
});
