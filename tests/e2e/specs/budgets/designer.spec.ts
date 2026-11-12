import { randomUUID } from 'node:crypto';
import type { APIRequestContext, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { API, blankForm, unique } from '../../support/forms-api.ts';

// The designer's budget (section 4.12, ADR-0169, ADR-0186, ADR-0243): a form of 300 fields keeps every interaction under
// 100 ms, from the input to the next paint, and the main thread is never held 100 ms or more by a task or by the work
// of a frame, nor 50 ms by a frame's rendering, as the browser itself measures them. A frame's whole duration, which
// also counts the wait for the browser's own drawing after the main thread is idle, is reported beside them. The
// budgets project runs alone, after every other project, so nothing else shares the machine while it measures.
//
// A browser draws each kind of thing slowly the first time, as its graphics processor's programs are compiled, and
// keeps them in its profile from then on; a test's browser starts with a new profile every run. So the steps run twice,
// each on a form of 300 fields of its own in a new page: first in the new browser, where the main thread is held to the
// same budgets and each interaction, with that first drawing in it, to 200 ms; then in a second page of the same
// browser, as new as the first but drawn as an editor's browser draws, where every interaction is held to 100 ms.

/** A long animation frame as the browser reports it, with what ran in it. */
interface LongFrame {
  readonly startTime: number;
  readonly duration: number;
  readonly renderStart: number;
  readonly scripts: readonly { readonly duration: number }[];
}

/** What the browser measured over the steps. */
interface Found {
  readonly events: readonly { readonly name: string; readonly duration: number }[];
  readonly tasks: readonly number[];
  readonly frames: readonly { readonly duration: number; readonly work: number; readonly render: number }[];
}

const workspaceOf = (key: string) => `/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}`;

/** A new form of three pages of ten groups of ten text fields. */
async function threeHundred(api: APIRequestContext): Promise<string> {
  const key = await blankForm(api, unique('Three hundred'));
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: Record<string, unknown>;
  };
  let number = 0;
  const definition = {
    ...draft.definition,
    pages: Array.from({ length: 3 }, (_, pageAt) => ({
      key: randomUUID(),
      title: { '': `Part ${pageAt + 1}` },
      groups: Array.from({ length: 10 }, (_, groupAt) => ({
        key: randomUUID(),
        title: { '': `Section ${pageAt * 10 + groupAt + 1}` },
        fields: Array.from({ length: 10 }, () => {
          number++;
          return { key: randomUUID(), type: 'text', alias: `answer${number}`, label: { '': `Question ${number}` } };
        }),
      })),
    })),
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  return key;
}

/** Opens a form in the page and goes through the steps, measuring each with the browser's own entries. */
async function steps(page: Page, key: string): Promise<Found> {
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-canvas-field')).toHaveCount(100);
  await page.waitForTimeout(1000);

  // From here on, the browser's own measures: each interaction from the input to the next paint (Event Timing), each
  // task that held the main thread 50 ms or more (Long Tasks), and each frame that took 50 ms or more with the main
  // thread's own work in it: its scripts and its rendering (Long Animation Frames).
  await page.evaluate(() => {
    const found = {
      events: [] as { name: string; duration: number }[],
      tasks: [] as number[],
      frames: [] as { duration: number; work: number; render: number }[],
    };
    (window as unknown as { jfbBudget: typeof found }).jfbBudget = found;
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) found.events.push({ name: entry.name, duration: entry.duration });
    }).observe({ type: 'event', durationThreshold: 16 } as PerformanceObserverInit);
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) found.tasks.push(entry.duration);
    }).observe({ type: 'longtask' });
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries() as unknown as LongFrame[]) {
        const render = entry.renderStart > 0 ? entry.startTime + entry.duration - entry.renderStart : 0;
        const scripts = entry.scripts.reduce((sum, script) => sum + script.duration, 0);
        found.frames.push({ duration: entry.duration, work: scripts + render, render });
      }
    }).observe({ type: 'long-animation-frame' });
  });

  const question = (at: number) =>
    design.getByRole('button', { name: `Question ${at}, short text field`, exact: true });
  await question(5).click();
  await expect(question(5)).toHaveAttribute('aria-current', 'true');
  await page.keyboard.press('Alt+ArrowDown');
  await expect(question(5)).toBeFocused();
  // Question 5 is after Question 6 now.
  await page.keyboard.press('ArrowDown');
  await expect(question(7)).toBeFocused();
  await page.keyboard.press('Control+d');
  await expect(design.locator('jfb-canvas-field')).toHaveCount(101);
  // The delete offers its Undo in a toast, which the undo closes.
  await page.keyboard.press('Delete');
  await expect(design.locator('jfb-canvas-field')).toHaveCount(100);
  await page.keyboard.press('Control+z');
  await expect(design.locator('jfb-canvas-field')).toHaveCount(101);
  await page.keyboard.press('Control+y');
  await expect(design.locator('jfb-canvas-field')).toHaveCount(100);
  await page.keyboard.press('Control+z');
  await expect(design.locator('jfb-canvas-field')).toHaveCount(101);

  // A drag down the page shown, its line and its refusals checked at every place, held at the bottom edge while the
  // canvas scrolls under it, then a drop.
  const from = design.locator('jfb-canvas-field').nth(2);
  await from.hover();
  const handle = (await from.locator('.bo-handle').boundingBox())!;
  const viewport = design.locator('jfb-form-canvas .bo-viewport');
  const area = (await viewport.boundingBox())!;
  await page.mouse.move(handle.x + 10, handle.y + 10);
  await page.mouse.down();
  await page.mouse.move(handle.x + 20, handle.y + 30, { steps: 2 });
  await page.mouse.move(area.x + 200, area.y + area.height - 12, { steps: 30 });
  for (let at = 0; at < 40; at++) await page.mouse.move(area.x + 200 + (at % 2), area.y + area.height - 12);
  expect(await viewport.evaluate((element) => element.scrollTop)).toBeGreaterThan(200);
  await page.mouse.up();
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'Field moved.' })).toBeVisible();
  await page.waitForTimeout(500);

  return page.evaluate(() => (window as unknown as { jfbBudget: Found }).jfbBudget);
}

