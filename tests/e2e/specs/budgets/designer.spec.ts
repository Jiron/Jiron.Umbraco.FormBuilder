import { randomUUID } from 'node:crypto';
import { expect, test } from '../../fixtures.ts';
import { API, blankForm, unique } from '../../support/forms-api.ts';

// The designer's budget (section 4.12, ADR-0169): a form of 300 fields keeps every interaction under 100 ms, from the
// input to the next paint, and every frame under 100 ms, as the browser itself measures them. The budgets project runs
// alone, after every other project, so nothing else shares the machine while it measures.

const workspaceOf = (key: string) => `/umbraco/section/jfb-forms/workspace/jfb-form/edit/${key}`;

test('a form of 300 fields keeps every designer interaction under 100 ms', async ({ page, loginAs, apiAs }) => {
  const api = await apiAs('designer');
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
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  await expect(design.locator('jfb-canvas-field')).toHaveCount(100);
  await page.waitForTimeout(1000);

  // From here on, the browser's own measures: each interaction from the input to the next paint (Event Timing), and
  // every frame the main thread kept longer than 50 ms (Long Animation Frames).
  await page.evaluate(() => {
    const found = { events: [] as { name: string; duration: number }[], frames: [] as number[] };
    (window as unknown as { jfbBudget: typeof found }).jfbBudget = found;
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) found.events.push({ name: entry.name, duration: entry.duration });
    }).observe({ type: 'event', durationThreshold: 16 } as PerformanceObserverInit);
    new PerformanceObserver((list) => {
      for (const entry of list.getEntries()) found.frames.push(entry.duration);
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
  await page.keyboard.press('Delete');
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

  const found = await page.evaluate(
    () =>
      (window as unknown as { jfbBudget: { events: { name: string; duration: number }[]; frames: number[] } })
        .jfbBudget,
  );
  const slowest = Math.max(0, ...found.events.map((event) => event.duration));
  const longest = Math.max(0, ...found.frames);
  test.info().annotations.push({
    type: 'budget',
    description: `slowest interaction ${slowest} ms (${found.events.length} over 16 ms), longest frame ${longest} ms`,
  });
  expect(slowest, JSON.stringify(found.events)).toBeLessThan(100);
  expect(longest, JSON.stringify(found.frames)).toBeLessThan(100);
});
