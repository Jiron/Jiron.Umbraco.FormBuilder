// A theme's view (ROADMAP 12.1, IA 7, ADR-0215): the editor lists the tokens a base theme declares with their defaults,
// a site's own base theme's among them, and offers dark colours only where the base theme's DarkScheme is true; a colour
// changed becomes the theme's, its check says what it needs, and the colour it offers is one click; the preview asks
// after a pause with the theme as edited; a closed section says in one line what it holds; a save's problem shows at its
// token.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { ThemeEditorResponseModel } from '../api';
import type { LibraryProblem } from '../library/items/library-items';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { THEME_WORKSPACE_CONTEXT } from './constants';
import type { ThemeDetailModel, ThemeTokens } from './theme-model';
import { loadThemeWords } from './theme-words';

const api = vi.hoisted(() => ({ previewed: [] as { baseTheme: string; tokens: unknown; customCss: string }[] }));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const colour = (name: string, light: string, dark: string | null) => ({ name, default: light, darkDefault: dark });

const editor: ThemeEditorResponseModel = {
  bases: [
    {
      alias: 'default',
      label: 'Default',
      darkScheme: true,
      tokens: [
        colour('--jfb-color-text', '#1d2433', '#e8eaee'),
        colour('--jfb-color-surface', '#fff', '#1d232d'),
        colour('--jfb-color-border', '#767c88', '#8b95a5'),
        colour('--jfb-color-accent', '#0f6e5c', '#5fd0b8'),
        { name: '--jfb-radius', default: '12px' },
      ],
    },
    {
      alias: 'acme',
      label: 'Acme',
      darkScheme: true,
      tokens: [colour('--jfb-color-ink', '#102030', '#f0f0f0'), { name: '--jfb-gutter', default: '18px' }],
    },
    { alias: 'plain', label: 'Plain', darkScheme: false, tokens: [{ name: '--jfb-color-ink', default: '#000' }] },
  ],
  defaultTheme: 'default',
  maxCustomCssLength: 65536,
  previewAlias: 'jfb-preview',
};

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  themeLibraryEditor: async () => ({ data: editor }),
  themeLibraryPreview: async (options: { body: (typeof api.previewed)[number] }) => {
    api.previewed.push(options.body);
    return { data: { alias: 'jfb-preview', css: '.jfb-form{}', removed: [], problems: [] } };
  },
  themeLibrarySample: async () => ({
    data: {
      html: '<!doctype html><html><head><style id="jfb-theme-layer"></style></head><body></body></html>',
      culture: 'en-US',
      layerId: 'jfb-theme-layer',
      darkClass: 'jfb-scheme-dark',
    },
  }),
}));

const { ThemeViewElement } = await import('./theme.view');

type Shown = Pick<ThemeDetailModel, 'baseTheme' | 'tokens' | 'customCss'>;

function contextOf(shown: Shown) {
  const theme = new UmbObjectState<Shown | undefined>(shown);
  const problems = new UmbObjectState<readonly LibraryProblem[]>([]);
  return {
    getEntityType: () => 'jfb-theme',
    getHostElement: () => document.body,
    theme: theme.asObservable(),
    problems: problems.asObservable(),
    setTokens: vi.fn((tokens: ThemeTokens) => theme.update({ tokens })),
    setCustomCss: vi.fn((customCss: string) => theme.update({ customCss })),
    problems$: problems,
  };
}

async function viewOf(baseTheme: string, tokens: ThemeTokens = { light: {}, dark: {} }) {
  const context = contextOf({ baseTheme, tokens, customCss: '' });
  const host = testHost();
  host.provideContext(THEME_WORKSPACE_CONTEXT, context as never);
  const view = new ThemeViewElement();
  host.append(view);
  await vi.waitFor(() => expect(shadow(view).querySelector('.page')).not.toBeNull());
  await settle(view);
  return { view, root: shadow(view), context };
}

const input = (root: ShadowRoot, id: string) => must(root.querySelector<HTMLInputElement>(`#${CSS.escape(id)}`), id);

