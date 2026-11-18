import AxeBuilder from '@axe-core/playwright';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import type { Page } from '@playwright/test';
import { inLanguage, inTheme } from './backoffice-look.ts';
import {
  FOLD_TOOLS,
  foldsInFolds,
  LANGUAGE_CODES,
  NAVIGATION,
  rawKeys,
  splitAxe,
  wantedScreen,
  type AxeNode,
  type Combo,
  type FoldNode,
  type ShotReport,
} from './ux-walk.ts';

// The shots of a UX walk (ROADMAP 9.6a; the journeys of 16.6 take them the same way): a page dressed in a combination of
// support/ux-walk.ts, each screen reached, settled and shot, with what axe found inside our own elements, how far the
// page scrolls sideways, what scrolls inside it and what an ellipsis cuts short, written beside the shots.

/** Dresses a page for a combination: its window, Umbraco's theme, and the backoffice's language, given by answering the
 * current user's language as the combination's. What the server writes in the user's language follows the account's
 * own, so a German walk signs in as a twin that keeps German (ROADMAP 16.6). */
export async function dress(page: Page, combo: Combo): Promise<void> {
  // A step that cannot find its way gives up soon, so the walk notes it and goes on.
  page.setDefaultTimeout(15_000);
  await page.setViewportSize({ width: combo.width, height: combo.height });
  await inTheme(page, combo.theme);
  await inLanguage(page, LANGUAGE_CODES[combo.language]);
}

/** Waits for the motion to end, with the pointer out of the way. */
export async function settle(page: Page): Promise<void> {
  await page.mouse.move(1, 1);
  // A loader spins for ever, so the wait has a limit of its own.
  await page
    .waitForFunction(
      () => document.getAnimations().every((animation) => animation.playState !== 'running'),
      undefined,
      {
        timeout: 3000,
      },
    )
    .catch(() => undefined);
  // In a window narrower than Umbraco's page the shot shows the workspace from its start, where a phone's reader pans to
  // read it (ADR-0241); a focused field may have scrolled the page elsewhere. A page without Umbraco's main area stays.
  await page.evaluate(() => {
    if (document.documentElement.scrollWidth <= innerWidth) return;
    const find = (root: Document | ShadowRoot): Element | null => {
      for (const element of root.querySelectorAll('*')) {
        if (element.localName === 'umb-section-main') return element;
        const inner = element.shadowRoot ? find(element.shadowRoot) : null;
        if (inner) return inner;
      }
      return null;
    };
    const main = find(document);
    if (main) window.scrollTo({ left: scrollX + main.getBoundingClientRect().left, top: scrollY, behavior: 'instant' });
  });
  await page.waitForTimeout(250);
}

/** Axe's WCAG 2.2 A and AA findings on the page, each with its ancestry. */
export async function axeNodes(page: Page): Promise<AxeNode[]> {
  // The options first: AxeBuilder's options() replaces what withTags() set, which would run every rule axe has. Frames
  // are left alone, as in axeInside: an email's preview is a sandboxed frame that runs no script (ADR-0189).
  const result = await new AxeBuilder({ page })
    .options({ ancestry: true, iframes: false })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
    .setLegacyMode(true)
    .analyze();
  return result.violations.flatMap((violation) =>
    violation.nodes.map((node) => ({
      rule: violation.id,
      target: node.target,
      ancestry: (node as { ancestry?: unknown[] }).ancestry ?? [],
    })),
  );
}

/**
 * How the page lies at this width: its sideways scroll, the boxes that scroll inside it, the texts cut short, how many
 * words the section's own elements show, how many its navigation shows, and the text drawn on the screen, which
 * `Walk.shot` reads for raw keys and does not keep. The forms tree's names end with Umbraco's ellipsis as its own
 * trees' do (ADR-0221), so its rows are left out.
 */
