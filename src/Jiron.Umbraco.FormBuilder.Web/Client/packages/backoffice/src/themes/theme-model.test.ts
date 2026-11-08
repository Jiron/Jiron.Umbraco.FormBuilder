// A theme as its editor holds it (ROADMAP 12.1, ADR-0215): the WCAG contrast of two colours and each colour's check
// against what it is read on, with the nearest colour of the same hue that passes; an alias made of a name; a token's
// value in each scheme and a change that drops what equals the base theme's default; and the sections' choices read
// from the tokens and written back.
import { describe, expect, it } from 'vitest';
import {
  checkOf,
  choicesOf,
  contrast,
  hexOf,
  isThemeAlias,
  luminance,
  othersOf,
  ratioText,
  suggestion,
  themeAliasOf,
  valueIn,
  withChoice,
  withToken,
  type ThemeTokens,
} from './theme-model';

const BASE = {
  darkScheme: true,
  tokens: [
    { name: '--jfb-color-text', default: '#1d2433', darkDefault: '#e8eaee' },
    { name: '--jfb-color-surface', default: '#fff', darkDefault: '#1d232d' },
    { name: '--jfb-color-border', default: '#767c88', darkDefault: '#8b95a5' },
    { name: '--jfb-color-rule', default: '#d9dde3', darkDefault: '#343c49' },
    { name: '--jfb-color-accent', default: '#0f6e5c', darkDefault: '#5fd0b8' },
    { name: '--jfb-color-on-accent', default: '#fff', darkDefault: '#0b1a17' },
    { name: '--jfb-color-button', default: '#0f6e5c', darkDefault: '#5fd0b8' },
    { name: '--jfb-color-on-button', default: '#fff', darkDefault: '#0b1a17' },
    { name: '--jfb-color-button-hover', default: '#125f54', darkDefault: '#7ad5c3' },
    { name: '--jfb-color-ink', default: '#102030', darkDefault: '#f0f0f0' },
    { name: '--jfb-radius', default: '12px' },
    { name: '--jfb-font-family', default: 'inherit' },
    { name: '--jfb-font-size', default: 'inherit' },
    { name: '--jfb-font-size-small', default: '.875em' },
    { name: '--jfb-font-size-large', default: '1.25em' },
    { name: '--jfb-font-size-title', default: '1.5em' },
    { name: '--jfb-focus-ring', default: '2px solid var(--jfb-color-focus, #1a5fd1)' },
    { name: '--jfb-gutter', default: '18px' },
  ],
};

const NONE: ThemeTokens = { light: {}, dark: {} };

describe('the contrast of colours', () => {
  it('reads #rgb and #rrggbb in any case, and nothing else', () => {
    expect(['#ABC', '#a1B2c3', ' #fff ', 'red', '#abcd', 'rgb(0 0 0)', undefined].map(hexOf)).toEqual([
      '#aabbcc',
      '#a1b2c3',
      '#ffffff',
      undefined,
      undefined,
      undefined,
      undefined,
    ]);
  });

  it("measures WCAG's ratios: 21 for black on white, 1 for a colour on itself, the same either way round", () => {
    expect(luminance('#ffffff')).toBe(1);
    expect(luminance('#000000')).toBe(0);
    expect(contrast('#000000', '#ffffff')).toBe(21);
    expect(contrast('#767c88', '#767c88')).toBe(1);
    expect(contrast('#0f6e5c', '#ffffff')).toBeCloseTo(6.167, 2);
    expect(contrast('#ffffff', '#0f6e5c')).toBe(contrast('#0f6e5c', '#ffffff'));
    expect(contrast('#767676', '#ffffff')).toBeCloseTo(4.54, 2);
  });

  it('says a ratio cut off at one decimal, so a ratio just under the line never reads as passing', () => {
    expect([ratioText(4.4999), ratioText(15.52), ratioText(3)]).toEqual(['4.4', '15.5', '3.0']);
  });

  it('offers the nearest colour of the same hue that passes, darker or lighter, and none where none does', () => {
    const lighter = suggestion('#c9ced6', '#ffffff', 3);
    expect(lighter).toBeDefined();
    expect(contrast(lighter ?? '', '#ffffff')).toBeGreaterThanOrEqual(3);
    expect(contrast(lighter ?? '', '#ffffff')).toBeLessThan(3.3);
    const darkGround = suggestion('#334455', '#1d232d', 4.5);
    expect(contrast(darkGround ?? '', '#1d232d')).toBeGreaterThanOrEqual(4.5);
    expect(luminance(darkGround ?? '')).toBeGreaterThan(luminance('#334455'));
    expect(suggestion('#808080', '#808080', 21)).toBeUndefined();
  });
});

