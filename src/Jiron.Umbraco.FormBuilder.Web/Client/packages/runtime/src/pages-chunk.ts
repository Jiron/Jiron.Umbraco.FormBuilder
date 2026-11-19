/**
 * Moving through a form's pages in the browser (ROADMAP 6.5d, ADR-0155), loaded for a form of several pages that sends on
 * the page. Every page is on the page already, as the server writes them, with every answer kept in place: Next asks the
 * server to check the page, then shows the next one the rules leave, and Back shows the one before. The buttons, the
 * progress and the page a later post names follow, the focus goes to the page's title, and `jfb:page` tells the site.
 * A summary page is the server's to write, so the step onto it posts.
 */
import { proto } from '@jfb/logic/proto';
import { removeSummary } from './display';
import type { PageErrors } from './errors';
import type { FormModel } from './model';
import type { CheckPage } from './send';

/** What `jfb:page` tells a site: which form and which page, never an answer. */
export interface PageDetail {
  readonly key: string;
  readonly alias: string;
  /** The page's key. */
  readonly page: string;
  /** The page's place among those the rules show, from 1. */
  readonly number: number;
  /** How many pages the rules show. */
  readonly total: number;
}

/** A form's pages as its errors and buttons work with them. */
export interface Paging {
  /** Goes on from the page the check just passed: true when the page answered the press, false to post it. */
  next(button: HTMLButtonElement, errors: PageErrors): Promise<boolean>;
  /** Goes back a page: false when there is none to go to. */
  back(): boolean;
  /** Shows the page that holds an element, which is about to show what is wrong with it. */
  reveal(element: Element): void;
}

const PAGE = '[data-jfb-page]';
const NAV = '[data-jfb-nav]';