export const measure = (page: Page) =>
  page.evaluate((navigation) => {
    const all: HTMLElement[] = [];
    const walk = (root: Document | ShadowRoot) => {
      for (const element of root.querySelectorAll('*')) {
        if (element.localName === 'jfb-library-tree-item') continue;
        if (element instanceof HTMLElement) all.push(element);
        if (element.shadowRoot) walk(element.shadowRoot);
      }
    };
    walk(document);
    const seen = (element: HTMLElement) => {
      const box = element.getBoundingClientRect();
      return box.width > 0 && box.height > 0 && box.bottom > 0 && box.right > 0 && box.top < innerHeight;
    };
    const scrollers: string[] = [];
    const clipped: string[] = [];
    for (const element of all) {
      if (element.scrollWidth <= element.clientWidth + 1 || !seen(element)) continue;
      const style = getComputedStyle(element);
      const text = (element.textContent ?? '').trim().replace(/\s+/g, ' ');
      if ((style.overflowX === 'auto' || style.overflowX === 'scroll') && element.clientWidth > 0) {
        const name = element.id
          ? `#${element.id}`
          : typeof element.className === 'string' && element.className.trim()
            ? `.${element.className.trim().split(/\s+/).join('.')}`
            : '';
        scrollers.push(`${element.tagName.toLowerCase()}${name} (${element.scrollWidth} > ${element.clientWidth})`);
      } else if (style.textOverflow === 'ellipsis' && text) clipped.push(text.slice(0, 60));
    }
    // The words a person has to read: the text inside the section's own elements that is drawn, wherever the page is
    // scrolled to. A subtree that is not drawn (display: none, hidden, a closed group) and a text heard but not seen
    // (a box of a pixel) are left out, and so are the forms tree's rows. The section's navigation (NAVIGATION: the
    // places and a place's tabs) is counted by itself, so a screen's words compare with the walks made before it was
    // the section's own (ADR-0259). Every drawn text, Umbraco's too, is kept for the raw-key check.
    const drawn = (element: HTMLElement): boolean => {
      const style = getComputedStyle(element);
      if (style.display === 'contents') return true;
      if (!element.checkVisibility({ visibilityProperty: true, opacityProperty: true })) return false;
      const box = element.getBoundingClientRect();
      return style.display.startsWith('inline') || (box.width > 1 && box.height > 1);
    };
    type Where = 'none' | 'words' | 'navigation';
    const sums = { words: 0, navigation: 0 };
    const texts: string[] = [];
    const count = (node: Node, where: Where): void => {
      for (const child of node.childNodes) {
        if (child.nodeType === Node.TEXT_NODE) {
          const text = (child.textContent ?? '').trim();
          if (text) texts.push(text);
          if (where !== 'none') sums[where] += text.split(/\s+/).filter((word) => /[\p{L}\p{N}]/u.test(word)).length;
        } else if (child instanceof HTMLElement) {
          if (['jfb-library-tree-item', 'style', 'script', 'template'].includes(child.localName) || !drawn(child))
            continue;
          const mine: Where = navigation.includes(child.localName)
            ? 'navigation'
            : where === 'none' && child.localName.startsWith('jfb-')
              ? 'words'
              : where;
          count(child, mine);
          if (child.shadowRoot) count(child.shadowRoot, mine);
        }
      }
    };
    count(document.body, 'none');
    return {
      overflowX: Math.max(0, document.documentElement.scrollWidth - innerWidth),
      scrollers: [...new Set(scrollers)].slice(0, 8),
      clipped: [...new Set(clipped)].slice(0, 12),
      words: sums.words,
      navigation: sums.navigation,
      text: texts.join('\n'),
    };
  }, NAVIGATION);

/**
 * The screen as the fold check reads it (`foldsInFolds` in support/ux-walk.ts): the drawn elements as the page composes
 * them, a shadow root in its host and what a slot shows in the slot, kept where an element has an id, a role,
 * `aria-expanded`, a popover or is a `details`, a `summary`, UUI's menu item or a tool of `FOLD_TOOLS`, and the rest
 * left out around their children.
 */
