// A library theme as its editor holds it (ROADMAP 12.1, IA 7, ADR-0215): what it changes of its base theme's tokens in
// each scheme, the colours' contrast checks with the nearest colour of the same hue that passes, and the choices of the
// sections that set several tokens at once (type, spacing and corners, buttons and the focus ring). Pure, so it is tested
// without the DOM.
import type { ThemeBaseResponseModel, ThemeTokenResponseModel } from '../api';
import type { LibraryItemModel } from '../library/items/library-items';

export type Scheme = 'light' | 'dark';

/** What a theme changes of its base theme: the tokens of both schemes, and the colours of the dark one. */
export interface ThemeTokens {
  readonly light: Readonly<Record<string, string>>;
  readonly dark: Readonly<Record<string, string>>;
}

export interface ThemeDetailModel extends LibraryItemModel {
  baseTheme: string;
  tokens: ThemeTokens;
  customCss: string;
}

/** A theme's alias, which forms name it by: a lowercase letter, then lowercase letters, digits and hyphens, up to 64. */
export const isThemeAlias = (alias: string): boolean => /^[a-z][a-z0-9-]{0,63}$/.test(alias);

/** An alias made of a name or of a camel-case alias: lowercase words joined by hyphens. */
export function themeAliasOf(name: string): string {
  const words = name
    .normalize('NFKD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  const alias = /^[a-z]/.test(words) ? words : `theme-${words}`.replace(/-+$/, '');
  return alias.slice(0, 64).replace(/-+$/, '');
}

/** Whether a token is a colour, which alone takes a value of its own in the dark scheme. */
export const isColour = (name: string): boolean => name.startsWith('--jfb-color-');

/** The colours of the default theme in the editor's groups and order (docs/ux/tokens.md). */
export const COLOUR_GROUPS: readonly { readonly id: 'lines' | 'signals'; readonly tokens: readonly string[] }[] = [
  {
    id: 'lines',
    tokens: [
      '--jfb-color-text',
      '--jfb-color-text-muted',
      '--jfb-color-surface',
      '--jfb-color-border',
      '--jfb-color-rule',
    ],
  },
  { id: 'signals', tokens: ['--jfb-color-accent', '--jfb-color-on-accent', '--jfb-color-focus', '--jfb-color-error'] },
];

/** The primary button's own colours. */
export const BUTTON_COLOURS: readonly string[] = [
  '--jfb-color-button',
  '--jfb-color-on-button',
  '--jfb-color-button-hover',
];

const SURFACE = '--jfb-color-surface';

/** What a colour is checked against and the ratio it needs (WCAG 1.4.3 and 1.4.11); dividers are decoration only. */
const CHECKS: Readonly<Record<string, { readonly against: string; readonly min: number } | 'decoration'>> = {
  '--jfb-color-text': { against: SURFACE, min: 4.5 },
  '--jfb-color-text-muted': { against: SURFACE, min: 4.5 },
  '--jfb-color-border': { against: SURFACE, min: 3 },
  '--jfb-color-rule': 'decoration',
  '--jfb-color-accent': { against: SURFACE, min: 3 },
  '--jfb-color-on-accent': { against: '--jfb-color-accent', min: 4.5 },
  '--jfb-color-focus': { against: SURFACE, min: 3 },
  '--jfb-color-error': { against: SURFACE, min: 4.5 },
  '--jfb-color-button': { against: SURFACE, min: 3 },
  '--jfb-color-on-button': { against: '--jfb-color-button', min: 4.5 },
  '--jfb-color-button-hover': { against: '--jfb-color-on-button', min: 4.5 },
};

/** A colour of the form `#rgb` or `#rrggbb` as `#rrggbb` in lowercase; undefined for any other value. */
export function hexOf(value: string | undefined): string | undefined {
  const match = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(value?.trim() ?? '');
  if (!match?.[1]) return undefined;
  const digits = match[1].length === 3 ? [...match[1]].map((digit) => digit + digit).join('') : match[1];
  return `#${digits.toLowerCase()}`;
}

const channels = (hex: string): [number, number, number] =>
  [1, 3, 5].map((at) => parseInt(hex.slice(at, at + 2), 16)) as [number, number, number];

/** A colour's relative luminance (WCAG 2.2). */
export function luminance(hex: string): number {
  const [r, g, b] = channels(hex).map((channel) => {
    const c = channel / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  }) as [number, number, number];
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

/** The contrast ratio of two colours, 1 to 21. */
export function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number];
  return (light + 0.05) / (dark + 0.05);
}

