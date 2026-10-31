// The values the page shows beside its answers (ROADMAP 6.4f, ADR-0114): each calculation's result as the visitor
// answers, in its field's format, a row's in its row, nothing without a value or while the rules hide it, written only
// when it changes and said once the visitor pauses, in one line; and a slider's value beside its thumb and in its
// `aria-valuetext`. The server's own pages hold the
// texts to the server's in outputs.fixture.test.ts.
import { FORMULAS } from '@jfb/logic/calc';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { engineOf } from './engine';
import type { FormModel, ModelField } from './model';
import { followSliders } from './outputs';
import { rulesOf } from './rules';
import { ENGLISH } from './testing';

const NOW = Date.UTC(2026, 9, 30, 9, 15, 0);

const field = (key: string, type: string, alias: string, more: Partial<ModelField> = {}): ModelField => ({
  key,
  type,
  alias,
  settings: {},
  ...more,
});

const KINDS: Readonly<Record<string, string | null>> = {
  number: 'number',
  calculation: 'number',
  slider: 'number',
  date: 'date',
  radio: 'choice',
  repeater: null,
};

function modelOf(fields: ModelField[], builtIn = true): FormModel {
  return {
    alias: 'booking',
    revision: 1,
    culture: 'en-US',
    timeZone: 'Europe/London',
    open: true,
    form: { key: 'f', pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }] },
    fields: Object.fromEntries(
      [...fields, ...fields.flatMap((each) => each.fields ?? [])].map((each) => [
        each.key,
        { kind: KINDS[each.type] ?? null, canBeRequired: each.type !== 'repeater', builtIn },
      ]),
    ),
    consents: {},
    ...ENGLISH,
  };
}

const calculation = (
  key: string,
  alias: string,
  formula: string,
  format: object = {},
  more: Partial<ModelField> = {},
) => field(key, 'calculation', alias, { settings: { formula, format }, ...more });

const BOOKING = [
  field('k-price', 'number', 'price'),
  field('k-quantity', 'number', 'quantity'),
  field('k-from', 'date', 'from'),
  field('k-to', 'date', 'to'),
  field('k-insured', 'radio', 'insured'),
  calculation('k-total', 'total', 'price * quantity', { prefix: 'EUR', decimals: 2 }),
  calculation('k-share', 'share', 'price / quantity'),
  calculation('k-nights', 'nights', 'daysBetween(from, to)', { suffix: 'nights', decimals: 0 }),
  calculation(
    'k-cover',
    'cover',
    'total * 0.1',
    { prefix: 'EUR' },
    {
      rules: [{ effect: 'show', match: 'all', conditions: [{ field: 'k-insured', op: 'equals', value: 'yes' }] }],
    },
  ),
  field('k-items', 'repeater', 'items', {
    fields: [field('k-cost', 'number', 'cost'), calculation('k-vat', 'vat', 'cost * 0.2', { suffix: 'VAT' })],
  }),
];

const output = (key: string, id: string, text = '') =>
  `<div class="jfb-field" data-jfb-field="${key}"><label for="${id}">Result</label>` +
  `<output class="jfb-output" id="${id}" name="${id}" aria-live="off">${text}</output></div>`;
const number = (key: string, name: string, value = '') =>
  `<div class="jfb-field" data-jfb-field="${key}"><input id="${name}" name="${name}" value="${value}"></div>`;

const FORM = `
  <form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">
    <section class="jfb-page" data-jfb-page="p1"><div class="jfb-group" data-jfb-group="g1">
      ${number('k-price', 'price')}
      ${number('k-quantity', 'quantity')}
      <div class="jfb-field" data-jfb-field="k-from"><input type="date" id="from" name="from"></div>
      <div class="jfb-field" data-jfb-field="k-to"><input type="date" id="to" name="to"></div>
      <div class="jfb-field" data-jfb-field="k-insured"><fieldset><legend>Insured?</legend>
        <input type="radio" id="insured-yes" name="insured" value="yes">
        <input type="radio" id="insured-no" name="insured" value="no" checked></fieldset></div>
      ${output('k-total', 'total')}
      ${output('k-share', 'share')}
      ${output('k-nights', 'nights')}
      <div class="jfb-field" data-jfb-field="k-cover" hidden data-jfb-hidden><output id="cover" name="cover"></output></div>
      <div class="jfb-field" data-jfb-field="k-items"><div class="jfb-repeater">
        <input type="hidden" name="jfb-rows-items" value="2">
        <fieldset class="jfb-row" data-jfb-row="0"><legend>Item 1</legend>
          ${number('k-cost', 'items[0].cost', '10')}${output('k-vat', 'items-0-vat', '2.00 VAT')}
        </fieldset>
        <fieldset class="jfb-row" data-jfb-row="1"><legend>Item 2</legend>
          ${number('k-cost', 'items[1].cost')}${output('k-vat', 'items-1-vat')}
        </fieldset>
      </div></div>
    </div></section>
  </form>`;

