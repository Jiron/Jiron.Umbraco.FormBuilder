// The backoffice's one rule for text kept for assistive technology and hidden from sight (ROADMAP 16.6): a 1 px box
// that takes no room of its own, so it never widens a scroll area that holds it, defined once and shared.
import { readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { jfbVisuallyHidden } from './tokens';
import { sourceFiles } from '../source-files';

const src = join(dirname(fileURLToPath(import.meta.url)), '..');

describe('the visually hidden rule', () => {
  it('keeps a 1 px box without padding, border or margin of its own', () => {
    const text = jfbVisuallyHidden.cssText.replace(/\s+/g, ' ');
    for (const declaration of [
      'position: absolute !important',
      'inline-size: 1px !important',
      'block-size: 1px !important',
      'margin: -1px !important',
      'padding: 0 !important',
      'border: 0 !important',
      'overflow: hidden !important',
      'clip-path: inset(50%) !important',
    ])
      expect(text).toContain(declaration);
  });

  it('is defined once, in the tokens, and shared from there', () => {
    // A folder whose name starts with a dot is another test's scratch (the API check's), which may go while this one reads.
    const defining = sourceFiles(src)
      .filter((file) => !file.split(/[\\/]/).some((part) => part.startsWith('.')))
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts') && !file.startsWith('api'))
      .filter((file) => /^\s*\.visually-hidden\s*\{/m.test(readFileSync(join(src, file), 'utf8')))
      .map((file) => relative(src, join(src, file)).split('\\').join('/'));
    expect(defining).toEqual(['styles/tokens.ts']);
  });
});
