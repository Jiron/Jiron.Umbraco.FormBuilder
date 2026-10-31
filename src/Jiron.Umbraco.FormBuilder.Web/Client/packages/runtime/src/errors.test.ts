// Errors on the page (ROADMAP 6.4e, ADR-0113): shown as the visitor leaves a field they changed, said once in the live
// region, gone on the input that fixes them; after a refused send or Next every error with the summary, which takes
// the focus, and `jfb:invalid` without an answer; a passing check lets the post go on with the button pressed; the
// server's errors stay while their field is unchanged and join only a refused send; a field the page leaves out is the
// server's to check; and a send whose errors are on another page goes to the server.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { engineOf } from './engine';
import { pageErrorsOf, type InvalidDetail } from './errors';
import type { FormModel, ModelField, ModelPage } from './model';
import { ENGLISH } from './testing';

const NOW = Date.UTC(2026, 9, 30, 9, 15, 0);

const field = (key: string, type: string, alias: string, more: Partial<ModelField> = {}): ModelField => ({
  key,
  type,
  alias,
  settings: {},
  ...more,
});

const KINDS: Readonly<Record<string, string>> = { text: 'text', email: 'text', radio: 'choice' };

function modelOf(pages: ModelPage[], settings: FormModel['form']['settings'] = {}): FormModel {
  const all = pages.flatMap((page) => page.groups.flatMap((group) => group.fields));
  return {
    alias: 'contact',
    revision: 1,
    culture: 'en-US',
    timeZone: 'UTC',
    open: true,
    form: { key: 'f', pages, settings },
    fields: Object.fromEntries(
      all.map((each) => [each.key, { kind: KINDS[each.type] ?? null, canBeRequired: true, builtIn: true }]),
    ),
    consents: {},
    ...ENGLISH,
  };
}

const NAME = field('k-name', 'text', 'name', { required: true, label: 'Name' });
const EMAIL = field('k-email', 'email', 'email', { required: true, label: 'Email address' });
const WAY = field('k-way', 'radio', 'way', {
  label: 'Way',
  settings: { other: false },
  options: { items: [{ value: 'post' }, { value: 'email' }] },
});
const DETAIL = field('k-detail', 'text', 'detail', {
  required: true,
  label: 'Detail',
  rules: [{ effect: 'show', match: 'all', conditions: [{ field: 'k-way', op: 'equals', value: 'email' }] }],
});

const TEMPLATES =
  '<template data-jfb-templates><div class="jfb-error-summary theme-alert" data-jfb-summary role="alert" tabindex="-1"' +
  ' aria-labelledby="jfb-1-summary"><h2 id="jfb-1-summary"></h2><ul role="list"><li><a href="#"></a></li></ul></div>' +
  '<p class="jfb-error theme-error" data-jfb-error></p></template>';

const text = (key: string, alias: string, label: string, more = ''): string =>
  `<div class="jfb-field" data-jfb-field="${key}" data-jfb-alias="${alias}" data-jfb-type="text" ${more}>` +
  `<label class="jfb-label" for="jfb-1-${alias}">${label}</label>` +
  `<input class="jfb-control" id="jfb-1-${alias}" name="${alias}"></div>`;

const WAY_HTML =
  '<div class="jfb-field" data-jfb-field="k-way" data-jfb-alias="way" data-jfb-type="radio"><fieldset>' +
  '<legend class="jfb-label">Way</legend>' +
  '<input type="radio" id="jfb-1-way" name="way" value="post"><label for="jfb-1-way">By post</label>' +
  '<input type="radio" id="jfb-1-way-email" name="way" value="email"><label for="jfb-1-way-email">By email</label>' +
  '</fieldset></div>';

const BUTTONS =
  '<div class="jfb-navigation"><button type="submit" name="jfb-nav" value="back" id="back">Back</button>' +
  '<button type="submit" name="jfb-nav" value="submit" id="send">Send</button></div>';

/** A one-page contact form as the views write it, with its templates, its note, its live region. */
function contact(model: FormModel, fields: string, before = '') {
  document.body.innerHTML =
    `<form id="jfb-1" class="jfb-form" data-jfb-form="f" data-jfb-contract="1">${TEMPLATES}${before}` +
    `<p class="jfb-help">Questions marked * need an answer.</p>` +
    `<section class="jfb-page" data-jfb-page="p1"><div data-jfb-group="g1">${fields}</div></section>` +
    `${BUTTONS}<div data-jfb-live aria-live="polite"></div></form>`;
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  const sent: string[] = [];
  form.requestSubmit = (button?: HTMLElement | null) => {
    sent.push(button instanceof HTMLButtonElement ? button.value : '');
  };
  const invalid: InvalidDetail[] = [];
  form.addEventListener('jfb:invalid', (event) => invalid.push((event as CustomEvent<InvalidDetail>).detail));
  const errors = pageErrorsOf(form, model, engineOf(model, null), () => NOW);
  return { form, sent, invalid, errors };
}

