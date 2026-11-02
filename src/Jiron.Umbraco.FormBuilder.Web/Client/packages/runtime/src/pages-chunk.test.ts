// Moving through a form's pages in the browser (ROADMAP 6.5d, ADR-0155): Next asks the server to check the page and then shows the
// next one the rules leave, Back shows the one before, and the buttons, the progress, the page a post names, the focus and `jfb:page`
// follow; what the server refuses stays on the page, and the step onto a summary page is left to the post.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PageErrors } from './errors';
import type { FormModel } from './model';
import { enhancePages, type PageDetail } from './pages-chunk';
import type { CheckPage } from './send';
import { ENGLISH } from './testing';

const modelOf = (settings: Record<string, unknown> = {}): FormModel => ({
  alias: 'apply',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: { key: 'f', settings, pages: [] },
  fields: {},
  consents: {},
  ...ENGLISH,
});

const BUTTONS =
  '<div class="nav"><button type="submit" name="jfb-nav" value="next" data-jfb-nav="next">Next</button>' +
  '<button type="submit" name="jfb-nav" value="submit" data-jfb-nav="submit">Send</button>' +
  '<button type="submit" name="jfb-nav" value="back" data-jfb-nav="back">Back</button></div>';

const page = (key: string, shown: boolean, ruled = false, title = true): string =>
  `<section data-jfb-page="${key}"${shown ? '' : ' hidden'}${ruled ? ' data-jfb-hidden' : ''}>` +
  `${title ? `<h2 class="jfb-title">Page ${key}</h2>` : ''}<div class="field"></div></section>`;

const step = (key: string, untitled = false): string =>
  `<li data-jfb-step="${key}"><span${untitled ? ' data-jfb-numbered' : ''}>${key}</span></li>`;
const waiting = (...steps: string[]): string => `<template data-jfb-steps>${steps.join('')}</template>`;

function wake({
  pages = page('a', true) + page('b', false) + page('c', false),
  steps = step('a') + step('b') + step('c'),
  hiddenSteps = '',
  settings = {},
  answer = 'on' as Awaited<ReturnType<CheckPage>>,
  action = 'action="/x"',
} = {}) {
  document.body.innerHTML =
    `<form id="jfb-1" data-jfb-form="f" ${action}><template data-jfb-templates data-jfb-sending="Sending">${BUTTONS}</template>` +
    '<button type="submit" class="ready" name="jfb-nav" value="next">Next</button>' +
    `<div class="progress"><p data-jfb-words="Step {0} of {1}">Step 1 of 3</p><ol data-jfb-untitled="Page {0}">${steps}</ol>${hiddenSteps}</div>` +
    '<div data-jfb-summary></div><div data-jfb-notice="form_changed"></div>' +
    `${pages}<div class="nav" id="nav"><button type="submit" name="jfb-nav" value="next" data-jfb-nav="next">Next</button></div>` +
    '<input type="hidden" name="jfb-page" value="a"><div data-jfb-live></div></form>';
  const form = document.querySelector('form') as HTMLFormElement;
  const check = vi.fn<CheckPage>(() => Promise.resolve(answer));
  const moved: PageDetail[] = [];
  form.addEventListener('jfb:page', (event) => moved.push((event as CustomEvent<PageDetail>).detail));
  const paging = enhancePages(form, modelOf(settings), check);
  const errors = {} as PageErrors;
  const next = (): Promise<boolean> =>
    paging.next(form.querySelector('[data-jfb-nav="next"]') as HTMLButtonElement, errors);
  const shown = (): (string | undefined)[] =>
    [...form.querySelectorAll<HTMLElement>('[data-jfb-page]:not([hidden])')].map((each) => each.dataset['jfbPage']);
  const buttons = (): string[] =>
    [...form.querySelectorAll<HTMLButtonElement>('#nav > [data-jfb-nav]')].map((each) => each.value);
  return { form, paging, check, moved, next, shown, buttons, errors };
}

beforeEach(() => {
  HTMLElement.prototype.scrollIntoView = vi.fn();
});

afterEach(() => {
  document.body.innerHTML = '';
  vi.restoreAllMocks();
});

