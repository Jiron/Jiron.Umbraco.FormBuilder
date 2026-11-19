import type { Page } from '@playwright/test';

// Every member of a form covered as a field of that name would cover it (ADR-0015 keeps a control's name its alias). A
// browser answers a read on a form with its control of that name in front of every built-in member, so an init script
// gives each form, as it enters the page, an <output> for every name of a form element's members that an alias could
// be, in this browser: owned by the form through its `form` attribute, kept outside it and hidden. An <output> is listed
// among the form's elements, so the form answers its name; it posts nothing, and unlike an <object> the document does
// not answer its name too. A member a later browser adds is covered as well, without a list to keep.
//
// The covers come as the form is parsed or put in, before the runtime's module runs on it, and the script notes at each
// `jfb:ready` which names a plain read still gets the form's own member for, so a spec can say there were none.
//
// With every member covered, a locator that starts at the form, or a selector whose match Playwright's engine checks on
// the way up past the form, asks the form for its own members (its querySelectorAll, its matches), and an action's
// check whether an element is enabled asks each ancestor's attributes: a spec on a covered form starts every locator at
// the document with one selector that names nothing above the element it finds, and presses and types by keyboard.

/**
 * Covers every member of every form the page shows from its first script on, but the names in `taken` (the form's own
 * fields of those names, whose controls cover them already).
 */
export async function coverEveryMember(page: Page, taken: readonly string[] = []): Promise<void> {
  await page.addInitScript(
    (kept: string[]) => {
      const alias = /^[a-z][A-Za-z0-9]{0,63}$/;
      const names: string[] = [];
      for (
        let link: object | null = HTMLFormElement.prototype;
        link !== null;
        link = Object.getPrototypeOf(link) as object | null
      )
        for (const name of Object.getOwnPropertyNames(link))
          if (alias.test(name) && !kept.includes(name) && !names.includes(name)) names.push(name);
      const done = new WeakSet<Element>();
      const cover = (form: HTMLFormElement): void => {
        const id = Element.prototype.getAttribute.call(form, 'id');
        if (done.has(form) || id === null) return;
        done.add(form);
        for (const name of names) {
          const output = document.createElement('output');
          output.hidden = true;
          output.name = name;
          output.setAttribute('form', id);
          (document.body ?? document.documentElement).append(output);
        }
      };
      new MutationObserver((changes) => {
        for (const change of changes)
          for (const node of change.addedNodes) {
            if (!(node instanceof Element)) continue;
            if (node instanceof HTMLFormElement) cover(node);
            for (const form of Element.prototype.querySelectorAll.call(node, 'form')) cover(form as HTMLFormElement);
          }
      }).observe(document, { childList: true, subtree: true });
      const uncovered: string[][] = [];
      Object.assign(window, { jfbUncoveredAtReady: uncovered });
      document.addEventListener('jfb:ready', (event) => {
        const plain = event.target as unknown as Record<string, unknown>;
        uncovered.push(
          names.filter((name) => !(plain[name] instanceof Element || plain[name] instanceof RadioNodeList)),
        );
      });
    },
    [...taken],
  );
}

/** For each `jfb:ready` the page heard, the names a plain read on that form still got the form's own member for. */
export const uncoveredAtReady = (page: Page): Promise<string[][]> =>
  page.evaluate(() => [...((window as unknown as { jfbUncoveredAtReady?: string[][] }).jfbUncoveredAtReady ?? [])]);