const formOf = (markup: string): HTMLFormElement => {
  document.body.innerHTML = markup;
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  return form;
};
const byId = (id: string): HTMLElement => {
  const element = document.getElementById(id);
  if (element === null) throw new Error(`no #${id}`);
  return element;
};
const textOf = (id: string): string => byId(id).textContent ?? '';

/** Types an answer as the visitor does, and waits for the page to follow. */
async function type(id: string, value: string): Promise<void> {
  const input = byId(id) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input', { bubbles: true }));
  await Promise.resolve();
}

async function choose(id: string): Promise<void> {
  const input = byId(id) as HTMLInputElement;
  input.checked = true;
  input.dispatchEvent(new Event('change', { bubbles: true }));
  await Promise.resolve();
}

afterEach(() => {
  document.body.innerHTML = '';
  vi.useRealTimers();
});

describe("a calculation's result", () => {
  const booking = modelOf(BOOKING);
  const attach = (form: HTMLFormElement) => rulesOf(form, booking, engineOf(booking, FORMULAS), () => NOW);

  it("follows its answers as the visitor types, in its field's format, none counting as 0", async () => {
    attach(formOf(FORM));
    expect(textOf('total')).toBe('EUR 0.00');
    await type('price', '12.5');
    expect(textOf('total')).toBe('EUR 0.00');
    await type('quantity', '3');
    expect(textOf('total')).toBe('EUR 37.50');
    expect(textOf('share')).toBe('4.17');
    await type('quantity', '1200');
    expect(textOf('total')).toBe('EUR 15,000.00');
    await type('from', '2026-11-02');
    await type('to', '2026-11-05');
    expect(textOf('nights')).toBe('3 nights');
  });

  it('shows nothing for an unreadable answer or one over its limits, a failed division or an empty date', async () => {
    attach(formOf(FORM));
    await type('price', '12.5');
    await type('quantity', '2');
    await type('from', '2026-11-02');
    await type('to', '2026-11-05');
    expect([textOf('total'), textOf('share'), textOf('nights')]).toEqual(['EUR 25.00', '6.25', '3 nights']);
    await type('price', '12,5x');
    expect(textOf('total')).toBe('');
    await type('price', '1'.repeat(5000));
    expect(textOf('total'), "an answer over its type's limits").toBe('');
    await type('price', '10');
    await type('quantity', '0');
    expect([textOf('total'), textOf('share')]).toEqual(['EUR 0.00', '']);
    await type('to', '');
    expect(textOf('nights')).toBe('');
  });

  it('shows nothing while the rules hide it, and its result once they show it', async () => {
    attach(formOf(FORM));
    await type('price', '200');
    await type('quantity', '1');
    expect(textOf('cover')).toBe('');
    await choose('insured-yes');
    expect(byId('cover').closest('[data-jfb-field]')?.hasAttribute('hidden')).toBe(false);
    expect(textOf('cover')).toBe('EUR 20.00');
    await choose('insured-no');
    expect(textOf('cover')).toBe('');
  });

  it("shows a row's result in its row, from the row's answers, none for one its type cannot read", async () => {
    attach(formOf(FORM));
    expect(textOf('items-0-vat')).toBe('2.00 VAT');
    expect(textOf('items-1-vat')).toBe('0.00 VAT');
    await type('items[1].cost', '7.5');
    expect([textOf('items-0-vat'), textOf('items-1-vat')]).toEqual(['2.00 VAT', '1.50 VAT']);
    await type('items[1].cost', '7.5 each');
    expect([textOf('items-0-vat'), textOf('items-1-vat')]).toEqual(['2.00 VAT', '']);
  });

  it('is written only when it changes, so its status says a result once', async () => {
    attach(formOf(FORM));
    await type('price', '5');
    await type('quantity', '2');
    const changes: MutationRecord[] = [];
    const observer = new MutationObserver((records) => changes.push(...records));
    observer.observe(byId('total'), { childList: true, characterData: true, subtree: true });
    await type('from', '2026-11-02');
    await type('quantity', '2.0');
    expect(changes.concat(observer.takeRecords())).toEqual([]);
    await type('quantity', '3');
    expect(changes.concat(observer.takeRecords())).not.toEqual([]);
    observer.disconnect();
    expect(textOf('total')).toBe('EUR 15.00');
  });

  it('is said once the visitor pauses, in one line, in place of its own quiet <output> (6.8b)', async () => {
    vi.useFakeTimers();
    const form = formOf(FORM);
    form.insertAdjacentHTML('beforeend', '<div data-jfb-live aria-live="polite"></div>');
    attach(form);
    const live = form.querySelector(':scope > [data-jfb-live]');
    const said: string[] = [];
    const observer = new MutationObserver(() => said.push(live?.textContent ?? ''));
    if (live !== null) observer.observe(live, { childList: true, characterData: true, subtree: true });
    await type('price', '12.5');
    for (const typed of ['1', '12', '123']) {
      await type('quantity', typed);
      // Closer than the pause each time, so a pause that is not taken up again would speak while the visitor types.
      vi.advanceTimersByTime(600);
      await Promise.resolve();
    }
    await Promise.resolve();
    expect(said, 'nothing while the visitor types, nor for the first pass').toEqual([]);
    vi.advanceTimersByTime(1000);
    await Promise.resolve();
    expect(said, 'the results that changed, once, in the order of the page').toEqual([
      'Result: EUR 1,537.50; Result: 0.10',
    ]);
    await type('from', '2026-11-02');
    vi.advanceTimersByTime(2000);
    await Promise.resolve();
    expect(said, 'an answer that changes no result says nothing').toHaveLength(1);
    observer.disconnect();
  });

  it("follows its answers on a form without rules, whose controls keep the server's state", async () => {
    const plain = modelOf(BOOKING.filter((each) => each.key !== 'k-cover'));
    const form = formOf(
      FORM.replace(
        '<input id="quantity" name="quantity" value="">',
        '<input id="quantity" name="quantity" value="4" disabled>',
      ),
    );
    const rules = rulesOf(form, plain, engineOf(plain, FORMULAS), () => NOW);
    expect(rules).not.toBeNull();
    await type('price', '2');
    expect(textOf('total')).toBe('EUR 0.00');
    expect(byId('quantity').hasAttribute('disabled')).toBe(true);
    expect(byId('cover').closest('[data-jfb-field]')?.hasAttribute('hidden')).toBe(true);
  });

  it("leaves a site's own type of that alias as the server wrote it", async () => {
    const booking = modelOf(BOOKING);
    const own = {
      ...booking,
      fields: { ...booking.fields, 'k-share': { kind: 'number', canBeRequired: true, builtIn: false } },
    };
    const form = formOf(
      FORM.replace(
        '<output class="jfb-output" id="share" name="share" aria-live="off"></output>',
        '<output id="share">By the site</output>',
      ),
    );
    rulesOf(form, own, engineOf(own, FORMULAS), () => NOW);
    await type('price', '2');
    await type('quantity', '3');
    expect([textOf('total'), textOf('share')]).toEqual(['EUR 6.00', 'By the site']);
  });
});