describe('Next', () => {
  it('asks the server about the page, then shows the next one', async () => {
    const { check, next, shown, errors, form } = wake();
    expect(await next()).toBe(true);

    expect(check).toHaveBeenCalledWith(form.querySelector('[data-jfb-nav="next"]') ?? expect.anything(), errors, 'a');
    expect(shown()).toEqual(['b']);
  });

  it('stays while the server has not answered', async () => {
    const { check, next, shown } = wake();
    let answer: (value: 'on') => void = () => undefined;
    check.mockImplementation(() => new Promise((resolve) => (answer = resolve)));
    const going = next();
    await Promise.resolve();
    expect(shown()).toEqual(['a']);

    answer('on');
    await going;
    expect(shown()).toEqual(['b']);
  });

  it('stays when the page answered the press itself', async () => {
    const { next, shown, moved } = wake({ answer: 'answered' });

    expect(await next()).toBe(true);
    expect(shown()).toEqual(['a']);
    expect(moved).toEqual([]);
  });

  it('leaves the press to the post when the server said so', async () => {
    const { next, shown } = wake({ answer: 'post' });

    expect(await next()).toBe(false);
    expect(shown()).toEqual(['a']);
  });

  it('skips a page the rules hide', async () => {
    const { next, shown } = wake({ pages: page('a', true) + page('b', false, true) + page('c', false) });
    await next();

    expect(shown()).toEqual(['c']);
  });

  it('has nowhere to go from the last page', async () => {
    const { next, check } = wake({ pages: page('a', false) + page('b', true) });

    expect(await next()).toBe(false);
    expect(check).not.toHaveBeenCalled();
  });
});

describe('a summary page, which the server writes', () => {
  const settings = { summaryPage: true };

  it('is reached by the post, asking nothing', async () => {
    const { next, check, shown } = wake({ settings, pages: page('a', false) + page('b', true) + page('c', false) });

    expect(await next()).toBe(false);
    expect(check).not.toHaveBeenCalled();
    expect(shown()).toEqual(['b']);
  });

  it('leaves the pages before it to move on the page', async () => {
    const { next, shown } = wake({ settings });
    await next();

    expect(shown()).toEqual(['b']);
  });

  it('moves on the page where no post can go, as in the headless element', async () => {
    const { next, shown } = wake({
      settings,
      action: '',
      pages: page('a', false) + page('b', true) + page('c', false),
    });
    await next();

    expect(shown()).toEqual(['c']);
  });
});

describe('Back', () => {
  it('shows the page before, asking nothing', async () => {
    const { paging, next, check, shown } = wake();
    await next();
    check.mockClear();

    expect(paging.back()).toBe(true);
    expect(shown()).toEqual(['a']);
    expect(check).not.toHaveBeenCalled();
  });

  it('skips a page the rules hide', () => {
    const { paging, shown } = wake({ pages: page('a', false) + page('b', false, true) + page('c', true) });
    paging.back();

    expect(shown()).toEqual(['a']);
  });

  it('has nowhere to go from the first page', () => {
    const { paging, shown } = wake();

    expect(paging.back()).toBe(false);
    expect(shown()).toEqual(['a']);
  });
});

