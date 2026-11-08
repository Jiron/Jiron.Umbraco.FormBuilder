// Library > Email layouts (ROADMAP 12.1, IA 7, ADR-0215): each layout by name with its swatch, "Default" on the site's
// default, its look, and the email actions that use it; Create email layout opens a new layout; and the layout API's
// answers read as the workspace holds a layout, a picked logo sent without an address.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { EmailLayoutResponseModel, EmailLayoutRowResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { loadEmailLayoutWords } from './email-layout-words';

const api = vi.hoisted(() => ({ created: [] as unknown[] }));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const layout = (more: Partial<EmailLayoutResponseModel> = {}): EmailLayoutResponseModel => ({
  key: '00000000-0000-4000-8000-000000000009',
  alias: 'brandMail',
  name: 'Brand mail',
  design: {
    look: 'letter',
    logoMedia: null,
    logoUrl: 'https://example.com/logo.png',
    logoAlt: { '': 'Brand' },
    light: { accent: '#1f4fbf', page: null, surface: null, text: null, muted: null, rule: null },
    dark: null,
    header: { '': '<p>Hello</p>' },
    footer: null,
  },
  concurrencyVersion: 2,
  updatedUtc: '2026-11-08T10:00:00Z',
  ...more,
});

const row = (at: number, more: Partial<EmailLayoutRowResponseModel> = {}): EmailLayoutRowResponseModel => ({
  key: `00000000-0000-4000-8000-${String(at).padStart(12, '0')}`,
  alias: `layout${at}`,
  name: `Layout ${at}`,
  look: 'card',
  swatch: ['#f4f5f7', '#ffffff', '#1f4fbf'],
  usedBy: at,
  isDefault: false,
  updatedUtc: '2026-11-08T10:00:00Z',
  ...more,
});

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  emailLayoutLibraryRows: async () => ({
    data: { total: 2, items: [row(1, { isDefault: true, look: 'letter' }), row(0)] },
  }),
  emailLayoutLibraryCreate: async (options: { body: unknown }) => {
    api.created.push(options.body);
    return { data: layout() };
  },
}));

const { EmailLayoutsScreenElement } = await import('./email-layouts.screen');
const { detailOf, createLayout } = await import('./email-layouts-api');

async function screenOf() {
  const screen = new EmailLayoutsScreenElement();
  testHost().append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('tbody')).not.toBeNull());
  await settle(screen);
  return { screen, root: shadow(screen) };
}

beforeAll(async () => {
  registerEnglish();
  await loadEmailLayoutWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
  api.created = [];
  vi.restoreAllMocks();
});

describe('Library > Email layouts', () => {
  it("lists each layout by name with its swatch, its look and the actions that use it, and marks the site's default", async () => {
    const { root } = await screenOf();
    const rows = [...root.querySelectorAll('tbody tr')];
    expect(rows.map((one) => text(one.querySelector('a')))).toEqual(['Layout 1', 'Layout 0']);
    expect(rows[0]?.querySelectorAll('.swatch span')).toHaveLength(3);
    expect(rows[0]?.querySelector('.swatch')?.getAttribute('aria-hidden')).toBe('true');
    expect(text(rows[0]?.querySelector('uui-tag'))).toBe('Default');
    expect(rows[1]?.querySelector('uui-tag')).toBeNull();
    expect([...root.querySelectorAll('thead th')].map((one) => text(one))).toContain('Look');
    expect(rows.map((one) => [...one.querySelectorAll('td')].map((cell) => text(cell)).slice(-2))).toEqual([
      ['Letter', '1 action'],
      ['Card', 'no actions'],
    ]);
    expect(rows[0]?.querySelector('a')?.getAttribute('href')).toBe(
      'section/jfb-forms/workspace/jfb-email-layout/edit/00000000-0000-4000-8000-000000000001',
    );
  });

  it('opens a new layout from Create email layout', async () => {
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { root } = await screenOf();
    must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Create email layout').click();
    await vi.waitFor(() => expect(pushed).toHaveBeenCalledOnce());
    expect(String(pushed.mock.calls[0]?.[2])).toMatch(/\/workspace\/jfb-email-layout\/create$/);
  });
});

describe('the email layout API', () => {
  it('reads a layout as the workspace holds it, and sends a picked logo without an address', async () => {
    const model = detailOf(layout());
    expect(model).toMatchObject({ entityType: 'jfb-email-layout', unique: layout().key, alias: 'brandMail' });
    expect(model.design).toEqual({
      look: 'letter',
      logoMedia: null,
      logoUrl: 'https://example.com/logo.png',
      logoAlt: { '': 'Brand' },
      light: { accent: '#1f4fbf' },
      dark: {},
      header: { '': '<p>Hello</p>' },
      footer: {},
    });
    const picked = { ...model, name: ' Brand mail ', design: { ...model.design, logoMedia: 'media-key' } };
    expect((await createLayout(testHost(), picked)).kind).toBe('saved');
    expect(api.created.at(-1)).toEqual({
      key: layout().key,
      layout: {
        alias: 'brandMail',
        name: 'Brand mail',
        design: { ...model.design, logoMedia: 'media-key', logoUrl: null },
      },
    });
  });
});
