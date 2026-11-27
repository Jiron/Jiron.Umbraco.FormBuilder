// What sits at a box's edge keeps its room (17.0.1): a dropdown's arrow is drawn by the section, a text's inset from the
// box's end, as the browser's own sits against the edge; a search box's icon keeps the box's inset; and what a
// sidebar's footer holds at its start lines up with the content above it. The rules are in the section's tokens, which
// every element takes (tokens-everywhere.test.ts), so no screen can miss them.
import { readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { sourceFiles } from '../source-files';
import { jfbBackofficeTokens } from './tokens';

const src = join(dirname(fileURLToPath(import.meta.url)), '..');
const text = jfbBackofficeTokens.cssText.replace(/\s+/g, ' ');

describe("the section's edges", () => {
  it("draws a dropdown's arrow itself, with room before it, and leaves a list box and a visitor's form alone", () => {
    const rule = /:host select:not\(\[multiple\], \[size\], \.jfb-form \*\) \{([^}]*)\}/.exec(text)?.[1] ?? '';
    expect(rule).toContain('appearance: none;');
    expect(rule).toContain('padding-inline-end: calc(var(--uui-size-space-4) * 2 + 10px);');
    expect(rule.split('linear-gradient(').length - 1).toBe(2);
    // In the text's colour, so the arrow follows Umbraco's themes and forced colours.
    expect(rule.split('currentColor').length - 1).toBe(4);
    expect(rule).toContain('calc(100% - var(--uui-size-space-4)) 50%;');
    // Only the image, its place and its size: an element's own fill stays its own.
    expect(rule).not.toMatch(/background(-color)?:/);
  });

  it('mirrors the arrow where the backoffice reads from right to left', () => {
    expect(text).toContain(':host select:not([multiple], [size], .jfb-form *):dir(rtl) { background-position:');
  });

  it("keeps a search box's icon the box's inset from its edge", () => {
    expect(text).toContain("uui-input > uui-icon[slot='prepend'] { margin-inline-start: var(--uui-size-space-3);");
  });

  it('lines up what a footer holds at its start with the content, and a second one beside the first', () => {
    expect(text).toContain("umb-body-layout > [slot='footer-info'] { margin-inline-start: var(--uui-size-layout-1); }");
    expect(text).toContain(
      "umb-body-layout > [slot='footer-info'] ~ [slot='footer-info'] { margin-inline-start: var(--uui-size-space-2); }",
    );
  });

  it("uses Umbraco's own dropdown, whose arrow the section cannot move, only among Umbraco's own controls", () => {
    const users = sourceFiles(src)
      .filter((file) => !file.split(/[\\/]/).some((part) => part.startsWith('.')))
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts'))
      .filter((file) => readFileSync(join(src, file), 'utf8').includes('<uui-select'))
      .map((file) => relative(src, join(src, file)).split('\\').join('/'));
    // The form picker's theme is a setting of a property editor, in Umbraco's content editor.
    expect(users).toEqual(['picker/form-picker-theme.element.ts']);
  });

  it("lets no element's own rule put the browser's arrow back or take the section's away", () => {
    const offenders = sourceFiles(src)
      .filter((file) => !file.split(/[\\/]/).some((part) => part.startsWith('.')))
      .filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts') && !file.startsWith('styles'))
      .filter((file) => {
        const source = readFileSync(join(src, file), 'utf8');
        // A rule for a dropdown of the section (not the canvas's .bo-select, a button) that names what the arrow needs.
        return [...source.matchAll(/(?:^|[\s,>])select(?![\w-])[^{};/]*\{([^}]*)\}/g)].some((match) =>
          /appearance:|background-image:|padding-inline-end:|padding-right:/.test(match[1] ?? ''),
        );
      })
      .map((file) => relative(src, join(src, file)).split('\\').join('/'));
    expect(offenders).toEqual([]);
  });
});