const ONE_PAGE = modelOf([{ key: 'p1', groups: [{ key: 'g1', fields: [NAME, EMAIL, WAY, DETAIL] }] }]);
const FIELDS =
  text('k-name', 'name', 'Name') +
  text('k-email', 'email', 'Email address') +
  WAY_HTML +
  text('k-detail', 'detail', 'Detail', 'hidden data-jfb-hidden');

const control = (id: string): HTMLInputElement => {
  const found = document.getElementById(id);
  if (!(found instanceof HTMLInputElement)) throw new Error(`no control ${id}`);
  return found;
};

function type(id: string, value: string): void {
  const input = control(id);
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
}

function pick(id: string): void {
  const input = control(id);
  input.checked = true;
  input.dispatchEvent(new Event('change', { bubbles: true }));
}

function leave(id: string, to: Element | null = null): void {
  control(id).dispatchEvent(new FocusEvent('focusout', { bubbles: true, relatedTarget: to }));
}

/** Presses a button as a visitor does: the form's submit event, with the button as its submitter. */
function press(form: HTMLFormElement, id: string): SubmitEvent {
  const button = document.getElementById(id);
  const event = new SubmitEvent('submit', { bubbles: true, cancelable: true, submitter: button });
  form.dispatchEvent(event);
  return event;
}

/** Lets the checks run: their words load, their promises settle. */
const settle = (): Promise<void> => new Promise((resolve) => setTimeout(resolve, 20));

const shownOf = (form: HTMLFormElement) => ({
  inline: [...form.querySelectorAll('[data-jfb-error]')].map((each) => `${each.id}: ${each.textContent}`),
  summary: [...form.querySelectorAll(':scope > [data-jfb-summary] a')].map(
    (link) => `${link.getAttribute('href') ?? ''} ${link.textContent}`,
  ),
});

afterEach(() => {
  document.body.innerHTML = '';
  vi.useRealTimers();
});

describe('leaving a field', () => {
  it('shows the error of a field the visitor changed, marks its control and says it once in the live region', async () => {
    const { form } = contact(ONE_PAGE, FIELDS);
    type('jfb-1-email', 'not an address');
    leave('jfb-1-email');
    await settle();
    const email = control('jfb-1-email');
    expect(shownOf(form).inline).toEqual([
      'jfb-1-email-error: Enter a complete email address, for example name@example.com',
    ]);
    expect(document.getElementById('jfb-1-email-error')?.className).toBe('jfb-error theme-error');
    expect([email.getAttribute('aria-invalid'), email.getAttribute('aria-describedby')]).toEqual([
      'true',
      'jfb-1-email-error',
    ]);
    await vi.waitFor(() =>
      expect(form.querySelector('[data-jfb-live]')?.textContent).toBe(
        'Email address: Enter a complete email address, for example name@example.com',
      ),
    );
    expect(form.querySelector('[data-jfb-summary]'), 'no summary before a send').toBeNull();
  });

  it('checks nothing for a field the visitor only passed through, or while the focus stays in the field', async () => {
    const required = modelOf([
      { key: 'p1', groups: [{ key: 'g1', fields: [NAME, EMAIL, { ...WAY, required: true }, DETAIL] }] },
    ]);
    const { form } = contact(required, FIELDS);
    leave('jfb-1-name');
    control('jfb-1-way').dispatchEvent(new Event('change', { bubbles: true }));
    leave('jfb-1-way', control('jfb-1-way-email'));
    await settle();
    expect(shownOf(form).inline).toEqual([]);
  });

  it("keeps a field's error as the visitor corrects it and clears it on the input that fixes it", async () => {
    const { form } = contact(ONE_PAGE, FIELDS);
    type('jfb-1-email', 'x');
    leave('jfb-1-email');
    await settle();
    type('jfb-1-email', '');
    await settle();
    expect(shownOf(form).inline, 'the message changes once the visitor leaves').toEqual([
      'jfb-1-email-error: Enter a complete email address, for example name@example.com',
    ]);
    leave('jfb-1-email');
    await settle();
    expect(shownOf(form).inline).toEqual(['jfb-1-email-error: Enter an email address']);
    type('jfb-1-email', 'ada@example.com');
    await settle();
    const email = control('jfb-1-email');
    expect(shownOf(form).inline).toEqual([]);
    expect([email.hasAttribute('aria-invalid'), email.hasAttribute('aria-describedby')]).toEqual([false, false]);
  });
});