describe('what follows a move', () => {
  it('writes the buttons of the page it shows, with their order and their words', async () => {
    const { next, buttons, form } = wake();
    expect(buttons(), 'on the first page').toEqual(['next']);

    await next();
    expect(buttons()).toEqual(['next', 'back']);
    expect(form.querySelector('#nav [data-jfb-nav="back"]')?.textContent).toBe('Back');

    await next();
    expect(buttons(), 'on the last page').toEqual(['submit', 'back']);
    expect(form.querySelector('#nav [data-jfb-nav="submit"]')?.textContent).toBe('Send');
  });

  it('keeps the Next that is there when it is the one wanted', async () => {
    const { next, form } = wake();
    const before = form.querySelector('#nav [data-jfb-nav="next"]');
    await next();
    const kept = form.querySelector('#nav [data-jfb-nav="next"]');
    await next();
    await Promise.resolve();

    expect(kept).not.toBe(before);
    expect(form.querySelector('#nav')).not.toBeNull();
  });

  it('points the hidden default button at the page, and names the page a later post comes from', async () => {
    const { next, form } = wake();
    await next();
    await next();

    expect(form.querySelector<HTMLButtonElement>('.ready')?.value).toBe('submit');
    expect(form.querySelector('.ready')?.textContent).toBe('Send');
    expect(form.querySelector<HTMLInputElement>('[name="jfb-page"]')?.value).toBe('c');
  });

  it('says the step, in the words, the list and the live region', async () => {
    const { next, form } = wake();
    await next();

    expect(form.querySelector('[data-jfb-words]')?.textContent).toBe('Step 2 of 3');
    const steps = [...form.querySelectorAll('[data-jfb-step]')];
    expect(steps.map((each) => each.getAttribute('aria-current'))).toEqual([null, 'step', null]);
    expect(steps.map((each) => each.hasAttribute('data-jfb-done'))).toEqual([true, false, false]);
    expect(form.querySelector('[data-jfb-live]')?.textContent).toBe('Step 2 of 3');
  });

  it('counts only the pages the rules show, and numbers an untitled one by its place', async () => {
    const { next, form } = wake({
      pages: page('a', true) + page('b', false, true) + page('c', false, false, false),
      steps: step('a') + step('c', true),
      hiddenSteps: waiting(step('b')),
    });
    expect(form.querySelector('[data-jfb-step="c"] span')?.textContent).toBe('Page 2');
    await next();

    expect(form.querySelector('[data-jfb-words]')?.textContent).toBe('Step 2 of 2');
    expect(form.querySelector('ol [data-jfb-step="b"]')).toBeNull();
  });

  it('keeps a title that recalls an answer, and numbers it by its place while it has none', async () => {
    const { form } = wake({
      pages: page('a', true) + page('b', false, true) + page('c', false),
      steps: step('a') + step('c', true).replace('<span', '<span data-jfb-text="c title" data-jfb-fallback="Page 3"'),
      hiddenSteps: waiting(step('b')),
    });
    const title = form.querySelector<HTMLElement>('[data-jfb-step="c"] span') as HTMLElement;
    expect(title.dataset['jfbFallback']).toBe('Page 2');

    title.textContent = 'About Ada';
    form.querySelector('[data-jfb-page="b"]')?.removeAttribute('data-jfb-hidden');
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(title.dataset['jfbFallback']).toBe('Page 3'));

    expect(title.textContent).toBe('About Ada');
  });

  it('fills a bar', async () => {
    const { next, form } = wake();
    form
      .querySelector('.progress')
      ?.insertAdjacentHTML('beforeend', '<progress aria-labelledby="t" max="9" value="9"></progress>');
    await next();

    expect(form.querySelector('progress')?.getAttribute('max')).toBe('3');
    expect(form.querySelector('progress')?.getAttribute('value')).toBe('2');
  });

  it("gives the focus to the page's title, and brings the form's top into view", async () => {
    const scrolled = vi.fn();
    HTMLElement.prototype.scrollIntoView = scrolled;
    const { next, form } = wake();
    await next();

    expect(document.activeElement).toBe(form.querySelector('[data-jfb-page="b"] .jfb-title'));
    expect(scrolled).toHaveBeenCalledWith({ block: 'start' });
  });

  it('gives it to the page itself when it has no title', async () => {
    const { next, form } = wake({ pages: page('a', true) + page('b', false, false, false) });
    await next();

    expect(document.activeElement).toBe(form.querySelector('[data-jfb-page="b"]'));
  });

  it('takes away what spoke of the page before', async () => {
    const { next, form } = wake();
    await next();

    expect(form.querySelector('[data-jfb-summary]')).toBeNull();
    expect(form.querySelector('[data-jfb-notice]')).toBeNull();
  });

  it('tells the site which page, and no answer', async () => {
    const { next, paging, moved } = wake();
    await next();
    paging.back();

    expect(moved).toEqual([
      { key: 'f', alias: 'apply', page: 'b', number: 2, total: 3 },
      { key: 'f', alias: 'apply', page: 'a', number: 1, total: 3 },
    ]);
  });
});

