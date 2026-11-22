// Every scroll area of the backoffice positions its box (ROADMAP 16.6). An absolutely positioned helper inside it, such
// as a visually hidden text or a drop line, then takes the scroll area as its containing block: the area clips it and
// it scrolls with its content. In an area that is not positioned, the helper belongs to the next positioned box
// outside, escapes the clip and stretches the workspace's own scroll area: the hidden reasons of the list of types once
// beside the canvas made the Design view scroll by a thousand pixels at 1440 px.
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

const SCROLLS = /overflow(?:-[a-z]+)?:\s*[^;{}]*\b(?:auto|scroll)\b/g;
const POSITIONED = /(?:^|[;{\s])position:\s*(?:relative|absolute|fixed|sticky)\b/;

/** Each style rule that makes its box scroll, with the declarations of that rule. */
function scrollRules(text: string): { line: number; block: string }[] {
  return [...text.matchAll(SCROLLS)].map((match) => {
    const at = match.index;
    const start = text.lastIndexOf('{', at) + 1;
    const end = text.indexOf('}', at);
    return { line: text.slice(0, at).split('\n').length, block: text.slice(start, end === -1 ? undefined : end) };
  });
}

describe('the backoffice scroll areas', () => {
  it('reads the rule a scroll declaration belongs to, and whether that rule positions its box', () => {
    const rules = scrollRules(
      '.a {\n  overflow: auto;\n}\n.b {\n  position: relative;\n  overflow-y: scroll;\n}\n.c { overflow: hidden; }',
    );
    expect(rules.map((rule) => rule.line)).toEqual([2, 6]);
    expect(rules.map((rule) => POSITIONED.test(rule.block))).toEqual([false, true]);
    expect(scrollRules('.d { overflow: hidden auto; }')).toHaveLength(1);
    expect(POSITIONED.test('scroll-position: auto;')).toBe(false);
  });

  it('each position their box, so the helpers inside stay inside', () => {
    const rules = sources.flatMap((source) => scrollRules(source.text).map((rule) => ({ ...rule, file: source.file })));
    expect(rules.length).toBeGreaterThan(10);
    const unpositioned = rules
      .filter((rule) => !POSITIONED.test(rule.block))
      .map((rule) => `${rule.file}:${rule.line}`);
    expect(unpositioned).toEqual([]);
  });
});