describe('a refused send', () => {
  it('stops the post, shows every error and the summary before the note, gives it the focus and fires jfb:invalid', async () => {
    const { form, sent, invalid } = contact(ONE_PAGE, FIELDS);
    type('jfb-1-email', 'ada@');
    const event = press(form, 'send');
    expect(event.defaultPrevented).toBe(true);
    await settle();
    expect(sent).toEqual([]);
    expect(shownOf(form)).toEqual({
      inline: [
        'jfb-1-name-error: Enter an answer',
        'jfb-1-email-error: Enter a complete email address, for example name@example.com',
      ],
      summary: [
        '#jfb-1-name Name: Enter an answer',
        '#jfb-1-email Email address: Enter a complete email address, for example name@example.com',
      ],
    });
    const summary = form.querySelector<HTMLElement>(':scope > [data-jfb-summary]');
    expect(summary?.className).toBe('jfb-error-summary theme-alert');
    expect(summary?.querySelector('h2')?.textContent).toBe('These answers need a change');
    expect(summary?.nextElementSibling?.matches('p.jfb-help')).toBe(true);
    expect(document.activeElement).toBe(summary);
    expect(invalid).toEqual([
      {
        key: 'f',
        alias: 'contact',
        scope: 'send',
        errors: [
          { field: 'name', kind: 'required' },
          { field: 'email', kind: 'invalid' },
        ],
      },
    ]);
    expect(JSON.stringify(invalid)).not.toContain('ada@');
  });

  it("marks a refused group's choices, not its fieldset, and clears the marks once it is answered (6.8c)", async () => {
    const required = modelOf([{ key: 'p1', groups: [{ key: 'g1', fields: [NAME, { ...WAY, required: true }] }] }]);
    const described = WAY_HTML.replace('value="email">', 'value="email" aria-describedby="jfb-1-way-email-text">');
    const { form } = contact(required, text('k-name', 'name', 'Name') + described);
    type('jfb-1-name', 'Ada');
    const marks = () =>
      [...form.querySelectorAll('fieldset, [type="radio"]')].map((each) => [
        each.getAttribute('aria-invalid'),
        each.getAttribute('aria-describedby'),
      ]);
    press(form, 'send');
    await settle();
    expect(shownOf(form).inline).toEqual(['jfb-1-way-error: Choose an answer']);
    expect(
      marks(),
      "a fieldset's role takes no invalid state, so each choice says it, after its own description",
    ).toEqual([
      [null, null],
      ['true', 'jfb-1-way-error'],
      ['true', 'jfb-1-way-email-text jfb-1-way-error'],
    ]);
    pick('jfb-1-way-email');
    await settle();
    expect(shownOf(form).inline).toEqual([]);
    expect(marks()).toEqual([
      [null, null],
      [null, null],
      [null, 'jfb-1-way-email-text'],
    ]);
  });

  it('updates the summary in its place on the next refusal, and takes it away once no error shows', async () => {
    const { form } = contact(ONE_PAGE, FIELDS);
    type('jfb-1-email', 'x');
    press(form, 'send');
    await settle();
    const summary = form.querySelector(':scope > [data-jfb-summary]');
    expect(shownOf(form).summary).toHaveLength(2);
    type('jfb-1-name', 'Ada');
    await settle();
    press(form, 'send');
    await settle();
    expect(form.querySelector(':scope > [data-jfb-summary]')).toBe(summary);
    expect(shownOf(form).summary).toEqual([
      '#jfb-1-email Email address: Enter a complete email address, for example name@example.com',
    ]);
    expect(summary?.querySelector('h2')?.textContent).toBe('This answer needs a change');
    type('jfb-1-email', 'ada@example.com');
    await settle();
    expect(form.querySelector('[data-jfb-summary]')).toBeNull();
  });

  it('takes away the error of a field the rules hide', async () => {
    const { form } = contact(ONE_PAGE, FIELDS);
    control('jfb-1-way-email').checked = true;
    const detail = form.querySelector<HTMLElement>('[data-jfb-field="k-detail"]');
    detail?.removeAttribute('hidden');
    detail?.removeAttribute('data-jfb-hidden');
    press(form, 'send');
    await settle();
    expect(shownOf(form).inline).toContain('jfb-1-detail-error: Enter an answer');
    control('jfb-1-way').checked = true;
    control('jfb-1-way').dispatchEvent(new Event('change', { bubbles: true }));
    await settle();
    expect(shownOf(form).inline).not.toContain('jfb-1-detail-error: Enter an answer');
  });

  it('says the empty submission in the summary, linked to the first field, until the visitor answers', async () => {
    const { form, invalid } = contact(ONE_PAGE, FIELDS);
    press(form, 'send');
    await settle();
    // The name is required, but a send with no answer at all is refused as empty, alone.
    expect(shownOf(form)).toEqual({
      inline: [],
      summary: ['#jfb-1-name Answer at least one question before you send the form'],
    });
    expect(invalid.map((each) => each.errors)).toEqual([[{ field: null, kind: 'empty' }]]);
    type('jfb-1-name', 'A');
    await settle();
    expect(form.querySelector('[data-jfb-summary]')).toBeNull();
  });

  it("moves the focus to the first field's control when the form shows no summary", async () => {
    const { form } = contact(
      modelOf([{ key: 'p1', groups: [{ key: 'g1', fields: [NAME, EMAIL, WAY, DETAIL] }] }], {
        validationSummary: false,
      }),
      FIELDS,
    );
    pick('jfb-1-way');
    press(form, 'send');
    await settle();
    expect(form.querySelector('[data-jfb-summary]')).toBeNull();
    expect(shownOf(form).inline).toHaveLength(2);
    expect(document.activeElement).toBe(control('jfb-1-name'));
  });

  it('does nothing for a second press while the check runs, and nothing for Back', async () => {
    const { form, invalid } = contact(ONE_PAGE, FIELDS);
    type('jfb-1-email', 'x');
    press(form, 'send');
    expect(press(form, 'send').defaultPrevented).toBe(true);
    await settle();
    expect(invalid).toHaveLength(1);
    expect(press(form, 'back').defaultPrevented).toBe(false);
  });
});