/** A ratio as the editor says it: one decimal, cut off, so a ratio just under the line never reads as passing. */
export const ratioText = (ratio: number): string => (Math.floor(ratio * 10) / 10).toFixed(1);

function hslOf(hex: string): [number, number, number] {
  const [r, g, b] = channels(hex).map((channel) => channel / 255) as [number, number, number];
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  const h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [h / 6, s, l];
}

function hexOfHsl(h: number, s: number, l: number): string {
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
  const p = 2 * l - q;
  const part = (t: number) => {
    const u = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
    const v = u < 1 / 6 ? p + (q - p) * 6 * u : u < 1 / 2 ? q : u < 2 / 3 ? p + (q - p) * (2 / 3 - u) * 6 : p;
    return Math.round(v * 255)
      .toString(16)
      .padStart(2, '0');
  };
  return s === 0 ? `#${part(l).repeat(3)}` : `#${part(h + 1 / 3)}${part(h)}${part(h - 1 / 3)}`;
}

/** The colour of the same hue and saturation nearest in lightness that passes against the other; undefined if none does. */
export function suggestion(hex: string, against: string, min: number): string | undefined {
  const [h, s, l] = hslOf(hex);
  for (let step = 1; step <= 100; step++) {
    for (const next of [l - step / 100, l + step / 100]) {
      if (next < 0 || next > 1) continue;
      const tried = hexOfHsl(h, s, next);
      if (contrast(tried, against) >= min) return tried;
    }
  }
  return undefined;
}

/** A colour's check, as the editor says it under the colour. */
export type ColourCheck =
  | { readonly state: 'decoration' }
  | { readonly state: 'unchecked' }
  | { readonly state: 'pass'; readonly ratio: number; readonly against: string }
  | {
      readonly state: 'fail';
      readonly ratio: number;
      readonly against: string;
      readonly min: number;
      readonly suggestion: string | undefined;
    };

/** The check of a colour token; undefined for one that is checked by others only, as the surface. */
export function checkOf(name: string, valueOf: (name: string) => string | undefined): ColourCheck | undefined {
  const check = CHECKS[name];
  if (check === undefined) return undefined;
  if (check === 'decoration') return { state: 'decoration' };
  const colour = hexOf(valueOf(name));
  // A button with no fill of its own shows the surface.
  const ground =
    hexOf(valueOf(check.against)) ?? (check.against === '--jfb-color-button' ? hexOf(valueOf(SURFACE)) : undefined);
  if (!colour || !ground) return { state: 'unchecked' };
  const ratio = contrast(colour, ground);
  return ratio >= check.min
    ? { state: 'pass', ratio, against: check.against }
    : {
        state: 'fail',
        ratio,
        against: check.against,
        min: check.min,
        suggestion: suggestion(colour, ground, check.min),
      };
}

/** A token's value in a scheme as the website draws it: the theme's own, else the base theme's default for the scheme. */
export function valueIn(
  tokens: ThemeTokens,
  base: Pick<ThemeBaseResponseModel, 'darkScheme' | 'tokens'>,
  scheme: Scheme,
  name: string,
): string | undefined {
  const declared = base.tokens.find((token) => token.name === name);
  if (!declared) return undefined;
  const value =
    scheme === 'dark' && base.darkScheme && isColour(name)
      ? (tokens.dark[name] ?? declared.darkDefault ?? tokens.light[name] ?? declared.default)
      : (tokens.light[name] ?? declared.default);
  // A reference to another token reads as that token's value, as an outlined button's text is the accent.
  const reference = /^var\((--[a-z0-9-]+)\)$/.exec(value.trim());
  return reference?.[1] && reference[1] !== name ? valueIn(tokens, base, scheme, reference[1]) : value;
}

