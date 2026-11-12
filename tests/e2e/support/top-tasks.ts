import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { expect, type Locator } from '@playwright/test';
import { repoRoot } from './paths.ts';

// The top tasks on the built screens (ROADMAP 16.6): docs/ux/top-tasks.md holds each persona's tasks with their click
// paths and counts, and the specs that do the first five of each read the counts from it, so the document and the
// screens cannot drift apart. A click counts as the document's rules count it: each step of the path from the section's
// start page; the task's own input and its confirming button do not.

/** The document, in the repository. */
export const TOP_TASKS_FILE = join(repoRoot, 'docs', 'ux', 'top-tasks.md');

/** One task of a persona's table. */
export interface TopTask {
  readonly number: number;
  readonly task: string;
  readonly path: string;
  readonly clicks: number;
}

/** Each persona's tasks, by the heading of its section ("Designer", "Entry handler", ...), in the document's order. */
export function readTopTasks(text: string): ReadonlyMap<string, readonly TopTask[]> {
  const personas = new Map<string, TopTask[]>();
  let current: TopTask[] | undefined;
  for (const line of text.split(/\r?\n/)) {
    const heading = /^## (.+)$/.exec(line);
    if (heading) {
      current = [];
      personas.set(heading[1]!.trim(), current);
      continue;
    }
    const row = /^\| (\d+) \| (.+?) \| (.+?) \| (\d+) \|$/.exec(line);
    if (row && current) current.push({ number: Number(row[1]), task: row[2]!, path: row[3]!, clicks: Number(row[4]) });
  }
  for (const [name, tasks] of personas) if (tasks.length === 0) personas.delete(name);
  return personas;
}

/** A persona's task by its number, from the repository's document. */
export function topTask(persona: string, number: number): TopTask {
  const tasks = readTopTasks(readFileSync(TOP_TASKS_FILE, 'utf8')).get(persona);
  const found = tasks?.find((task) => task.number === number);
  if (!found) throw new Error(`docs/ux/top-tasks.md has no task ${String(number)} for ${persona}.`);
  return found;
}

/** Counts the clicks of a task's path; the task's own input and confirming button go around it. */
export class ClickCounter {
  #count = 0;

  /** The clicks counted so far. */
  get count(): number {
    return this.#count;
  }

  /** A step of the path: counted, then clicked. */
  async click(target: Locator): Promise<void> {
    await this.step(() => target.click());
  }

  /** A step a helper clicks, such as a row's "..." that needs the row's hover first: counted, then run. */
  async step(run: () => Promise<unknown>): Promise<void> {
    this.#count += 1;
    await run();
  }
}

/** Holds a task's counted clicks to the document's number for it. */
export function expectTaskClicks(counter: ClickCounter, persona: string, number: number): void {
  const task = topTask(persona, number);
  expect(counter.count, `${persona} ${String(number)}: ${task.task} (${task.path})`).toBe(task.clicks);
}
