import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import type { Locator } from '@playwright/test';
import { ClickCounter, readTopTasks, TOP_TASKS_FILE, topTask } from './top-tasks.ts';

const SAMPLE = `# Top tasks

Some words.

## Designer

| # | Task | Path | Clicks |
| --- | --- | --- | --- |
| 1 | Start a new, empty form. | sidebar header "+" \u2192 Form | 2 |
| 2 | Start a form from a template. | "+" \u2192 Form from a template\u2026 \u2192 (Use template) | 2 |

## How the paths were checked

1. **The first** found 9 tasks.
`;

test('the tables are read per persona, each row with its number, words, path and count; a section without one is left out', () => {
  const read = readTopTasks(SAMPLE);
  assert.deepEqual([...read.keys()], ['Designer']);
  assert.deepEqual(read.get('Designer'), [
    { number: 1, task: 'Start a new, empty form.', path: 'sidebar header "+" \u2192 Form', clicks: 2 },
    {
      number: 2,
      task: 'Start a form from a template.',
      path: '"+" \u2192 Form from a template\u2026 \u2192 (Use template)',
      clicks: 2,
    },
  ]);
});

test("the repository's document has five tasks or more for each persona the specs walk, and a count for each", () => {
  const read = readTopTasks(readFileSync(TOP_TASKS_FILE, 'utf8'));
  for (const persona of ['Designer', 'Entry handler', 'Viewer', 'Administrator', 'Privacy officer'])
    assert.ok((read.get(persona)?.length ?? 0) >= 5, persona);
  assert.equal(read.get('No access')?.length, 3);
  assert.equal(topTask('Entry handler', 1).clicks, 1);
  assert.throws(() => topTask('Designer', 99), /no task 99 for Designer/);
});

test('the counter counts the steps it clicks, and only those', async () => {
  const clicked: string[] = [];
  const target = (name: string) => ({ click: async () => void clicked.push(name) }) as unknown as Locator;
  const counter = new ClickCounter();
  await counter.click(target('the form'));
  await target('the label').click();
  await counter.click(target('Publish'));
  await counter.step(async () => void clicked.push("a row's ..."));
  assert.equal(counter.count, 3);
  assert.deepEqual(clicked, ['the form', 'the label', 'Publish', "a row's ..."]);
});
