// The theme of a form picker's settings (ADR-0135): the site's themes after "The form's own theme", the chosen one, a
// chosen theme the site lacks, a change, read-only, and the states while the themes load. Its read is replaced by a
// hand-answered fake.
import { UmbChangeEvent } from '@umbraco-cms/backoffice/event';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { deferred, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { OWN_THEME, type FormPickerThemeElement } from './form-picker-theme.element';
import type { PickerTheme } from './picker-data';

const reads = vi.hoisted(() => ({
  themes: vi.fn<() => Promise<PickerTheme[] | undefined>>(),
  made: [] as (string | undefined)[],
}));

vi.mock('./picker-data', async (original) => ({
  ...(await original<typeof import('./picker-data')>()),
  formPickerData: (_host: unknown, dataTypeKey: string | undefined) => {
    reads.made.push(dataTypeKey);
    return reads;
  },
}));

const themes: PickerTheme[] = [
  { alias: 'default', label: 'Default' },
  { alias: 'quiet', label: 'Quiet' },
];

interface Option {
  name: string;
  value: string;
  selected: boolean;
}

let changes: number;

async function setting(value?: string, readonly = false) {
  const host = testHost();
  const element = document.createElement('jfb-form-picker-theme') as FormPickerThemeElement;
  element.value = value;
  element.readonly = readonly;
  changes = 0;
  element.addEventListener(UmbChangeEvent.TYPE, () => changes++);
  host.append(element);
  await settle(element);
  return element;
}

const select = (element: Element) =>
  shadow(element).querySelector<HTMLElement & { options: Option[]; value: unknown; readonly: boolean }>('uui-select');
const options = (element: Element) => must(select(element), 'the select').options;

/** Chooses an option as uui-select does: it sets its value and sends change. */
async function pick(element: FormPickerThemeElement, value: string) {
  const field = must(select(element), 'the select');
  field.value = value;
  field.dispatchEvent(new Event('change', { bubbles: true }));
  await settle(element);
}

beforeAll(registerEnglish);

beforeEach(() => {
  reads.themes.mockReset();
  reads.themes.mockResolvedValue(themes);
  reads.made.length = 0;
});

afterEach(() => document.body.replaceChildren());

describe("the form picker's theme", () => {
  it("offers the form's own theme first, then the site's themes by label, with none chosen at first", async () => {
    const element = await setting();

    expect(reads.made).toEqual([undefined]);
    expect(select(element)?.getAttribute('label')).toBe('Theme');
    expect(options(element)).toEqual([
      { name: "The form's own theme", value: OWN_THEME, selected: true },
      { name: 'Default', value: 'default', selected: false },
      { name: 'Quiet', value: 'quiet', selected: false },
    ]);
  });

  it('shows the chosen theme as chosen', async () => {
    const element = await setting('quiet');

    expect(
      options(element)
        .filter((option) => option.selected)
        .map((option) => option.value),
    ).toEqual(['quiet']);
  });

  it('keeps a chosen theme the site no longer has, and says so', async () => {
    const element = await setting('autumn');

    expect(options(element).at(-1)).toEqual({ name: 'autumn (not on this site)', value: 'autumn', selected: true });
    expect(options(element).filter((option) => option.selected)).toHaveLength(1);
    expect(element.value).toBe('autumn');
  });

  it("writes the chosen theme's alias, and no value for the form's own theme", async () => {
    const element = await setting();

    await pick(element, 'default');
    expect(element.value).toBe('default');
    expect(changes).toBe(1);

    await pick(element, OWN_THEME);
    expect(element.value).toBeUndefined();
    expect(changes).toBe(2);
    expect(OWN_THEME).not.toMatch(/^[a-z][a-z0-9-]*$/);
  });

  it('passes read-only on to the select', async () => {
    const element = await setting('quiet', true);

    expect(select(element)?.readonly).toBe(true);
  });

  it("holds the select's place while the themes load", async () => {
    const loading = deferred<PickerTheme[] | undefined>();
    reads.themes.mockReturnValueOnce(loading.promise);
    const element = await setting();

    expect(select(element)).toBeNull();
    expect(shadow(element).querySelector('.placeholder')?.getAttribute('aria-hidden')).toBe('true');
    loading.resolve(themes);
    await settle(element);
    expect(options(element)).toHaveLength(3);
  });

  it('says when the themes cannot be loaded, and Try again loads them', async () => {
    reads.themes.mockResolvedValueOnce(undefined);
    const element = await setting('quiet');

    const alert = shadow(element).querySelector('[role="alert"]');
    expect(text(alert?.querySelector('span'))).toBe("The themes couldn't be loaded.");
    expect(select(element)).toBeNull();
    (alert?.querySelector('uui-button') as HTMLElement).click();
    await settle(element);
    expect(options(element).find((option) => option.selected)?.value).toBe('quiet');
    expect(shadow(element).querySelector('[role="alert"]')).toBeNull();
  });
});
