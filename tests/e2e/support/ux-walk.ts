import { join } from 'node:path';
import { e2eState } from './paths.ts';

// The UX walk of a phase review (ROADMAP 9.6a; 10.4, 11.6 and 12.6 walk their phases the same way): every screen of
// the phase at three widths, in Umbraco's light and dark themes, in English and German, each shot with what axe and the
// layout measures found, and one contact sheet per screen that shows its twelve shots side by side for the review.

/** One way to look at a screen: a window size, a theme and the backoffice's language. */
export interface Combo {
  readonly width: number;
  readonly height: number;
  readonly theme: Theme;
  readonly language: Language;
}

export type Theme = 'light' | 'dark';
export type Language = 'en' | 'de';

/** The windows of section 4.13's reviews: a phone, a tablet and a laptop. */
export const WINDOWS = [
  { width: 375, height: 812 },
  { width: 768, height: 1024 },
  { width: 1440, height: 900 },
] as const;

/** The backoffice languages, as a user's `languageIsoCode` names them. */
export const LANGUAGE_CODES: Readonly<Record<Language, string>> = { en: 'en-US', de: 'de-DE' };

const THEMES: readonly Theme[] = ['light', 'dark'];
const LANGUAGES: readonly Language[] = ['en', 'de'];

/** Every combination, widths first, so a sheet's rows are the widths and its columns the looks. */
export const COMBOS: readonly Combo[] = WINDOWS.flatMap((window) =>
  LANGUAGES.flatMap((language) => THEMES.map((theme) => ({ ...window, theme, language }))),
);

/** A combination's folder name, for example `375-dark-de`. */
export const comboName = (combo: Combo): string => `${combo.width}-${combo.theme}-${combo.language}`;

/**
 * Whether the walk shoots a screen: every screen, or with `JFB_UX_ONLY` only those whose names start with one of its
 * comma-separated prefixes, so a step looks at its own screens without the whole walk.
 */
export const wantedScreen = (screen: string, env: NodeJS.ProcessEnv = process.env): boolean => {
  const only =
    env['JFB_UX_ONLY']
      ?.split(',')
      .map((prefix) => prefix.trim())
      .filter(Boolean) ?? [];
  return only.length === 0 || only.some((prefix) => screen.startsWith(prefix));
};

/**
 * Whether the walk looks at a screen this way: every way, or with `JFB_UX_LOOKS` only those its comma-separated looks
 * name, each look the parts of a combination's name it must have (`de`, `light-de`, `1440-dark-de`), so a step
 * that changed the German screens walks them in German only.
 */
export const wantedCombo = (combo: Combo, env: NodeJS.ProcessEnv = process.env): boolean => {
  const looks =
    env['JFB_UX_LOOKS']
      ?.split(',')
      .map((look) => look.trim())
      .filter(Boolean) ?? [];
  const parts = comboName(combo).split('-');
  return looks.length === 0 || looks.some((look) => look.split('-').every((part) => parts.includes(part)));
};

/** Where a walk writes: `JFB_UX_OUT`, or `.local/ux/latest`. */
export const walkDir = (env: NodeJS.ProcessEnv = process.env): string =>
  env['JFB_UX_OUT'] ?? join(e2eState, '..', 'ux', 'latest');

/** What a shot found. */
export interface ShotReport {
  readonly screen: string;
  /** The PNG beside the report, or null when the walk could not reach the screen. */
  readonly file: string | null;
  /** Why the walk could not reach the screen. */
  readonly error?: string;
  /** Axe's findings inside Jiron Form Builder's own elements, as `rule: target`. */
  readonly axe: readonly string[];
  /** How many of axe's findings lie in Umbraco's own elements, which are Umbraco's to answer for. */
  readonly axeUmbraco: number;
  /** How far the page itself scrolls sideways, in pixels. */
  readonly overflowX: number;
  /** Boxes that scroll sideways inside the page, as `tag (scroll width > width)`. */
  readonly scrollers: readonly string[];
  /** Texts cut short with an ellipsis. */
  readonly clipped: readonly string[];
  /**
   * How many words the section's own elements show, below the fold included: what a person has to read. The forms
   * tree's rows are left out, as they are a site's own names (ROADMAP 21.1).
   */
  readonly words: number;
}

