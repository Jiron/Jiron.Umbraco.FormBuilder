// Settings > Access's and the Permissions sidebar's words (ROADMAP 12.4a, ADR-0218): the same keys in English and German,
// each used by the screens by name, or under the part's prefix the server's answer names.
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import de from '../lang/access.de';
import en from '../lang/access.en';

describe("Access's words", () => {
  it('are the same keys in German as in English', () => {
    expect(Object.keys(de.jfbAccess).sort()).toEqual(Object.keys(en.jfbAccess).sort());
  });

  it('are each used by the screens, by name or under the prefix of the parts the server names', () => {
    const folder = dirname(fileURLToPath(import.meta.url));
    const sources = readdirSync(folder)
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
      .map((file) => readFileSync(join(folder, file), 'utf8'))
      .join('\n');
    expect(sources).toContain('part_');
    const unused = Object.keys(en.jfbAccess).filter((key) => !sources.includes(`'${key}'`) && !key.startsWith('part_'));
    expect(unused).toEqual([]);
  });
});
