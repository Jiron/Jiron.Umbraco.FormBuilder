import { randomUUID } from 'node:crypto';
import type { APIRequestContext, Locator, Page } from '@playwright/test';
import { expect, test } from '../../fixtures.ts';
import { axeInside } from '../../support/axe.ts';
import { API, blankForm, unique } from '../../support/forms-api.ts';

// The designer's moves (ROADMAP 9.3c, ADR-0016, ADR-0169): a form of three pages built with the keyboard only, the
// focus asserted after every operation; fields, a group and a page dragged with the mouse, a refused drop explained in
// place, and a palette type dropped on the page. The 300-field budget is measured alone, in specs/budgets.

const SECTION = '/umbraco/section/jfb-forms';

const workspaceOf = (key: string) => `${SECTION}/workspace/jfb-form/edit/${key}`;

/** Whether an element holds the focus, followed into the shadow roots it lies in. */
const holdsFocus = (target: Locator) =>
  target.evaluate((element) => {
    let found = document.activeElement;
    while (found?.shadowRoot?.activeElement) found = found.shadowRoot.activeElement;
    return found === element;
  });

/** Presses Tab (or Shift+Tab) until the target holds the focus, as a keyboard user goes there. */
async function tabTo(page: Page, target: Locator, back = false, most = 120) {
  for (let pressed = 0; pressed <= most; pressed++) {
    if ((await target.count()) === 1 && (await holdsFocus(target))) return;
    await page.keyboard.press(back ? 'Shift+Tab' : 'Tab');
  }
  throw new Error(`Tab never reached ${target.toString()}`);
}

interface DraftField {
  readonly type: string;
  readonly label?: Record<string, string>;
}

/** A draft's fields, page by page, read through the API; a draft leaves its empty lists out. */
async function draftFields(api: APIRequestContext, key: string): Promise<DraftField[][]> {
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    definition: { pages?: { groups?: { fields?: DraftField[] }[] }[] };
  };
  return (draft.definition.pages ?? []).map((each) => (each.groups ?? []).flatMap((group) => group.fields ?? []));
}