/** The highest of each measure, and a line for the report. */
function summary(found: Found) {
  const most = (values: readonly number[]) => Math.round(Math.max(0, ...values));
  const slowest = most(found.events.map((event) => event.duration));
  const task = most(found.tasks);
  const work = most(found.frames.map((frame) => frame.work));
  const render = most(found.frames.map((frame) => frame.render));
  const longest = most(found.frames.map((frame) => frame.duration));
  const said =
    `slowest interaction ${slowest} ms (${found.events.length} over 16 ms), longest task ${task} ms, ` +
    `most work in a frame ${work} ms (rendering ${render} ms), longest frame ${longest} ms`;
  return { slowest, task, work, render, said };
}

test('a form of 300 fields keeps every designer interaction under 100 ms', async ({ page, loginAs, apiAs }) => {
  test.setTimeout(240_000);
  const api = await apiAs('designer');
  const first = await threeHundred(api);
  const second = await threeHundred(api);
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');

  const found = await steps(page, first);
  const cold = summary(found);
  test.info().annotations.push({ type: 'budget', description: `in the new browser: ${cold.said}` });
  // A second page of the same browser, signed in as the first; the first closes, so nothing of it runs meanwhile.
  const next = await page.context().newPage();
  await next.setViewportSize({ width: 1440, height: 900 });
  await page.close();
  const measured = await steps(next, second);
  const warm = summary(measured);
  test.info().annotations.push({ type: 'budget', description: `in a new page of the same browser: ${warm.said}` });

  expect(cold.slowest, JSON.stringify(found.events)).toBeLessThan(200);
  expect(cold.task, JSON.stringify(found.tasks)).toBeLessThan(100);
  expect(cold.work, JSON.stringify(found.frames)).toBeLessThan(100);
  expect(cold.render, JSON.stringify(found.frames)).toBeLessThan(50);
  expect(warm.slowest, JSON.stringify(measured.events)).toBeLessThan(100);
  expect(warm.task, JSON.stringify(measured.tasks)).toBeLessThan(100);
  expect(warm.work, JSON.stringify(measured.frames)).toBeLessThan(100);
  expect(warm.render, JSON.stringify(measured.frames)).toBeLessThan(50);
});