/** Takes a form's pages over from the post, with the buttons of its templates. */
export function enhancePages(form: HTMLFormElement, model: FormModel, check: CheckPage): Paging {
  const templates = proto(form).querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]');
  const list = proto(form).querySelector<HTMLElement>('[data-jfb-untitled]');
  const steps = new Map<string, HTMLElement>();
  for (const step of [
    ...(list?.children ?? []),
    ...(proto(form).querySelector<HTMLTemplateElement>('template[data-jfb-steps]')?.content.children ?? []),
  ])
    steps.set((step as HTMLElement).dataset['jfbStep'] ?? '', step as HTMLElement);
  let checking = false;

  const all = (): HTMLElement[] => [...proto(form).querySelectorAll<HTMLElement>(PAGE)];
  const shown = (): HTMLElement[] => all().filter((page) => !page.hasAttribute('data-jfb-hidden'));
  const current = (): HTMLElement | null => proto(form).querySelector<HTMLElement>(`${PAGE}:not([hidden])`);
  let was = current();
  const number = (words: string, ...values: readonly (string | number)[]): string =>
    values.reduce<string>((text, value, at) => text.replace(`{${String(at)}}`, String(value)), words);
  /** The step onto a summary page, which only a post can write; where no post can go it is a page like another. */
  const posts = (target: HTMLElement, pages: readonly HTMLElement[]): boolean =>
    model.form.settings?.summaryPage === true && target === pages.at(-1) && proto(form).hasAttribute('action');

  /** The buttons, the progress and the page a post names, as the server would write them for the page that shows. */
  const write = (): void => {
    const pages = shown();
    const page = current();
    was = page ?? was;
    const at = page === null ? -1 : pages.indexOf(page);
    const wanted = [at === pages.length - 1 || at < 0 ? 'submit' : 'next', ...(at > 0 ? ['back'] : [])];
    const buttons = proto(form).querySelector(`:scope > :not([hidden]) > ${NAV}`)?.parentElement;
    if (
      buttons != null &&
      [...buttons.querySelectorAll<HTMLButtonElement>(NAV)].map((each) => each.value).join() !== wanted.join()
    ) {
      // What the row holds besides its buttons, such as the link that saves for later (ADR-0163), stays after them.
      const others = [...buttons.children].filter((child) => !child.matches(NAV));
      buttons.replaceChildren(
        ...[...(templates?.content.querySelectorAll<HTMLButtonElement>(NAV) ?? [])]
          .filter((each) => wanted.includes(each.value))
          .map((each) => proto(form).ownerDocument.importNode(each, true)),
        ...others,
      );
    }
    const primary = buttons?.querySelector<HTMLButtonElement>(NAV);
    const ready = proto(form).querySelector<HTMLButtonElement>(':scope > button[name="jfb-nav"]');
    if (ready !== null && primary != null) {
      ready.value = primary.value;
      ready.textContent = primary.textContent;
    }
    const named = proto(form).querySelector<HTMLInputElement>(':scope > input[name="jfb-page"]');
    if (named !== null && page !== null) named.value = page.dataset['jfbPage'] ?? '';

    const text = proto(form).querySelector<HTMLElement>('[data-jfb-words]');
    if (text !== null) {
      text.textContent = number(text.dataset['jfbWords'] ?? '', Math.max(at, 0) + 1, Math.max(pages.length, 1));
      const line = text.parentElement;
      if (line !== null) proto(line).hidden = pages.length < 2;
    }
    const bar = proto(form).querySelector('progress[aria-labelledby]');
    bar?.setAttribute('max', String(pages.length));
    bar?.setAttribute('value', String(at + 1));
    if (list === null) return;
    const wantedSteps = pages.flatMap((each) => steps.get(each.dataset['jfbPage'] ?? '') ?? []);
    if (
      list.children.length !== wantedSteps.length ||
      [...list.children].some((each, place) => each !== wantedSteps[place])
    ) {
      list.replaceChildren(...wantedSteps);
    }
    wantedSteps.forEach((step, place) => {
      step.toggleAttribute('data-jfb-done', place < at);
      if (place === at) step.setAttribute('aria-current', 'step');
      else step.removeAttribute('aria-current');
      const title = step.querySelector<HTMLElement>('[data-jfb-numbered]');
      if (title === null) return;
      const words = number(list.dataset['jfbUntitled'] ?? '', place + 1);
      // A title that recalls an answer keeps it once it has one, and falls back to the number while it has none.
      if (title.dataset['jfbFallback'] === undefined || title.textContent === title.dataset['jfbFallback'])
        title.textContent = words;
      if (title.dataset['jfbFallback'] !== undefined) title.dataset['jfbFallback'] = words;
    });
  };

  const show = (page: HTMLElement): void => {
    for (const each of all()) each.hidden = each !== page || each.hasAttribute('data-jfb-hidden');
    removeSummary(form);
    for (const notice of proto(form).querySelectorAll(':scope > [data-jfb-notice]')) notice.remove();
    write();
    const pages = shown();
    proto(form).dispatchEvent(
      new CustomEvent<PageDetail>('jfb:page', {
        bubbles: true,
        detail: {
          key: proto(form).dataset['jfbForm'] ?? '',
          alias: model.alias,
          page: page.dataset['jfbPage'] ?? '',
          number: pages.indexOf(page) + 1,
          total: pages.length,
        },
      }),
    );
  };

  /** Shows a page the visitor moved to: its title takes the focus, and the form's top comes into view with its progress. */
  const land = (page: HTMLElement): void => {
    // The way the visitor moved, which the theme brings the page in from (ADR-0022); a page as it loads has none.
    const pages = all();
    const from = current() ?? was;
    proto(form).setAttribute(
      'data-jfb-direction',
      from !== null && pages.indexOf(page) < pages.indexOf(from) ? 'back' : 'next',
    );
    show(page);
    const title = page.querySelector<HTMLElement>('.jfb-title:not([hidden])') ?? page;
    title.tabIndex = -1;
    title.focus({ preventScroll: true });
    proto(form).scrollIntoView({ block: 'start' });
    const live = proto(form).querySelector(':scope > [data-jfb-live]');
    const text = proto(form).querySelector('[data-jfb-words]');
    if (live !== null && text !== null) live.textContent = text.textContent;
  };

  // A rule may show or hide a page: what comes next changes with the answers, once the rules have read them. A rule that hides
  // the page itself leaves the visitor on the nearest one it shows, as a post would. While a check is out, its answer decides.
  const later = (): void => {
    setTimeout(() => {
      if (checking) return;
      if (current() !== null) return write();
      const pages = all();
      const place = was === null ? 0 : pages.indexOf(was);
      const left = shown();
      const target = left.find((each) => pages.indexOf(each) > place) ?? left.at(-1);
      if (target === undefined) write();
      else land(target);
    });
  };
  proto(form).addEventListener('input', later);
  proto(form).addEventListener('change', later);
  write();

  return {
    async next(button, errors) {
      const before = shown();
      const page = current();
      const first = page === null ? undefined : before[before.indexOf(page) + 1];
      if (page === null || first === undefined || posts(first, before)) return false;
      checking = true;
      const answer = await check(button, errors, page.dataset['jfbPage'] ?? '').finally(() => (checking = false));
      if (answer === 'post') return false;
      // The answers may have changed while it asked, and the rules with them.
      const pages = shown();
      const target = pages[pages.indexOf(page) + 1];
      if (answer === 'on' && target !== undefined && posts(target, pages)) return false;
      if (answer === 'on' && target !== undefined) land(target);
      else write();
      return true;
    },
    back() {
      const pages = shown();
      const page = current();
      const target = page === null ? undefined : pages[pages.indexOf(page) - 1];
      if (target === undefined) return false;
      land(target);
      return true;
    },
    reveal(element) {
      const page = element.closest<HTMLElement>(PAGE);
      if (page?.hidden === true) show(page);
    },
  };
}
