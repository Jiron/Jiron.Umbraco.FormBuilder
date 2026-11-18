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
  /** The folds inside a region another fold opened, as `foldsInFolds` names them; the walk fails a screen with one. */
  readonly folds: readonly string[];
  /** How far the page itself scrolls sideways, in pixels. */
  readonly overflowX: number;
  /** Boxes that scroll sideways inside the page, as `tag (scroll width > width)`. */
  readonly scrollers: readonly string[];
  /** Texts cut short with an ellipsis. */
  readonly clipped: readonly string[];
  /** The raw keys of the words drawn on the screen, as `rawKeys` finds them; the walk fails a screen with one. */
  readonly keys: readonly string[];
  /**
   * How many words the section's own elements show, below the fold included: what a person has to read. The forms
   * tree's rows are left out, as they are a site's own names (ROADMAP 21.1), and so is the section's navigation
   * (`NAVIGATION`), which `navigation` counts: until step 22.2 the left side was Umbraco's menu, which no walk
   * counted, so a screen's words compare with every walk made before (ADR-0259).
   */
  readonly words: number;
  /** How many words the section's navigation shows: the places on the left and the open place's tabs (ADR-0259). */
  readonly navigation: number;
}

/** The section's navigation, whose words a shot counts by itself (`ShotReport.navigation`): places and tabs. */
export const NAVIGATION: readonly string[] = ['jfb-places', 'jfb-place-tabs'];

/**
 * The raw keys of the backoffice's words in a screen's drawn text: a word built as a key is (`jfb`, a capital, then
 * `_name` parts, as `jfbGlobalSettings_privacy_retention_rejectedHelp`), which a screen draws when a word it asks for
 * does not exist. Each key once, in the order found.
 */