describe('a send the page does not stop', () => {
  it('goes on as without JavaScript, with the button the visitor pressed', async () => {
    const { form, sent } = contact(ONE_PAGE, FIELDS);
    type('jfb-1-name', 'Ada');
    type('jfb-1-email', 'ada@example.com');
    expect(press(form, 'send').defaultPrevented).toBe(true);
    await settle();
    expect(sent).toEqual(['submit']);
  });

  it("goes on in a task of its own, after the press's submit event, which ignores a submission asked for in it", async () => {
    const { form, sent } = contact(ONE_PAGE, FIELDS);
    type('jfb-1-name', 'Ada');
    type('jfb-1-email', 'ada@example.com');
    leave('jfb-1-email');
    await settle();
    press(form, 'send');
    for (let turn = 0; turn < 50; turn++) await Promise.resolve();
    expect(sent, 'the check is done, the words loaded, yet no post within the event').toEqual([]);
    await settle();
    expect(sent).toEqual(['submit']);
    press(form, 'send');
    await settle();
    expect(sent, 'and a later press sends again').toEqual(['submit', 'submit']);
  });

  it('goes to the server when its errors lie on a page the server has not shown', async () => {
    const pages: ModelPage[] = [
      { key: 'p1', groups: [{ key: 'g1', fields: [NAME] }] },
      { key: 'p2', groups: [{ key: 'g2', fields: [EMAIL] }] },
    ];
    const { form, sent } = contact(modelOf(pages), text('k-name', 'name', 'Name'));
    form
      .querySelector('section')
      ?.insertAdjacentHTML(
        'afterend',
        `<section class="jfb-page" data-jfb-page="p2" hidden><div data-jfb-group="g2">${text('k-email', 'email', 'Email address')}</div></section>`,
      );
    form.querySelector('section')?.setAttribute('hidden', '');
    form.querySelectorAll('section')[1]?.removeAttribute('hidden');
    // The second page is the one shown, its answer given; the first page's name is missing.
    type('jfb-1-email', 'ada@example.com');
    press(form, 'send');
    await settle();
    expect(sent).toEqual(['submit']);
    expect(shownOf(form)).toEqual({ inline: [], summary: [] });
  });
});