/** The words two walks of the same build can differ by on a screen: a time of day, a count, a date's month. */
export const WORDS_NOISE = 3;

/** A screen's words in one look of two walks. */
export interface WordChange {
  readonly screen: string;
  readonly look: string;
  readonly before: number;
  readonly after: number;
}

/** What each shot of a walk found, screen by screen and look by look: a walk's summary.json. */
export type WalkSummary = Readonly<
  Record<string, Readonly<Record<string, { readonly words?: number; readonly error?: string }>>>
>;

/**
 * The words of every screen both walks reached, in the looks both have, the screens that grew most first, then by
 * name: a pass that calms the screens shows no growth here (ROADMAP 21.1). A shot without a count (an earlier walk's,
 * or a screen not reached) is left out.
 */
export function wordChanges(before: WalkSummary, after: WalkSummary): WordChange[] {
  const changes: WordChange[] = [];
  for (const [screen, looks] of Object.entries(after)) {
    for (const [look, found] of Object.entries(looks)) {
      const earlier = before[screen]?.[look];
      if (earlier?.words === undefined || found.words === undefined || earlier.error || found.error) continue;
      changes.push({ screen, look, before: earlier.words, after: found.words });
    }
  }
  return changes.sort(
    (a, b) =>
      b.after - b.before - (a.after - a.before) || a.screen.localeCompare(b.screen) || a.look.localeCompare(b.look),
  );
}

/** An axe finding with the ancestry axe gives when asked: the selectors from the document down to the node. */
export interface AxeNode {
  readonly rule: string;
  readonly target: readonly unknown[];
  readonly ancestry: readonly unknown[];
}

/**
 * Axe's findings split by whose they are: a node inside one of Jiron Form Builder's elements (a `jfb-` tag on its way
 * from the document) is ours, any other Umbraco's.
 */
export function splitAxe(nodes: readonly AxeNode[]): { ours: string[]; umbraco: number } {
  const ours: string[] = [];
  let umbraco = 0;
  for (const node of nodes) {
    if (/(^|[^a-z0-9-])jfb-[a-z]/.test(JSON.stringify(node.ancestry)))
      ours.push(`${node.rule}: ${JSON.stringify(node.target)}`);
    else umbraco++;
  }
  return { ours, umbraco };
}

/** A cell of a contact sheet: a combination and its shot as a data URL, or null when there is none. */
export interface SheetCell {
  readonly combo: Combo;
  readonly image: string | null;
  readonly note?: string;
}

const escape = (text: string): string =>
  text.replace(/[&<>"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[char] ?? char);

/**
 * A contact sheet's page: the screen's name, then one row per width and one column per look (theme and language), each
 * cell its shot scaled to fit, or a line saying why there is none.
 */
export function sheetHtml(screen: string, cells: readonly SheetCell[]): string {
  const looks = COMBOS.filter((combo) => combo.width === WINDOWS[0].width);
  const cellOf = (window: (typeof WINDOWS)[number], look: Combo) =>
    cells.find(
      (cell) =>
        cell.combo.width === window.width && cell.combo.theme === look.theme && cell.combo.language === look.language,
    );
  const rows = WINDOWS.map((window) =>
    looks
      .map((look) => {
        const cell = cellOf(window, look);
        const name = comboName({ ...window, theme: look.theme, language: look.language });
        const body = cell?.image
          ? `<img src="${cell.image}" alt="">`
          : `<p class="none">${escape(cell?.note ?? 'no shot')}</p>`;
        return `<figure>${body}<figcaption>${escape(name)}</figcaption></figure>`;
      })
      .join(''),
  ).join('');
  return `<!doctype html><html><head><meta charset="utf-8"><style>
body{margin:0;padding:16px;font:14px system-ui,sans-serif;background:#f3f4f6;color:#1f2933}
h1{font-size:18px;margin:0 0 12px}
main{display:grid;grid-template-columns:repeat(${looks.length},480px);gap:12px}
figure{margin:0;background:#fff;border:1px solid #c9ced6;height:440px;display:grid;grid-template-rows:1fr auto}
img{width:100%;height:100%;object-fit:contain;min-height:0}
figcaption{padding:4px 8px;border-top:1px solid #c9ced6}
.none{margin:auto;color:#8a2e12}
</style></head><body><h1>${escape(screen)}</h1><main>${rows}</main></body></html>`;
}
