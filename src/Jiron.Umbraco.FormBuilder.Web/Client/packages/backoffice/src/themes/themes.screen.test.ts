// Library > Themes (ROADMAP 12.1, IA 7, ADR-0215): each theme by name with its swatch, "Default" on the site's default,
// and the forms that use it; Create theme asks the base theme first and opens the new theme on it; Import a theme...
// reads a file into a new theme, which opens, and says a file that is no theme; and the theme API's answers read as the
// workspace holds a theme, a blank custom CSS saved as none.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { ThemeResponseModel, ThemeRowResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { loadThemeWords } from './theme-words';

const api = vi.hoisted(() => ({
  chosen: undefined as unknown,
  modalData: undefined as unknown,
  imported: undefined as unknown,
  created: [] as unknown[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, options: { data: unknown }) => {
    api.modalData = options.data;
    return api.chosen ? api.chosen : Promise.reject(new Error('closed'));
  },
}));

const theme = (more: Partial<ThemeResponseModel> = {}): ThemeResponseModel => ({
  key: '00000000-0000-4000-8000-000000000009',
  alias: 'autumn',
  name: 'Autumn',
  baseTheme: 'default',
  tokens: { light: { '--jfb-color-accent': '#8a1538' }, dark: {} },
  customCss: null,
  concurrencyVersion: 2,
  updatedUtc: '2026-11-08T10:00:00Z',
  ...more,
});

const row = (at: number, more: Partial<ThemeRowResponseModel> = {}): ThemeRowResponseModel => ({
  key: `00000000-0000-4000-8000-${String(at).padStart(12, '0')}`,
  alias: `theme-${at}`,
  name: `Theme ${at}`,
  baseLabel: 'Default',
  usedBy: at,
  isDefault: false,
  swatch: ['#fff', '#1d2433', '#0f6e5c'],
  updatedUtc: '2026-11-08T10:00:00Z',
  ...more,
});

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  themeLibraryRows: async () => ({ data: { total: 2, items: [row(1, { isDefault: true }), row(0)] } }),
  themeLibraryEditor: async () => ({
    data: {
      bases: [{ alias: 'bootstrap5', label: 'Bootstrap 5', darkScheme: false, tokens: [] }],
      defaultTheme: 'default',
      maxCustomCssLength: 65536,
      previewAlias: 'jfb-preview',
    },
  }),
  themeLibraryImport: async (options: { body: { document: string } }) => {
    api.created.push(options.body.document);
    return api.imported;
  },
  themeLibraryCreate: async (options: { body: unknown }) => {
    api.created.push(options.body);
    return { data: theme() };
  },
}));

const { ThemesScreenElement } = await import('./themes.screen');
const { detailOf, createTheme } = await import('./themes-api');

async function screenOf() {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const screen = new ThemesScreenElement();
  host.append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('tbody')).not.toBeNull());
  await settle(screen);
  return { screen, root: shadow(screen), peek };
}

/** Chooses a file in the screen's hidden file box. */
async function importFile(root: ShadowRoot, screen: { updateComplete: Promise<unknown> }, content: string) {
  const box = must(root.querySelector<HTMLInputElement>('input[type="file"]'), 'the file box');
  Object.defineProperty(box, 'files', { configurable: true, value: [new File([content], 'autumn.jfb-theme.json')] });
  box.dispatchEvent(new Event('change'));
  await settle(screen);
}

beforeAll(async () => {
  registerEnglish();
  await loadThemeWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
  api.chosen = undefined;
  api.imported = undefined;
  api.created = [];
  vi.restoreAllMocks();
});

describe('Library > Themes', () => {
  it("lists each theme by name with its swatch and the forms that use it, and marks the site's default", async () => {
    const { root } = await screenOf();
    const rows = [...root.querySelectorAll('tbody tr')];
    expect(rows.map((one) => text(one.querySelector('a')))).toEqual(['Theme 1', 'Theme 0']);
    expect(rows[0]?.querySelectorAll('.swatch span')).toHaveLength(3);
    expect(rows[0]?.querySelector('.swatch')?.getAttribute('aria-hidden')).toBe('true');
    expect(text(rows[0]?.querySelector('uui-tag'))).toBe('Default');
    expect(rows[1]?.querySelector('uui-tag')).toBeNull();
    expect(rows.map((one) => text(one.querySelector('td.end')))).toEqual(['1 form', 'no forms']);
    expect(rows[0]?.querySelector('a')?.getAttribute('href')).toBe(
      'section/jfb-forms/workspace/jfb-theme/edit/00000000-0000-4000-8000-000000000001',
    );
  });

  it('asks the base theme first, then opens the new theme on it; closing the dialog opens nothing', async () => {
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { root, screen } = await screenOf();
    const create = () => must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Create theme').click();
    create();
    await settle(screen);
    expect(pushed).not.toHaveBeenCalled();
    expect(api.modalData).toEqual({
      bases: [{ alias: 'bootstrap5', label: 'Bootstrap 5', darkScheme: false, tokens: [] }],
    });
    api.chosen = { baseTheme: 'bootstrap5' };
    create();
    await vi.waitFor(() => expect(pushed).toHaveBeenCalledOnce());
    expect(String(pushed.mock.calls[0]?.[2])).toMatch(/\/workspace\/jfb-theme\/create\/bootstrap5$/);
  });

  it('imports a file into a new theme, which opens, and says a file that is no theme', async () => {
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { root, screen, peek } = await screenOf();
    api.imported = { data: theme({ name: 'Autumn imported' }) };
    await importFile(root, screen, '{"format":"jfb-theme"}');
    await vi.waitFor(() => expect(pushed).toHaveBeenCalledOnce());
    expect(api.created).toEqual(['{"format":"jfb-theme"}']);
    expect(peek).toHaveBeenLastCalledWith('positive', { data: { message: 'Autumn imported was imported.' } });
    expect(String(pushed.mock.calls[0]?.[2])).toMatch(
      /\/workspace\/jfb-theme\/edit\/00000000-0000-4000-8000-000000000009$/,
    );
    api.imported = { error: new UmbApiError('Bad', 400, {}, { status: 400, code: 'invalid_json' } as never) };
    await importFile(root, screen, 'not json');
    await vi.waitFor(() => expect(peek).toHaveBeenCalledTimes(2));
    expect(peek).toHaveBeenLastCalledWith('danger', {
      data: { message: "This file isn't a theme. Choose a file exported from a theme." },
    });
    expect(pushed).toHaveBeenCalledOnce();
  });
});

describe('the theme API', () => {
  it('reads a theme as the workspace holds it, and saves a blank custom CSS as none', async () => {
    const model = detailOf(theme({ customCss: null }));
    expect(model).toMatchObject({
      entityType: 'jfb-theme',
      unique: theme().key,
      alias: 'autumn',
      baseTheme: 'default',
      customCss: '',
    });
    const answer = await createTheme(testHost(), { ...model, customCss: '   ' });
    expect(answer.kind).toBe('saved');
    expect(api.created.at(-1)).toEqual({
      theme: { alias: 'autumn', name: 'Autumn', baseTheme: 'default', tokens: model.tokens, customCss: null },
    });
  });
});