/** The tokens with one changed: set, or taken out when it is the base theme's default for the scheme. */
export function withToken(
  tokens: ThemeTokens,
  base: Pick<ThemeBaseResponseModel, 'tokens'>,
  scheme: Scheme,
  name: string,
  value: string | undefined,
): ThemeTokens {
  const declared = base.tokens.find((token) => token.name === name);
  const own: Record<string, string> = Object.fromEntries(
    Object.entries(tokens[scheme]).filter(([key]) => key !== name),
  );
  const fallback = scheme === 'dark' ? declared?.darkDefault : declared?.default;
  if (value !== undefined && value.trim() !== '' && value.trim() !== fallback) own[name] = value.trim();
  return { ...tokens, [scheme]: own };
}

/** The type choices: the font's stacks, the sizes, and the scales of the three steps above and below the body. */
export const FONTS: Readonly<Record<'site' | 'sans' | 'serif' | 'mono', string>> = {
  site: 'inherit',
  sans: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif',
  serif: 'Georgia, Cambria, "Times New Roman", serif',
  mono: 'ui-monospace, "Cascadia Mono", Menlo, Consolas, monospace',
};

export const SIZES: readonly string[] = ['inherit', '14px', '15px', '16px', '17px', '18px', '20px', '22px', '24px'];

const STEPS = ['--jfb-font-size-small', '--jfb-font-size-large', '--jfb-font-size-title'] as const;
export const SCALES: Readonly<Record<'compact' | 'default' | 'generous', readonly string[]>> = {
  compact: ['.875em', '1.125em', '1.25em'],
  default: ['.875em', '1.25em', '1.5em'],
  generous: ['.875em', '1.375em', '1.75em'],
};

/** The spacing choices: the seven spaces on the 4 px grid at three quarters, the default and a quarter more. */
const SPACES = [
  '--jfb-space-1',
  '--jfb-space-2',
  '--jfb-space-3',
  '--jfb-space-4',
  '--jfb-space-5',
  '--jfb-space-6',
  '--jfb-space-7',
] as const;
export const SPACINGS: Readonly<Record<'compact' | 'default' | 'roomy', readonly string[]>> = {
  compact: ['4px', '8px', '8px', '12px', '16px', '24px', '36px'],
  default: ['4px', '8px', '12px', '16px', '24px', '32px', '48px'],
  roomy: ['4px', '8px', '16px', '20px', '32px', '40px', '60px'],
};

export const RADII: readonly number[] = [0, 2, 4, 6, 8, 12, 16, 20, 24];
export const RINGS: readonly number[] = [2, 3, 4];

const RADIUS = '--jfb-radius';
const RING = '--jfb-focus-ring';
const FONT = '--jfb-font-family';
const SIZE = '--jfb-font-size';

/** An outlined primary button: no fill, the accent's text, a tint of it on hover. */
const OUTLINED: Readonly<Record<string, string>> = {
  '--jfb-color-button': 'transparent',
  '--jfb-color-on-button': 'var(--jfb-color-accent)',
  '--jfb-color-button-hover': 'color-mix(in srgb, var(--jfb-color-accent) 12%, transparent)',
};

/** The tokens the sections' choices set, which "Other tokens" leaves out. */
export const CHOSEN: ReadonlySet<string> = new Set([FONT, SIZE, ...STEPS, ...SPACES, RADIUS, RING]);

/** What a section's choice reads as now; `custom` for values that are none of its choices. */
export interface Choices {
  readonly font: keyof typeof FONTS | 'custom' | undefined;
  readonly size: string | undefined;
  readonly scale: keyof typeof SCALES | 'custom' | undefined;
  readonly spacing: keyof typeof SPACINGS | 'custom' | undefined;
  readonly radius: number | undefined;
  readonly ring: number | undefined;
  readonly buttons: 'filled' | 'outlined' | undefined;
}

const choiceOf = <K extends string>(
  choices: Readonly<Record<K, readonly string[]>>,
  values: readonly (string | undefined)[],
) =>
  (Object.keys(choices) as K[]).find((key) =>
    choices[key].every((value, at) => values[at]?.replace(/^0\./, '.') === value),
  ) ?? ('custom' as const);