describe('a rule that changes the pages behind this one', () => {
  it('brings the step of a page it shows into the list, in its place, and takes it out again', async () => {
    const { form } = wake({
      pages: page('a', true) + page('b', false, true) + page('c', false),
      steps: step('a') + step('c'),
      hiddenSteps: waiting(step('b')),
    });
    const keys = (): (string | undefined)[] =>
      [...form.querySelectorAll<HTMLElement>('ol > li')].map((each) => each.dataset['jfbStep']);
    expect(keys()).toEqual(['a', 'c']);

    form.querySelector('[data-jfb-page="b"]')?.removeAttribute('data-jfb-hidden');
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(keys()).toEqual(['a', 'b', 'c']));
    expect(form.querySelector('[data-jfb-words]')?.textContent).toBe('Step 1 of 3');

    form.querySelector('[data-jfb-page="b"]')?.setAttribute('data-jfb-hidden', '');
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(keys()).toEqual(['a', 'c']));
  });

  it('shows the progress once there is a second page to go to, and hides it with one', async () => {
    const { form } = wake({ pages: page('a', true) + page('b', false) });
    const progress = form.querySelector<HTMLElement>('.progress') as HTMLElement;
    expect(progress.hidden).toBe(false);

    form.querySelector('[data-jfb-page="b"]')?.setAttribute('data-jfb-hidden', '');
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(progress.hidden).toBe(true));

    form.querySelector('[data-jfb-page="b"]')?.removeAttribute('data-jfb-hidden');
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await vi.waitFor(() => expect(progress.hidden).toBe(false));
  });

  it('takes the visitor to the next page it leaves when it hides this one', async () => {
    const { form, shown } = wake();
    const here = form.querySelector<HTMLElement>('[data-jfb-page="a"]') as HTMLElement;
    here.setAttribute('data-jfb-hidden', '');
    here.hidden = true;
    form.dispatchEvent(new Event('change', { bubbles: true }));

    await vi.waitFor(() => expect(shown()).toEqual(['b']));
    expect(document.activeElement).toBe(form.querySelector('[data-jfb-page="b"] .jfb-title'));
  });

  it('or to the one before, when none follows', async () => {
    const { form, shown } = wake({ pages: page('a', false) + page('b', true) });
    const here = form.querySelector<HTMLElement>('[data-jfb-page="b"]') as HTMLElement;
    here.setAttribute('data-jfb-hidden', '');
    here.hidden = true;
    form.dispatchEvent(new Event('change', { bubbles: true }));

    await vi.waitFor(() => expect(shown()).toEqual(['a']));
  });

  it('touches nothing while a check is out, and its answer goes by the rules as they are then', async () => {
    const { form, check, next, buttons, shown } = wake();
    let answer: (value: 'on') => void = () => undefined;
    check.mockImplementation(() => new Promise((resolve) => (answer = resolve)));
    const going = next();
    await Promise.resolve();
    form.querySelector('[data-jfb-page="b"]')?.setAttribute('data-jfb-hidden', '');
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(buttons(), 'the button that is busy stays').toEqual(['next']);

    answer('on');
    await going;
    expect(shown(), 'the page it just hid is skipped').toEqual(['c']);
  });

  it('leaves the busy button in place while a check is out, even when it would no longer be wanted', async () => {
    const { form, check, next, buttons } = wake({ pages: page('a', true) + page('b', false) });
    let answer: (value: 'answered') => void = () => undefined;
    check.mockImplementation(() => new Promise((resolve) => (answer = resolve)));
    const going = next();
    await Promise.resolve();
    form.querySelector('[data-jfb-page="b"]')?.setAttribute('data-jfb-hidden', '');
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 5));
    expect(buttons()).toEqual(['next']);

    answer('answered');
    await going;
    expect(buttons(), 'its answer brings them up to date').toEqual(['submit']);
  });

  it('stays, with Send, when the check passes and the rules have left no page to go to', async () => {
    const { form, check, next, buttons, shown } = wake({ pages: page('a', true) + page('b', false) });
    check.mockImplementation(() => {
      form.querySelector('[data-jfb-page="b"]')?.setAttribute('data-jfb-hidden', '');
      return Promise.resolve('on');
    });

    expect(await next()).toBe(true);
    expect(shown()).toEqual(['a']);
    expect(buttons()).toEqual(['submit']);
  });

  it('turns Next into Send when none is left to go to', async () => {
    const { form, buttons } = wake();
    for (const each of form.querySelectorAll<HTMLElement>('[data-jfb-page="b"], [data-jfb-page="c"]'))
      each.setAttribute('data-jfb-hidden', '');
    form.dispatchEvent(new Event('change', { bubbles: true }));

    await vi.waitFor(() => expect(buttons()).toEqual(['submit']));
    expect(form.querySelector('[data-jfb-words]')?.textContent).toBe('Step 1 of 1');
  });

  it('follows an input too', async () => {
    const { form, buttons } = wake({ pages: page('a', true) + page('b', false, true) });
    expect(buttons()).toEqual(['submit']);
    form.querySelector('[data-jfb-page="b"]')?.removeAttribute('data-jfb-hidden');
    form.dispatchEvent(new Event('input', { bubbles: true }));

    await vi.waitFor(() => expect(buttons()).toEqual(['next']));
  });

  it('leaves the focus on a button that is still wanted', async () => {
    const { form } = wake();
    const next = form.querySelector<HTMLElement>('#nav [data-jfb-nav="next"]');
    next?.focus();
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(document.activeElement).toBe(next);
  });
});

describe('errors on another page', () => {
  it('leave the page that shows when they are on it', () => {
    const { paging, form, moved } = wake();
    paging.reveal(form.querySelector('[data-jfb-page="a"] .field') as Element);

    expect(moved).toEqual([]);
    expect(form.querySelector('[data-jfb-summary]'), 'its summary stays').not.toBeNull();
  });

  it('show the page that holds them, without taking the focus', () => {
    const { paging, form, shown, moved } = wake();
    const before = document.activeElement;
    paging.reveal(form.querySelector('[data-jfb-page="c"] .field') as Element);

    expect(shown()).toEqual(['c']);
    expect(moved.map((each) => each.page)).toEqual(['c']);
    expect(document.activeElement).toBe(before);
  });
});