describe('a field the page leaves out', () => {
  it("is the server's to check: a send is refused for the other fields' errors alone (6.8b)", async () => {
    // As for a member whose own address stands in for the email field, which their page leaves out (ADR-0124).
    const both = modelOf([{ key: 'p1', groups: [{ key: 'g1', fields: [NAME, EMAIL, WAY] }] }]);
    const { form, sent, invalid } = contact(both, text('k-name', 'name', 'Name') + WAY_HTML);
    pick('jfb-1-way');
    press(form, 'send');
    await settle();
    expect(sent).toEqual([]);
    expect(shownOf(form)).toEqual({
      inline: ['jfb-1-name-error: Enter an answer'],
      summary: ['#jfb-1-name Name: Enter an answer'],
    });
    expect(invalid[0]?.errors).toEqual([{ field: 'name', kind: 'required' }]);
    type('jfb-1-name', 'Ada');
    press(form, 'send');
    await settle();
    expect(sent, 'and the send goes on once the others pass').toEqual(['submit']);
  });
});

describe('the summary', () => {
  it("links move the focus to their field's control", async () => {
    const { form } = contact(ONE_PAGE, FIELDS);
    pick('jfb-1-way');
    press(form, 'send');
    await settle();
    const link = form.querySelector<HTMLAnchorElement>('[data-jfb-summary] a[href="#jfb-1-email"]');
    const click = new MouseEvent('click', { bubbles: true, cancelable: true });
    link?.dispatchEvent(click);
    expect(click.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(control('jfb-1-email'));
  });
});

describe("the server's errors after a post without JavaScript", () => {
  const unique = field('k-email', 'email', 'email', {
    required: true,
    label: 'Email address',
    validation: [{ kind: 'uniquePerForm' }],
  });
  const model = modelOf([{ key: 'p1', groups: [{ key: 'g1', fields: [NAME, unique] }] }]);
  const served =
    text('k-name', 'name', 'Name') +
    '<div class="jfb-field" data-jfb-field="k-email" data-jfb-alias="email" data-jfb-type="email">' +
    '<label class="jfb-label" for="jfb-1-email">Email address</label>' +
    '<input class="jfb-control" id="jfb-1-email" name="email" value="ada@example.com" aria-invalid="true"' +
    ' aria-describedby="jfb-1-email-error">' +
    '<p class="jfb-error theme-error" id="jfb-1-email-error" data-jfb-error>This address has sent the form already</p>' +
    '</div>';
  const summary =
    '<div class="jfb-error-summary theme-alert" data-jfb-summary role="alert" tabindex="-1" aria-labelledby="jfb-1-summary">' +
    '<h2 id="jfb-1-summary">This answer needs a change</h2><ul role="list"><li><a href="#jfb-1-email">' +
    'This address has sent the form already</a></li></ul></div>';

  it("stay, in a refusal's summary too, while their field is unchanged, and go once it changes", async () => {
    const { form, invalid } = contact(model, served, summary);
    control('jfb-1-name').value = '';
    press(form, 'send');
    await settle();
    expect(shownOf(form)).toEqual({
      inline: ['jfb-1-name-error: Enter an answer', 'jfb-1-email-error: This address has sent the form already'],
      summary: ['#jfb-1-name Name: Enter an answer', '#jfb-1-email This address has sent the form already'],
    });
    expect(invalid[0]?.errors).toEqual([
      { field: 'name', kind: 'required' },
      { field: 'email', kind: 'server' },
    ]);
    type('jfb-1-email', 'lin@example.com');
    await settle();
    expect(shownOf(form).inline).toEqual(['jfb-1-name-error: Enter an answer']);
  });

  it("let a send go on when the page's own checks pass, as the server checks it again (6.8b)", async () => {
    const { form, sent, invalid } = contact(model, served, summary);
    type('jfb-1-name', 'Ada');
    press(form, 'send');
    await settle();
    expect(sent).toEqual(['submit']);
    expect(invalid).toEqual([]);
    expect(shownOf(form).inline, 'still shown until the server answers').toEqual([
      'jfb-1-email-error: This address has sent the form already',
    ]);
  });

  it('go when the rules hide their field', async () => {
    const { form } = contact(model, served, summary);
    const email = form.querySelector<HTMLElement>('[data-jfb-field="k-email"]');
    email?.setAttribute('hidden', '');
    email?.setAttribute('data-jfb-hidden', '');
    type('jfb-1-name', 'Ada');
    await settle();
    expect(shownOf(form)).toEqual({ inline: [], summary: [] });
  });
});