/** The sections' choices as the theme's tokens give them; undefined where the base theme lacks the tokens. */
export function choicesOf(tokens: ThemeTokens, base: Pick<ThemeBaseResponseModel, 'darkScheme' | 'tokens'>): Choices {
  const has = (name: string) => base.tokens.some((token) => token.name === name);
  const value = (name: string) => valueIn(tokens, base, 'light', name);
  const font = value(FONT);
  const ring = /^(\d+)px\b/.exec(value(RING) ?? '')?.[1];
  const radius = /^(\d+)px$/.exec(value(RADIUS) ?? '')?.[1];
  return {
    font: has(FONT)
      ? ((Object.keys(FONTS) as (keyof typeof FONTS)[]).find((key) => FONTS[key] === font) ?? 'custom')
      : undefined,
    size: has(SIZE) ? value(SIZE) : undefined,
    scale: STEPS.every(has) ? choiceOf(SCALES, STEPS.map(value)) : undefined,
    spacing: SPACES.every(has) ? choiceOf(SPACINGS, SPACES.map(value)) : undefined,
    radius: has(RADIUS) && radius !== undefined ? Number(radius) : undefined,
    ring: has(RING) && ring !== undefined ? Number(ring) : undefined,
    buttons: BUTTON_COLOURS.every(has)
      ? tokens.light['--jfb-color-button'] === 'transparent'
        ? 'outlined'
        : 'filled'
      : undefined,
  };
}

/** The tokens with a section's choice made. */
export function withChoice(
  tokens: ThemeTokens,
  base: Pick<ThemeBaseResponseModel, 'tokens'>,
  choice:
    | { readonly font: keyof typeof FONTS }
    | { readonly size: string }
    | { readonly scale: keyof typeof SCALES }
    | { readonly spacing: keyof typeof SPACINGS }
    | { readonly radius: number }
    | { readonly ring: number }
    | { readonly buttons: 'filled' | 'outlined' },
): ThemeTokens {
  const set = (names: readonly string[], values: readonly (string | undefined)[]) =>
    names.reduce((next, name, at) => withToken(next, base, 'light', name, values[at]), tokens);
  if ('font' in choice) return set([FONT], [FONTS[choice.font]]);
  if ('size' in choice) return set([SIZE], [choice.size]);
  if ('scale' in choice) return set(STEPS, SCALES[choice.scale]);
  if ('spacing' in choice) return set(SPACES, SPACINGS[choice.spacing]);
  if ('radius' in choice) return set([RADIUS], [`${choice.radius}px`]);
  if ('ring' in choice) return set([RING], [`${choice.ring}px solid var(--jfb-color-focus, #1a5fd1)`]);
  const outlined = choice.buttons === 'outlined';
  const next = set(
    BUTTON_COLOURS,
    BUTTON_COLOURS.map((name) => (outlined ? OUTLINED[name] : undefined)),
  );
  // Outlined has no fill in either scheme, so the dark one takes none of its own either.
  return BUTTON_COLOURS.reduce((all, name) => withToken(all, base, 'dark', name, undefined), next);
}

/** The base theme's tokens that no section sets: its colours beyond the editor's groups, and every other token. */
export function othersOf(base: Pick<ThemeBaseResponseModel, 'tokens'>): {
  readonly colours: readonly ThemeTokenResponseModel[];
  readonly tokens: readonly ThemeTokenResponseModel[];
} {
  const grouped = new Set([...COLOUR_GROUPS.flatMap((group) => group.tokens), ...BUTTON_COLOURS]);
  return {
    colours: base.tokens.filter((token) => isColour(token.name) && !grouped.has(token.name)),
    tokens: base.tokens.filter((token) => !isColour(token.name) && !CHOSEN.has(token.name)),
  };
}

/** How many of the given tokens the theme changes, in either scheme. */
export const changedOf = (tokens: ThemeTokens, names: readonly string[]): number =>
  names.filter((name) => name in tokens.light || name in tokens.dark).length;