const open = async (root: ShadowRoot, view: { updateComplete: Promise<unknown> }, name: string) => {
  const button = [...root.querySelectorAll<HTMLButtonElement>('.section h3 button')].find((one) =>
    text(one).startsWith(name),
  );
  must(button, name).click();
  await settle(view);
};

beforeAll(async () => {
  registerEnglish();
  await loadThemeWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
  api.previewed = [];
});

describe("a theme's view", () => {
  it("lists a site's own base theme's tokens with their defaults, and its colours' dark values where it has a dark scheme", async () => {
    const { view, root } = await viewOf('acme');
    expect(text(root.querySelector('.base'))).toBe('Builds on Acme');
    expect(text(root.querySelector('#group-more'))).toBe('More colours');
    const ink = input(root, 'colour-light-jfb-color-ink');
    expect([ink.value, ink.placeholder]).toEqual(['', '#102030']);
    const dark = must(root.querySelector<HTMLElement>('uui-button[label="Dark"]'), 'Dark');
    dark.dispatchEvent(new Event('click'));
    await settle(view);
    expect(input(root, 'colour-dark-jfb-color-ink').placeholder).toBe('#f0f0f0');
    await open(root, view, 'Other tokens');
    const gutter = input(root, 'token-light-jfb-gutter');
    expect([gutter.placeholder, text(root.querySelector('label[for="token-light-jfb-gutter"]'))]).toEqual([
      '18px',
      '--jfb-gutter',
    ]);
    expect(root.querySelectorAll('iframe')).toHaveLength(2);
  });

  it('offers no dark colours for a base theme without a dark scheme, and previews the light scheme alone', async () => {
    const { root } = await viewOf('plain');
    expect(root.querySelector('uui-button[label="Dark"]')).toBeNull();
    expect(input(root, 'colour-light-jfb-color-ink').placeholder).toBe('#000');
    expect(root.querySelector('[id^="colour-dark-"]')).toBeNull();
    expect([...root.querySelectorAll('iframe')].map((frame) => frame.dataset['scheme'])).toEqual(['light']);
  });

  it("makes a changed colour the theme's, says what its check needs, and sets the colour it offers on request", async () => {
    const { view, root, context } = await viewOf('default');
    const border = input(root, 'colour-light-jfb-color-border');
    border.value = '#c9ced6';
    border.dispatchEvent(new Event('change'));
    await settle(view);
    expect(context.setTokens).toHaveBeenLastCalledWith({ light: { '--jfb-color-border': '#c9ced6' }, dark: {} });
    const check = must(root.querySelector('#colour-light-jfb-color-border-check'), 'the check');
    expect(text(check)).toMatch(/^1\.5:1 on the surface: controls need 3:1\. Use #[0-9a-f]{6}$/);
    must(check.querySelector<HTMLButtonElement>('button'), 'Use').click();
    await settle(view);
    const used = context.setTokens.mock.lastCall?.[0].light['--jfb-color-border'];
    expect(used).toMatch(/^#[0-9a-f]{6}$/);
    expect(text(root.querySelector('#colour-light-jfb-color-border-check'))).toMatch(/on the surface: passes$/);
    await vi.waitFor(() =>
      expect(api.previewed.at(-1)?.tokens).toEqual({ light: { '--jfb-color-border': used }, dark: {} }),
    );
  });

  it("says in one line what a closed section holds, and a save's problem at its token", async () => {
    const { view, root, context } = await viewOf('default', { light: { '--jfb-color-accent': '#8a1538' }, dark: {} });
    const colours = must(root.querySelector<HTMLButtonElement>('.section h3 button'), 'Colours');
    colours.click();
    await settle(view);
    expect(text(colours.querySelector('.summary'))).toBe('1 colour changed');
    colours.click();
    context.problems$.setValue([{ code: 'invalid_value', path: "$.tokens.light['--jfb-color-accent']", message: '' }]);
    await settle(view);
    expect(input(root, 'colour-light-jfb-color-accent').getAttribute('aria-invalid')).toBe('true');
    expect(text(root.querySelector('.colour .why'))).toBe(
      'A value is one CSS value, such as #1f4fbf or 12px, without an address.',
    );
  });
});