export function rawKeys(text: string): string[] {
  return [...new Set(text.match(/\bjfb[A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+)+\b/g) ?? [])];
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
  Record<
    string,
    Readonly<Record<string, { readonly words?: number; readonly navigation?: number; readonly error?: string }>>
  >
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
 * from the document) is ours, any other Umbraco's. Two findings are Umbraco's even inside ours, and no other there:
 * the sidebar's places (ROADMAP 22.2) draw Umbraco's own menu item, unstyled, and axe questions in it what it questions
 * in Umbraco's tree beside them, its `#menu-item` named "menuitem" whatever it shows, and its label's colours, which
 * the text given to its label slot takes too (the Inbox's name beside its badge). Any other rule on a menu item of
 * ours, a missing name for one, stays ours.
 */
export function splitAxe(nodes: readonly AxeNode[]): { ours: string[]; umbraco: number } {
  const ours: string[] = [];
  let umbraco = 0;
  for (const node of nodes) {
    const path = JSON.stringify(node.ancestry);
    const last = node.target.at(-1);
    const host = Array.isArray(last) ? String(last.at(-2) ?? '') : '';
    const inner = Array.isArray(last) ? String(last.at(-1) ?? '') : String(last ?? '');
    const inItem = host.startsWith('uui-menu-item');
    const itemsOwn =
      (node.rule === 'label-content-name-mismatch' && inItem && inner === '#menu-item') ||
      (node.rule === 'color-contrast' &&
        (inItem || (inner.endsWith('[slot="label"]') && path.includes('uui-menu-item'))));
    if (!itemsOwn && /(^|[^a-z0-9-])jfb-[a-z]/.test(path)) ours.push(`${node.rule}: ${JSON.stringify(node.target)}`);
    else umbraco++;
  }
  return { ours, umbraco };
}

/**
 * An element of a screen as the fold check reads it (support/ux-shots.ts): only what is drawn, shadow roots inside
 * their hosts, and only what the check needs. Ids carry their shadow root's number (`3:more`), as an `aria-controls`
 * names an element of its own root.
 */
export interface FoldNode {
  readonly tag: string;
  readonly id?: string;
  readonly role?: string;
  readonly name?: string;
  /** `aria-expanded`, where the element has it. */
  readonly expanded?: string;
  /** The ids its `aria-controls` names. */
  readonly controls?: readonly string[];
  /** `aria-haspopup`, where the element has it. */
  readonly haspopup?: string;
  /** Whether it opens a popover (`popovertarget`), or is one (`popover`). */
  readonly popovertarget?: boolean;
  readonly popover?: boolean;
  /** A `details` element's `open`. */
  readonly open?: boolean;
  readonly children: readonly FoldNode[];
}

/** The roles of what a control opens over the page rather than in it, which is a menu or a list, not a fold. */
const POPUP_ROLES = new Set(['menu', 'listbox', 'dialog', 'tree', 'grid']);

/**
 * The elements whose own opening and closing is no section (ADR-0259). Tools that open under the box they write into:
 * Insert merge tag, the AI add-on's Suggest help text and the rich text's Link. And the option editor: a row opens its
 * own option (its value and its description), its handle the option's place in the list, and Paste options and Paste
 * rows a box to paste into. They put a value in a box or show one item of a list, not a group of the element's
 * settings, so they are no folds.
 */
export const FOLD_TOOLS: readonly string[] = [
  'jfb-merge-tag-picker',
  'jfb-ai-suggest',
  'jfb-rich-text',
  'jfb-option-editor',
];

/**
 * The folds that stand inside a region another fold opened, as `"inner" inside "outer"`: a screen that opens a section
 * inside a section (ROADMAP 22.3, the fifth rule of ADR-0254). A fold is a control that opens and closes a region in
 * the page: an element with `aria-expanded`, or a `details` (named by its `summary`). What opens over the page is no
 * fold: a control with `aria-haspopup`, a combobox, one that opens a popover, or one whose region is a menu, a list
 * box, a dialog, a tree or a grid. A tree's own items are no folds either (a `treeitem`, anything in a `tree`, and
 * UUI's menu item, which draws Umbraco's trees), nor what a tool of `FOLD_TOOLS` holds. A fold's region is what its
 * `aria-controls` names while it is expanded, or a `details` itself while it is open.
 */
export function foldsInFolds(root: FoldNode): string[] {
  const NOT_FOLDS = new Set(['uui-menu-item', ...FOLD_TOOLS]);
  const byId = new Map<string, FoldNode>();
  const index = (node: FoldNode) => {
    if (node.id) byId.set(node.id, node);
    node.children.forEach(index);
  };
  index(root);
  const nameOf = (node: FoldNode) =>
    node.tag === 'details'
      ? (node.children.find((child) => child.tag === 'summary')?.name ?? node.name ?? 'details')
      : (node.name ?? node.id ?? node.tag);
  const isFold = (node: FoldNode) => {
    if (node.tag === 'details') return true;
    if (node.expanded === undefined) return false;
    if (node.haspopup !== undefined && node.haspopup !== 'false') return false;
    if (node.role === 'combobox' || node.popovertarget) return false;
    return !(node.controls ?? []).some((id) => {
      const region = byId.get(id);
      return region?.popover === true || POPUP_ROLES.has(region?.role ?? '');
    });
  };
  // Each open region, by its fold's name.
  const regions = new Map<FoldNode, string>();
  const findRegions = (node: FoldNode, inTree: boolean) => {
    const tree = inTree || node.role === 'tree' || node.role === 'treeitem' || NOT_FOLDS.has(node.tag);
    if (!tree && isFold(node)) {
      if (node.tag === 'details' && node.open) regions.set(node, nameOf(node));
      else if (node.expanded === 'true')
        for (const id of node.controls ?? []) {
          const region = byId.get(id);
          if (region) regions.set(region, nameOf(node));
        }
    }
    for (const child of node.children) findRegions(child, tree);
  };
  findRegions(root, false);
  const found: string[] = [];
  const walk = (node: FoldNode, within: readonly { region: FoldNode; fold: string }[], inTree: boolean) => {
    const tree = inTree || node.role === 'tree' || node.role === 'treeitem' || NOT_FOLDS.has(node.tag);
    if (!tree && isFold(node)) {
      const outer = within.find((one) => one.region !== node);
      if (outer) found.push(`"${nameOf(node)}" inside "${outer.fold}"`);
    }
    const fold = regions.get(node);
    const inside = fold === undefined ? within : [...within, { region: node, fold }];
    for (const child of node.children) walk(child, inside, tree);
  };
  walk(root, [], false);
  return found;
}

/** A cell of a contact sheet: a combination and its shot as a data URL, or null when there is none. */
export interface SheetCell {
  readonly combo: Combo;
  readonly image: string | null;
  readonly note?: string;
  /** The shot's own words and its navigation's, which its caption gives. */
  readonly words?: number;
  readonly navigation?: number;
}

const escape = (text: string): string =>
  text.replace(/[&<>"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[char] ?? char);

/**
 * A contact sheet's page: the screen's name, then one row per width and one column per look (theme and language), each
 * cell its shot scaled to fit, captioned with its look and its words and its navigation's, or a line saying why there
 * is none.
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
        const counts =
          cell?.image && cell.words !== undefined
            ? `: ${cell.words} words, ${cell.navigation ?? 0} in the navigation`
            : '';
        return `<figure>${body}<figcaption>${escape(name + counts)}</figcaption></figure>`;
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