describe("a slider's value", () => {
  const SLIDERS = [
    field('k-hours', 'slider', 'hours', { settings: { min: '1', max: '8', step: '0.5', unit: 'hours' } }),
    field('k-seats', 'slider', 'seats', { settings: { min: '0', max: '2000', step: '100' } }),
    field('k-level', 'slider', 'level', { settings: { min: '1', max: '5', step: '1' } }),
  ];
  const sliders = modelOf(SLIDERS);
  const slider = (key: string, id: string, value: string, valueText: string | null, shown: boolean) =>
    `<div class="jfb-field" data-jfb-field="${key}"><div class="jfb-range">` +
    `<input type="range" id="${id}" name="${id}" min="0" max="2000" step="0.5" value="${value}"` +
    (valueText === null ? '' : ` aria-valuetext="${valueText}"`) +
    '>' +
    (shown ? `<output for="${id}" aria-hidden="true">${valueText ?? value}</output>` : '') +
    '</div></div>';
  const SLIDER_FORM =
    '<form id="jfb-1" data-jfb-form="f" data-jfb-contract="1">' +
    slider('k-hours', 'hours', '4.5', '4.5 hours', true) +
    slider('k-seats', 'seats', '1000', '1,000', false) +
    slider('k-level', 'level', '3', null, true) +
    '</form>';

  it('follows the thumb beside it and in its aria-valuetext, as the server writes them', async () => {
    followSliders(formOf(SLIDER_FORM), sliders);
    await type('hours', '2');
    expect(byId('hours').getAttribute('aria-valuetext')).toBe('2 hours');
    expect(byId('hours').nextElementSibling?.textContent).toBe('2 hours');
    await type('seats', '1500');
    expect(byId('seats').getAttribute('aria-valuetext')).toBe('1,500');
    await type('seats', '900');
    expect(byId('seats').hasAttribute('aria-valuetext')).toBe(false);
    await type('level', '5');
    expect(byId('level').hasAttribute('aria-valuetext')).toBe(false);
    expect(byId('level').nextElementSibling?.textContent).toBe('5');
  });

  it('shows a value the browser restored as the visitor left it', () => {
    const form = formOf(SLIDER_FORM);
    (byId('hours') as HTMLInputElement).value = '7';
    followSliders(form, sliders);
    expect(byId('hours').getAttribute('aria-valuetext')).toBe('7 hours');
    expect(byId('hours').nextElementSibling?.textContent).toBe('7 hours');
  });

  it("changes nothing the server wrote for the values it had, nor a site's own type", async () => {
    const form = formOf(SLIDER_FORM);
    const before = form.outerHTML;
    followSliders(form, sliders);
    expect(form.outerHTML).toBe(before);
    const own = formOf(SLIDER_FORM);
    byId('hours').setAttribute('aria-valuetext', 'By the site');
    const kept = own.outerHTML;
    followSliders(own, modelOf(SLIDERS, false));
    await type('hours', '6');
    expect(own.outerHTML).toBe(kept);
  });
});