test('a form of three pages is built with the keyboard only, the focus where it belongs after every operation', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const key = await blankForm(api, unique('Keyboard'));
  await page.setViewportSize({ width: 1440, height: 900 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  const palette = design.locator('jfb-designer-palette');
  const search = palette.getByRole('searchbox', { name: 'Search fields' });
  const steps = design.getByRole('navigation', { name: 'Pages' });
  const said = design.locator('[role="status"]');
  const fields = design.locator('jfb-canvas-field');
  const field = (name: RegExp) => design.getByRole('button', { name });
  const type = (name: string) => palette.getByRole('button', { name, exact: true }).first();
  const labels = () =>
    fields.evaluateAll((all) =>
      all.map((each) => each.querySelector(':scope > .bo-chrome > .bo-select')?.getAttribute('aria-label')),
    );
  await expect(design).toContainText('Add your first field');

  // Page 1. A type from the palette's search, picked with Enter: the focus goes to the field added.
  await tabTo(page, search);
  await page.keyboard.type('Short text');
  await tabTo(page, type('Short text'));
  await page.keyboard.press('Enter');
  await expect(field(/^Short text, short text field$/)).toBeFocused();

  // Ctrl+D: the copy, after it, has the focus.
  await page.keyboard.press('Control+d');
  await expect(fields).toHaveCount(2);
  await expect(
    fields
      .nth(1)
      .getByRole('button', { name: /^Short text, / })
      .first(),
  ).toBeFocused();

  // F2 edits its label in place; Enter keeps it and gives the focus back to the field.
  await page.keyboard.press('F2');
  // The canvas's box, not the inspector's Label beside it.
  const box = design.locator('jfb-form-canvas').getByRole('textbox', { name: 'Label' });
  await expect(box).toBeFocused();
  await page.keyboard.press('Control+a');
  await page.keyboard.type('Your name');
  await page.keyboard.press('Enter');
  await expect(field(/^Your name, short text field$/)).toBeFocused();

  // The insert point below it opens the palette at its search; the pick lands there with the focus.
  await tabTo(page, design.getByRole('button', { name: 'Add a field here, after Your name' }));
  await page.keyboard.press('Enter');
  await expect(search).toBeFocused();
  await page.keyboard.press('Control+a');
  await page.keyboard.type('Email');
  await tabTo(page, type('Email'));
  await page.keyboard.press('Enter');
  await expect(field(/^Email, email field$/)).toBeFocused();
  expect(await labels()).toEqual(['Short text, short text field', 'Your name, short text field', 'Email, email field']);

  // Alt+Up moves it one place up; the focus stays on it, and where it is now is said.
  await page.keyboard.press('Alt+ArrowUp');
  await expect
    .poll(labels)
    .toEqual(['Short text, short text field', 'Email, email field', 'Your name, short text field']);
  await expect(field(/^Email, email field$/)).toBeFocused();
  await expect(said).toHaveText('Moved. Email: position 2 of 3 in Group 1, Page 1.');

  // Up goes to the field before it; Delete deletes that one and gives the focus to its neighbour.
  await page.keyboard.press('ArrowUp');
  await expect(field(/^Short text, short text field$/)).toBeFocused();
  await page.keyboard.press('Delete');
  await expect(fields).toHaveCount(2);
  await expect(field(/^Email, email field$/)).toBeFocused();
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'Field deleted.' })).toBeVisible();

  // Ctrl+Z brings it back with the focus.
  await page.keyboard.press('Control+z');
  await expect(fields).toHaveCount(3);
  await expect(field(/^Short text, short text field$/)).toBeFocused();

  // Page 2: Add page shows it with the focus on its Add field, which opens the palette.
  await tabTo(page, steps.getByRole('button', { name: 'Add page' }), true);
  await page.keyboard.press('Enter');
  await expect(steps.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'step');
  await expect(design.getByRole('button', { name: 'Add field' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(search).toBeFocused();
  await page.keyboard.press('Control+a');
  await page.keyboard.type('Long text');
  await tabTo(page, type('Long text'));
  await page.keyboard.press('Enter');
  await expect(field(/^Long text, long text field$/)).toBeFocused();

  // Page 3.
  await tabTo(page, steps.getByRole('button', { name: 'Add page' }), true);
  await page.keyboard.press('Enter');
  await expect(steps.getByRole('button', { name: 'Page 3' })).toHaveAttribute('aria-current', 'step');
  await expect(design.getByRole('button', { name: 'Add field' })).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(search).toBeFocused();
  await page.keyboard.press('Control+a');
  await page.keyboard.type('Date');
  await tabTo(page, type('Date'));
  await page.keyboard.press('Enter');
  const date = field(/^Date, date field$/);
  await expect(date).toBeFocused();

  // Alt+Up at the top of page 3 takes it to the end of page 2, which shows with the focus still on it; Alt+Down back.
  await page.keyboard.press('Alt+ArrowUp');
  await expect(steps.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'step');
  await expect(date).toBeFocused();
  await expect(said).toHaveText('Moved. Date: position 2 of 2 in Group 1, Page 2.');
  await page.keyboard.press('Alt+ArrowDown');
  await expect(steps.getByRole('button', { name: 'Page 3' })).toHaveAttribute('aria-current', 'step');
  await expect(date).toBeFocused();

  // At the end of the form it goes no further, and says why at the field.
  await page.keyboard.press('Alt+ArrowDown');
  await expect(design.locator('.bo-refusal')).toHaveText("It can't move any further.");
  await expect(said).toHaveText("It can't move any further.");
  await expect(date).toBeFocused();

  // Move to...: the first place that can be chosen has the focus, the arrows choose, Enter moves.
  await tabTo(page, design.getByRole('button', { name: /^Move to., Date$/ }));
  await page.keyboard.press('Enter');
  const moveTo = page.locator('jfb-move-to-modal');
  await expect(moveTo).toBeVisible();
  const places = moveTo.getByRole('radio');
  await expect(places).toHaveCount(3);
  await expect(places.nth(2)).toBeDisabled();
  await expect(places.nth(0)).toBeFocused();
  await axeInside(page, 'jfb-move-to-modal');
  await page.keyboard.press('ArrowDown');
  await expect(places.nth(1)).toBeFocused();
  await expect(places.nth(1)).toBeChecked();
  await page.keyboard.press('Enter');
  await expect(moveTo).toHaveCount(0);
  await expect(steps.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'step');
  await expect(date).toBeFocused();
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'Field moved.' })).toBeVisible();

  // Ctrl+Z takes it back to page 3, with the focus.
  await page.keyboard.press('Control+z');
  await expect(steps.getByRole('button', { name: 'Page 3' })).toHaveAttribute('aria-current', 'step');
  await expect(date).toBeFocused();

  // ? shows the shortcuts; Esc closes them and gives the focus back.
  await page.keyboard.press('?');
  const shortcuts = page.locator('jfb-shortcuts-modal');
  await expect(shortcuts).toBeVisible();
  await expect(shortcuts.getByRole('term')).toHaveCount(9);
  await axeInside(page, 'jfb-shortcuts-modal');
  await page.keyboard.press('Escape');
  await expect(shortcuts).toHaveCount(0);
  await expect(date).toBeFocused();

  // Up goes to its group, then to its page.
  await page.keyboard.press('ArrowUp');
  await expect(design.getByRole('button', { name: 'Group 1, group' })).toBeFocused();
  await page.keyboard.press('ArrowUp');
  await expect(design.getByRole('button', { name: 'Page 3, page' })).toBeFocused();
  await axeInside(page, 'jfb-form-design-view');

  // Saved with the keyboard too.
  await tabTo(page, page.getByRole('button', { name: 'Save draft' }));
  await page.keyboard.press('Enter');
  await expect
    .poll(async () => (await draftFields(api, key)).map((each) => each.map((one) => one.type)))
    .toEqual([['text', 'email', 'text'], ['textarea'], ['date']]);
});