describe("a colour's check", () => {
  const values =
    (tokens: ThemeTokens, scheme: 'light' | 'dark' = 'light') =>
    (name: string) =>
      valueIn(tokens, BASE, scheme, name);

  it('holds text to 4.5:1 and controls to 3:1 on the surface, the text on the accent to 4.5:1 on it', () => {
    expect(checkOf('--jfb-color-text', values(NONE))).toMatchObject({ state: 'pass', against: '--jfb-color-surface' });
    const pale = { light: { '--jfb-color-border': '#c9ced6' }, dark: {} };
    const failed = checkOf('--jfb-color-border', values(pale));
    expect(failed).toMatchObject({ state: 'fail', min: 3, against: '--jfb-color-surface' });
    expect(failed?.state === 'fail' && contrast(failed.suggestion ?? '', '#ffffff')).toBeGreaterThanOrEqual(3);
    const onAccent = { light: { '--jfb-color-on-accent': '#2a7a6b' }, dark: {} };
    expect(checkOf('--jfb-color-on-accent', values(onAccent))).toMatchObject({
      state: 'fail',
      min: 4.5,
      against: '--jfb-color-accent',
    });
  });

  it('checks the dark scheme against its own surface, calls dividers decoration, and leaves the surface to the others', () => {
    expect(checkOf('--jfb-color-text', values(NONE, 'dark'))).toMatchObject({ state: 'pass' });
    const dim = { light: {}, dark: { '--jfb-color-text': '#3a4250' } };
    expect(checkOf('--jfb-color-text', values(dim, 'dark'))).toMatchObject({ state: 'fail', min: 4.5 });
    expect(checkOf('--jfb-color-rule', values(NONE))).toEqual({ state: 'decoration' });
    expect(checkOf('--jfb-color-surface', values(NONE))).toBeUndefined();
    expect(checkOf('--jfb-color-ink', values(NONE))).toBeUndefined();
  });

  it("checks an outlined button's text on the surface, and says a value it cannot read", () => {
    const outlined = withChoice(NONE, BASE, { buttons: 'outlined' });
    expect(checkOf('--jfb-color-on-button', values(outlined))).toMatchObject({ state: 'pass' });
    const named = { light: { '--jfb-color-text': 'rebeccapurple' }, dark: {} };
    expect(checkOf('--jfb-color-text', values(named))).toEqual({ state: 'unchecked' });
  });
});

describe("a theme's tokens", () => {
  it("make an alias of a name, or of Umbraco's camel-case alias, as lowercase words joined by hyphens", () => {
    expect(themeAliasOf('Autumn campaign 2026')).toBe('autumn-campaign-2026');
    expect(themeAliasOf('autumnMw7Y1Kii49')).toBe('autumn-mw7y1kii49');
    expect(themeAliasOf('Herbst & Gr\u00fcn')).toBe('herbst-grun');
    expect(themeAliasOf('2026 brand')).toBe('theme-2026-brand');
    expect(themeAliasOf('x'.repeat(80))).toHaveLength(64);
    expect([isThemeAlias('autumn-2'), isThemeAlias('Autumn'), isThemeAlias('2-autumn'), isThemeAlias('a_b')]).toEqual([
      true,
      false,
      false,
      false,
    ]);
  });

  it("give a token's value in each scheme: the theme's own, else the base theme's default for the scheme", () => {
    const tokens = { light: { '--jfb-color-accent': '#8a1538', '--jfb-radius': '6px' }, dark: {} };
    expect(valueIn(tokens, BASE, 'light', '--jfb-color-accent')).toBe('#8a1538');
    expect(valueIn(tokens, BASE, 'dark', '--jfb-color-accent')).toBe('#5fd0b8');
    expect(valueIn(tokens, BASE, 'dark', '--jfb-radius')).toBe('6px');
    expect(valueIn(tokens, { ...BASE, darkScheme: false }, 'dark', '--jfb-color-accent')).toBe('#8a1538');
    expect(valueIn(tokens, BASE, 'light', '--jfb-nowhere')).toBeUndefined();
  });

  it("drop a value that is blank or the base theme's default for its scheme, and keep the rest", () => {
    const set = withToken(NONE, BASE, 'light', '--jfb-color-accent', ' #8a1538 ');
    expect(set.light).toEqual({ '--jfb-color-accent': '#8a1538' });
    expect(withToken(set, BASE, 'light', '--jfb-color-accent', '#0f6e5c').light).toEqual({});
    expect(withToken(set, BASE, 'light', '--jfb-color-accent', '').light).toEqual({});
    expect(withToken(NONE, BASE, 'dark', '--jfb-color-accent', '#5fd0b8').dark).toEqual({});
    expect(withToken(NONE, BASE, 'dark', '--jfb-color-accent', '#f0a5b8').dark).toEqual({
      '--jfb-color-accent': '#f0a5b8',
    });
  });

  it("read the sections' choices from the tokens, and write each one back", () => {
    expect(choicesOf(NONE, BASE)).toEqual({
      font: 'site',
      size: 'inherit',
      scale: 'default',
      spacing: undefined,
      radius: 12,
      ring: 2,
      buttons: 'filled',
    });
    let tokens = withChoice(NONE, BASE, { font: 'serif' });
    tokens = withChoice(tokens, BASE, { scale: 'generous' });
    tokens = withChoice(tokens, BASE, { radius: 6 });
    tokens = withChoice(tokens, BASE, { ring: 3 });
    tokens = withChoice(tokens, BASE, { buttons: 'outlined' });
    expect(choicesOf(tokens, BASE)).toMatchObject({
      font: 'serif',
      scale: 'generous',
      radius: 6,
      ring: 3,
      buttons: 'outlined',
    });
    expect(tokens.light['--jfb-font-size-small']).toBeUndefined();
    expect(tokens.light['--jfb-font-size-large']).toBe('1.375em');
    expect(withChoice(tokens, BASE, { buttons: 'filled' }).light['--jfb-color-button']).toBeUndefined();
    expect(withChoice(withChoice(NONE, BASE, { font: 'mono' }), BASE, { font: 'site' }).light).toEqual({});
    expect(choicesOf({ light: { '--jfb-font-family': 'Lato, sans-serif' }, dark: {} }, BASE).font).toBe('custom');
  });

  it("leave to Other tokens the base theme's tokens no section sets, a site's own colours among the colours", () => {
    const others = othersOf(BASE);
    expect(others.colours.map((token) => token.name)).toEqual(['--jfb-color-ink']);
    expect(others.tokens.map((token) => token.name)).toEqual(['--jfb-gutter']);
  });
});