export const foldTree = (page: Page): Promise<FoldNode> =>
  page.evaluate((tools) => {
    interface Node {
      tag: string;
      id?: string;
      role?: string;
      name?: string;
      expanded?: string;
      controls?: string[];
      haspopup?: string;
      popovertarget?: boolean;
      popover?: boolean;
      open?: boolean;
      children: Node[];
    }
    const scopes = new Map<globalThis.Node, number>();
    const scopeOf = (element: Element) => {
      const root = element.getRootNode();
      if (!scopes.has(root)) scopes.set(root, scopes.size);
      return scopes.get(root) ?? 0;
    };
    const drawn = (element: Element) =>
      getComputedStyle(element).display === 'contents' ||
      element.checkVisibility({ visibilityProperty: true, opacityProperty: true });
    const kept = (element: Element) =>
      element.id !== '' ||
      ['role', 'aria-expanded', 'popover'].some((name) => element.hasAttribute(name)) ||
      ['details', 'summary', 'uui-menu-item', ...tools].includes(element.localName);
    // A form answers `form.children` with its control of that name, and the demo's newsletter has a field called
    // "children": an element's children are read by Element's own getter, which no control stands in front of.
    const ownChildren = Object.getOwnPropertyDescriptor(Element.prototype, 'children')?.get;
    const inside = (element: Element): Element[] =>
      element instanceof HTMLSlotElement
        ? element.assignedElements({ flatten: true })
        : [...(element.shadowRoot?.children ?? (ownChildren?.call(element) as HTMLCollection))];
    const read = (element: Element): Node[] => {
      if (['style', 'script', 'template'].includes(element.localName) || !drawn(element)) return [];
      const children = inside(element).flatMap(read);
      if (!kept(element)) return children;
      const scope = scopeOf(element);
      const attribute = (name: string) => element.getAttribute(name) ?? undefined;
      const text = (element.textContent ?? '').trim().replace(/\s+/g, ' ').slice(0, 60);
      const node: Node = { tag: element.localName, children };
      if (element.id) node.id = `${scope}:${element.id}`;
      const role = attribute('role');
      if (role) node.role = role;
      const name = attribute('aria-label') ?? attribute('label') ?? (text || undefined);
      if (name) node.name = name;
      const expanded = attribute('aria-expanded');
      if (expanded !== undefined) node.expanded = expanded;
      const controls = attribute('aria-controls');
      if (controls) node.controls = controls.split(/\s+/).map((id) => `${scope}:${id}`);
      const haspopup = attribute('aria-haspopup');
      if (haspopup !== undefined) node.haspopup = haspopup;
      if (element.hasAttribute('popovertarget')) node.popovertarget = true;
      if (element.hasAttribute('popover')) node.popover = true;
      if (element instanceof HTMLDetailsElement) node.open = element.open;
      return [node];
    };
    return { tag: 'body', children: read(document.body).flatMap((node) => node.children) };
  }, FOLD_TOOLS);

/** One walk: its shots and what each found, written beside them. */
export class Walk {
  readonly reports: ShotReport[] = [];
  readonly page: Page;
  readonly dir: string;

  constructor(page: Page, dir: string) {
    this.page = page;
    this.dir = dir;
    mkdirSync(dir, { recursive: true });
  }

  /** Reaches a screen, shoots it and measures it; a screen the walk cannot reach is noted, and the walk goes on. */
  async shot(screen: string, reach: () => Promise<void>, page = this.page): Promise<void> {
    if (!wantedScreen(screen)) return;
    try {
      await reach();
      await settle(page);
      const file = `${screen}.png`;
      await page.screenshot({ path: join(this.dir, file) });
      const { ours, umbraco } = splitAxe(await axeNodes(page));
      const folds = foldsInFolds(await foldTree(page));
      const { text, ...measured } = await measure(page);
      this.reports.push({ screen, file, axe: ours, axeUmbraco: umbraco, folds, keys: rawKeys(text), ...measured });
    } catch (error) {
      const message = (error instanceof Error ? error.message : String(error)).split('\n')[0]?.slice(0, 300) ?? '';
      await page.screenshot({ path: join(this.dir, `${screen}.failed.png`) }).catch(() => undefined);
      this.reports.push({
        screen,
        file: null,
        error: message,
        axe: [],
        axeUmbraco: 0,
        folds: [],
        keys: [],
        overflowX: 0,
        scrollers: [],
        clipped: [],
        words: 0,
        navigation: 0,
      });
    }
  }

  /** Writes what the shots found beside them: the walk's report.json, or another walk's own file. */
  write(combo: Combo, file = 'report.json'): void {
    writeFileSync(join(this.dir, file), JSON.stringify({ combo, screens: this.reports }, null, 2));
  }
}