test('fields, a group and a page move with the mouse; a refused drop says why where it was dropped', async ({
  page,
  loginAs,
  apiAs,
}) => {
  const api = await apiAs('designer');
  const key = await blankForm(api, unique('Mouse moves'));
  const draft = (await (await api.get(`${API}/form/${key}/draft`)).json()) as {
    form: { concurrencyVersion: number };
    definition: Record<string, unknown>;
  };
  const text = (alias: string, label: string, extra: Record<string, unknown> = {}) => ({
    key: randomUUID(),
    type: 'text',
    alias,
    label: { '': label },
    ...extra,
  });
  const definition = {
    ...draft.definition,
    pages: [
      {
        key: randomUUID(),
        groups: [
          { key: randomUUID(), title: { '': 'You' }, fields: [text('first', 'First name'), text('last', 'Last name')] },
          {
            key: randomUUID(),
            title: { '': 'Guests' },
            fields: [
              {
                key: randomUUID(),
                type: 'repeater',
                alias: 'guests',
                label: { '': 'Guests' },
                fields: [text('guest', 'Guest name')],
              },
            ],
          },
        ],
      },
      { key: randomUUID(), groups: [{ key: randomUUID(), fields: [text('note', 'Note')] }] },
    ],
  };
  const saved = await api.put(`${API}/form/${key}/draft`, {
    data: { concurrencyVersion: draft.form.concurrencyVersion, definition },
  });
  expect(saved.ok(), await saved.text()).toBe(true);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await loginAs('designer');
  await page.goto(workspaceOf(key));
  const design = page.locator('jfb-form-design-view');
  const steps = design.getByRole('navigation', { name: 'Pages' });
  const field = (label: string) =>
    design
      .locator('jfb-canvas-field')
      .filter({ has: page.getByRole('button', { name: `Move ${label}`, exact: true }) })
      .first();
  const order = () =>
    design.locator('jfb-canvas-field').evaluateAll((all) => all.map((each) => each.getAttribute('key')));
  await expect(field('Last name')).toBeVisible();

  /** Drags by a handle to a point, in small steps as a hand does, and lets go there. */
  async function drag(handle: Locator, to: { x: number; y: number }, release = true) {
    await handle.hover({ force: true });
    const from = (await handle.boundingBox())!;
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2);
    await page.mouse.down();
    await page.mouse.move(from.x + from.width / 2 + 8, from.y + from.height / 2 + 8, { steps: 2 });
    await page.mouse.move(to.x, to.y, { steps: 12 });
    if (release) await page.mouse.up();
  }

  // Last name above First name: the line shows the place, the drop moves it, says so with Undo, and selects it.
  const lastKey = await field('Last name').getAttribute('key');
  const first = (await field('First name').boundingBox())!;
  await field('Last name').hover();
  await drag(
    design.getByRole('button', { name: 'Move Last name', exact: true }),
    { x: first.x + 40, y: first.y + 6 },
    false,
  );
  await expect(design.locator('.bo-drag .bo-line')).toBeVisible();
  await expect(design.locator('.bo-drag .bo-ghost')).toHaveText('Last name');
  await page.mouse.up();
  await expect.poll(async () => (await order())[0]).toBe(lastKey);
  await expect(design.getByRole('button', { name: /^Last name, / })).toHaveAttribute('aria-current', 'true');
  await expect(design.getByRole('button', { name: /^Last name, / })).toBeFocused();
  await expect(page.locator('uui-toast-notification').filter({ hasText: 'Field moved.' })).toBeVisible();

  // Into the repeatable group's row: a short text may go there.
  const row = (await design.locator('jfb-canvas-field [data-fields]').first().boundingBox())!;
  await field('First name').hover();
  await drag(design.getByRole('button', { name: 'Move First name', exact: true }), {
    x: row.x + 30,
    y: row.y + row.height - 4,
  });
  await expect(design.locator('jfb-canvas-field jfb-canvas-field')).toHaveCount(2);

  // A repeatable group into a repeatable group is refused: the line says why, and the drop changes nothing.
  const before = await order();
  await design.locator('jfb-designer-palette').getByRole('searchbox', { name: 'Search fields' }).fill('Repeat');
  const repeater = design
    .locator('jfb-designer-palette')
    .getByRole('button', { name: /^Repeatable/ })
    .first();
  const inRow = (await design.locator('jfb-canvas-field [data-fields]').first().boundingBox())!;
  await drag(repeater, { x: inRow.x + 30, y: inRow.y + 4 }, false);
  await expect(design.locator('.bo-drag .bo-line')).toHaveAttribute('data-refused', '');
  await expect(design.locator('.bo-drag .bo-why')).toHaveText("A repeatable group can't hold that.");
  await page.mouse.up();
  await expect(design.locator('[role="status"]')).toHaveText("A repeatable group can't hold that.");
  expect(await order()).toEqual(before);

  // A palette type dropped below the repeatable group lands there as a new field.
  await design.locator('jfb-designer-palette').getByRole('searchbox', { name: 'Search fields' }).fill('Email');
  const email = design.locator('jfb-designer-palette').getByRole('button', { name: 'Email', exact: true }).first();
  const guests = (await design.locator('[data-group]').nth(1).boundingBox())!;
  await drag(email, { x: guests.x + 60, y: guests.y + guests.height - 6 });
  await expect(design.getByRole('button', { name: /^Email, email field$/ })).toBeFocused();

  // Escape ends a drag without a drop.
  const now = await order();
  await field('Last name').hover();
  await drag(
    design.getByRole('button', { name: 'Move Last name', exact: true }),
    { x: guests.x + 60, y: guests.y + guests.height - 6 },
    false,
  );
  await page.keyboard.press('Escape');
  await page.mouse.up();
  await expect(design.locator('.bo-drag')).toHaveCount(0);
  expect(await order()).toEqual(now);

  // The group "Guests" above "You", by its handle.
  const you = (await design.locator('[data-group]').first().boundingBox())!;
  // A group's handle shows over its title or its border, not over one of its fields.
  await design
    .locator('[data-group]')
    .nth(1)
    .hover({ position: { x: 40, y: 6 } });
  await drag(design.getByRole('button', { name: 'Move Guests', exact: true }).first(), { x: you.x + 40, y: you.y + 4 });
  await expect(design.locator('[data-group]').first()).toContainText('Guests');

  // A field onto page 2's step lands at the end of that page, which shows.
  const step2 = (await steps.getByRole('button', { name: 'Page 2' }).boundingBox())!;
  await field('Last name').hover();
  await drag(design.getByRole('button', { name: 'Move Last name', exact: true }), {
    x: step2.x + step2.width / 2,
    y: step2.y + step2.height / 2,
  });
  await expect(steps.getByRole('button', { name: 'Page 2' })).toHaveAttribute('aria-current', 'step');
  expect(await order()).toEqual([expect.any(String), lastKey]);

  // Page 2 before page 1, by its step.
  const step1 = (await steps.getByRole('button', { name: 'Page 1' }).boundingBox())!;
  await drag(steps.getByRole('button', { name: 'Page 2' }), { x: step1.x + 4, y: step1.y + step1.height / 2 });
  await expect(steps.getByRole('button', { name: 'Page 1' })).toHaveAttribute('aria-current', 'step');
  await expect(design.locator('[role="status"]')).toHaveText('Moved. Page 1: page 1 of 2.');
  await axeInside(page, 'jfb-form-design-view');

  // A palette type dropped on a new page, which shows only its sentence, lands in the page's one group.
  await steps.getByRole('button', { name: 'Add page' }).click();
  await expect(design.locator('.bo-empty')).toBeVisible();
  const sentence = (await design.locator('.bo-empty').boundingBox())!;
  await design.locator('jfb-designer-palette').getByRole('searchbox', { name: 'Search fields' }).fill('Number');
  const number = design.locator('jfb-designer-palette').getByRole('button', { name: 'Number', exact: true }).first();
  await drag(number, { x: sentence.x + 40, y: sentence.y + sentence.height + 30 });
  await expect(design.getByRole('button', { name: /^Number, number field$/ })).toBeFocused();

  await page.getByRole('button', { name: 'Save draft' }).click();
  await expect
    .poll(async () => (await draftFields(api, key)).map((each) => each.map((one) => one.label?.[''] ?? one.type)))
    .toEqual([['Note', 'Last name'], ['Guests', 'Email'], ['Number']]);
});
