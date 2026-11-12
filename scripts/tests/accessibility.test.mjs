// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The accessibility table holds every success criterion of WCAG 2.2 at levels A and AA (ROADMAP 16.3): docs/accessibility.md
// has one row for each, at its level, saying whether it applies and how it was tested, and no row for a criterion that is
// not one of them. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';

const TABLE = fileURLToPath(new URL('../../docs/accessibility.md', import.meta.url));

/** WCAG 2.2's success criteria at A and AA, by number, with their level (4.1.1 Parsing is not in 2.2). */
export const CRITERIA = Object.freeze({
  '1.1.1': 'A', '1.2.1': 'A', '1.2.2': 'A', '1.2.3': 'A', '1.2.4': 'AA', '1.2.5': 'AA',
  '1.3.1': 'A', '1.3.2': 'A', '1.3.3': 'A', '1.3.4': 'AA', '1.3.5': 'AA',
  '1.4.1': 'A', '1.4.2': 'A', '1.4.3': 'AA', '1.4.4': 'AA', '1.4.5': 'AA', '1.4.10': 'AA', '1.4.11': 'AA', '1.4.12': 'AA', '1.4.13': 'AA',
  '2.1.1': 'A', '2.1.2': 'A', '2.1.4': 'A',
  '2.2.1': 'A', '2.2.2': 'A',
  '2.3.1': 'A',
  '2.4.1': 'A', '2.4.2': 'A', '2.4.3': 'A', '2.4.4': 'A', '2.4.5': 'AA', '2.4.6': 'AA', '2.4.7': 'AA', '2.4.11': 'AA',
  '2.5.1': 'A', '2.5.2': 'A', '2.5.3': 'A', '2.5.4': 'A', '2.5.7': 'AA', '2.5.8': 'AA',
  '3.1.1': 'A', '3.1.2': 'AA',
  '3.2.1': 'A', '3.2.2': 'A', '3.2.3': 'AA', '3.2.4': 'AA', '3.2.6': 'A',
  '3.3.1': 'A', '3.3.2': 'A', '3.3.3': 'AA', '3.3.4': 'AA', '3.3.7': 'A', '3.3.8': 'AA',
  '4.1.2': 'A', '4.1.3': 'AA',
});

/** The table's rows as { number, level, applies, tested, gaps }, from lines `| 1.1.1 Name | A | ... |`. */
export function rowsOf(markdown) {
  return markdown
    .split(/\r?\n/)
    .filter((line) => /^\| \d+\.\d+\.\d+ /.test(line))
    .map((line) => {
      const cells = line.split('|').slice(1, -1).map((cell) => cell.trim());
      return { number: cells[0].split(' ')[0], level: cells[1], applies: cells[2] ?? '', tested: cells[3] ?? '', gaps: cells[4] ?? '' };
    });
}

/** What is wrong with a table: criteria missing, rows twice or unknown, a wrong level, an empty cell. */
export function problemsOf(markdown) {
  const rows = rowsOf(markdown);
  const problems = [];
  for (const [number, level] of Object.entries(CRITERIA)) {
    const found = rows.filter((row) => row.number === number);
    if (found.length === 0) problems.push(`${number} is missing`);
    else if (found.length > 1) problems.push(`${number} is there ${found.length} times`);
    else if (found[0].level !== level) problems.push(`${number} is level ${level}, not ${found[0].level}`);
  }
  for (const row of rows) {
    if (!(row.number in CRITERIA)) problems.push(`${row.number} is no WCAG 2.2 criterion at A or AA`);
    if (!row.applies) problems.push(`${row.number} says nothing of whether it applies`);
    if (!row.tested) problems.push(`${row.number} says nothing of how it was tested`);
    if (!row.gaps) problems.push(`${row.number} says nothing of known gaps`);
  }
  return problems;
}

test('the list holds the 55 success criteria of WCAG 2.2 at A and AA', () => {
  assert.equal(Object.keys(CRITERIA).length, 55);
  assert.equal(Object.values(CRITERIA).filter((level) => level === 'A').length, 31);
});

test('docs/accessibility.md has a row for each, at its level, with every cell said', () => {
  assert.deepEqual(problemsOf(readFileSync(TABLE, 'utf8')), []);
});

test('a table that leaves one out, holds one twice, names another or says nothing of the testing is refused', () => {
  const row = (number, level, tested = 'axe') => `| ${number} Name | ${level} | Website: yes. | ${tested} | None |`;
  const whole = Object.entries(CRITERIA).map(([number, level]) => row(number, level));
  assert.deepEqual(problemsOf(whole.join('\n')), []);
  assert.deepEqual(problemsOf(whole.slice(1).join('\n')), ['1.1.1 is missing']);
  assert.deepEqual(problemsOf([...whole, row('2.5.8', 'AA')].join('\n')), ['2.5.8 is there 2 times']);
  assert.deepEqual(problemsOf([...whole, row('4.1.1', 'A')].join('\n')), ['4.1.1 is no WCAG 2.2 criterion at A or AA']);
  assert.deepEqual(problemsOf(whole.map((line) => line.replace('| 1.4.3 Name | AA |', '| 1.4.3 Name | A |')).join('\n')), ['1.4.3 is level AA, not A']);
  assert.deepEqual(problemsOf(whole.map((line) => (line.startsWith('| 2.4.7 ') ? row('2.4.7', 'AA', '') : line)).join('\n')), ['2.4.7 says nothing of how it was tested']);
});
