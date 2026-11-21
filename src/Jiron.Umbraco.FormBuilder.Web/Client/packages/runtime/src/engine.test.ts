// The form engine as the page runs it (ROADMAP 6.4f, ADR-0114): the calculation language loads only for a form with a
// calculation, and the merge tags only for a form with a text that recalls answers (6.4g, ADR-0115), each once for the
// page; the form chunk enhances a form once they are there, and leaves a form whose chunk cannot load to the post
// without JavaScript.
import type { Context } from '@jfb/logic';
import { FORMULAS } from '@jfb/logic/calc';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { engineOf, loadEngine, loadFormulas, loadTexts } from './engine';
import type { FormModel, ModelField, ModelMergeTags } from './model';
import { textsOf } from './recall';
import { ENGLISH } from './testing';

const NOW = '2026-10-30T09:00:00Z';

function modelOf(fields: ModelField[]): FormModel {
  return {
    alias: 'booking',
    revision: 1,
    culture: 'en-US',
    timeZone: 'Europe/London',
    open: true,
    form: { key: 'f', pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }] },
    fields: Object.fromEntries(
      fields.map((each) => [each.key, { kind: 'number', canBeRequired: true, builtIn: true }]),
    ),
    consents: {},
    ...ENGLISH,
  };
}

const COUNT: ModelField = { key: 'k-count', type: 'number', alias: 'count', settings: {} };
const TOTAL: ModelField = { key: 'k-total', type: 'calculation', alias: 'total', settings: { formula: 'count * 2' } };
const CONTEXT: Context = {
  now: NOW,
  timeZone: 'Europe/London',
  culture: 'en-US',
  member: { signedIn: false, groups: [] },
  query: {},
  hidden: [],
};

const FORM =
  '<form id="jfb-1" data-jfb-form="f" data-jfb-contract="1"><section data-jfb-page="p1"><div data-jfb-group="g1">' +
  '<div data-jfb-field="k-count"><input id="count" name="count" value="4"></div>' +
  '<div data-jfb-field="k-total"><output id="total" name="total"></output></div></div></section></form>';

/** The count's default takes the query value `n`, which the form allows, and the server marked it for the page. */
const MARKED = FORM.replace(
  '<div data-jfb-field="k-count"><input id="count" name="count" value="4">',
  '<div data-jfb-field="k-count" data-jfb-default><input id="count" name="count">',
);
/** A model of these fields whose count takes the query value `n` as its default. */
function defaultedOf(...more: ModelField[]): FormModel {
  const model = modelOf([{ ...COUNT, defaultValue: '{{ query.n }}' }, ...more]);
  return {
    ...model,
    form: { ...model.form, settings: { queryValues: ['n'] } },
    mergeTags: {
      texts: [{ owner: 'k-count', text: 'defaultValue', context: 'text' }],
      values: {},
      recall: { pages: [] },
    },
  };
}
const DEFAULTED = defaultedOf(TOTAL);

const NOTE: ModelField = {
  key: 'k-note',
  type: 'text',
  alias: 'note',
  label: 'Notes for {{ field.count }}',
  settings: {},
};

/** Merge tags whose note's label recalls the count, or names no field when `recalled` is false. */
const tagsOf = (recalled = true): ModelMergeTags => ({
  texts: [{ owner: 'k-note', text: 'label', context: 'text' }],
  values: {},
  recall: {
    pages: [
      {
        id: 'p1',
        groups: [
          {
            id: 'g1',
            fields: [
              ...(recalled
                ? [
                    {
                      id: 'k-count',
                      type: 'number',
                      holdsValue: true,
                      builtIn: true,
                      kind: 'number' as const,
                      alias: 'count',
                    },
                  ]
                : []),
              { id: 'k-note', type: 'text', holdsValue: true },
            ],
          },
        ],
      },
    ],
  },
});

const RECALLING =
  '<form id="jfb-1" data-jfb-form="f" data-jfb-contract="1"><section data-jfb-page="p1"><div data-jfb-group="g1">' +
  '<div data-jfb-field="k-count"><input id="count" name="count" value="4"></div>' +
  '<div data-jfb-field="k-note"><label for="note"><span id="label" data-jfb-text="k-note label">Notes for 4</span></label>' +
  '<input id="note" name="note"></div></div></section></form>';

afterEach(() => {
  document.body.innerHTML = '';
  vi.doUnmock('@jfb/logic/calc');
  vi.doUnmock('./recall');
  vi.resetModules();
});

describe("a form's engine", () => {
  it('loads the calculation language for a form with a calculation, and decides with it', async () => {
    const load = vi.fn(() => Promise.resolve(FORMULAS));
    const engine = await loadEngine(modelOf([COUNT, TOTAL]), load);
    expect(load).toHaveBeenCalledOnce();
    expect(engine.calculates).toBe(true);
    expect(engine.decide({ values: { 'k-count': ['4'] } }, CONTEXT).calculations).toEqual({ 'k-total': '8' });
  });

  it('never loads it for a form without a calculation', async () => {
    const load = vi.fn(() => Promise.resolve(FORMULAS));
    const engine = await loadEngine(modelOf([COUNT]), load);
    expect(load).not.toHaveBeenCalled();
    expect(engine.calculates).toBe(false);
    expect(engine.decide({ values: { 'k-count': ['4'] } }, CONTEXT).calculations).toEqual({});
  });

  it('fails as the language does, so the form is left to the post', async () => {
    const offline = new Error('offline');
    await expect(loadEngine(modelOf([COUNT, TOTAL]), () => Promise.reject(offline))).rejects.toBe(offline);
  });

  it('imports the language once for the page', async () => {
    const first = loadFormulas();
    expect(loadFormulas()).toBe(first);
    expect(Object.keys(await first)).toEqual(['parse', 'check', 'calculate']);
  });

  it('loads the merge tags for a form with a text that recalls answers, and renders it with them', async () => {
    const texts = vi.fn(() => Promise.resolve(textsOf));
    const load = vi.fn(() => Promise.resolve(FORMULAS));
    const engine = await loadEngine({ ...modelOf([COUNT, NOTE]), mergeTags: tagsOf() }, load, texts);
    expect(texts).toHaveBeenCalledOnce();
    expect(load, 'no calculation').not.toHaveBeenCalled();
    expect(engine.texts).not.toBeNull();
  });

  it('never loads them for a form without merge tags, or whose texts recall no answer', async () => {
    const texts = vi.fn(() => Promise.resolve(textsOf));
    expect((await loadEngine(modelOf([COUNT, NOTE]), undefined, texts)).texts).toBeNull();
    expect((await loadEngine({ ...modelOf([COUNT, NOTE]), mergeTags: null }, undefined, texts)).texts).toBeNull();
    expect(
      (await loadEngine({ ...modelOf([COUNT, NOTE]), mergeTags: tagsOf(false) }, undefined, texts)).texts,
    ).toBeNull();
    expect(texts).not.toHaveBeenCalled();
  });

  it('fails as the merge tags do, so the form is left to the post', async () => {
    const offline = new Error('offline');
    await expect(
      loadEngine({ ...modelOf([COUNT, NOTE]), mergeTags: tagsOf() }, undefined, () => Promise.reject(offline)),
    ).rejects.toBe(offline);
  });

  it('imports the merge tags once for the page', async () => {
    const first = loadTexts();
    expect(loadTexts()).toBe(first);
    expect(typeof (await first)).toBe('function');
  });

  it("decides a form without a calculation without the language, and refuses to guess one's result", () => {
    expect(engineOf(modelOf([COUNT]), null).decide({}, CONTEXT).calculations).toEqual({});
    expect(() => engineOf(modelOf([COUNT, TOTAL]), null).decide({}, CONTEXT)).toThrow(
      'A form with a calculation needs the calculation language.',
    );
  });
});

describe('the form chunk', () => {
  it('enhances a form with a calculation once its language is there, its result shown at once', async () => {
    document.body.innerHTML = FORM;
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const { attach } = await import('./form');
    const session = await attach(form, modelOf([COUNT, TOTAL]));
    expect(session?.engine.calculates).toBe(true);
    expect(session?.rules).not.toBeNull();
    expect(document.getElementById('total')?.textContent).toBe('8.00');
  });

  it("tells save and continue later of a form that came in another's place, which holds that one's answers", async () => {
    const enhanceResume = vi.fn();
    vi.doMock('./resume-chunk', () => ({ enhanceResume }));
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response('{}', { status: 200 }))),
    );
    const { attach } = await import('./form');
    const model = modelOf([COUNT]);
    const offering = { ...model, form: { ...model.form, settings: { resume: true } } };
    const woken = async (carried?: { id: string; challenge: string | null }): Promise<unknown> => {
      document.body.innerHTML = FORM.replace('data-jfb-form="f"', 'data-jfb-form="f" data-jfb-api="/jfb/api/v1"');
      const form = document.querySelector('form');
      if (form === null) throw new Error('no form');
      enhanceResume.mockClear();
      expect(await attach(form, offering, undefined, undefined, carried)).not.toBeNull();
      return (enhanceResume.mock.calls[0]?.[2] as { carried?: boolean } | undefined)?.carried;
    };
    expect([await woken(), await woken({ id: 'id-1', challenge: null })]).toEqual([undefined, true]);
    vi.doUnmock('./resume-chunk');
    vi.unstubAllGlobals();
  });

  it('hands the errors what the visitor answered and left before it woke, so that field is checked as it wakes', async () => {
    document.body.innerHTML = FORM;
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const count = document.getElementById('count') as HTMLInputElement;
    const heard: Event[] = [];
    for (const kind of ['input', 'focusout']) form.addEventListener(kind, (event) => heard.push(event));
    count.value = '';
    count.dispatchEvent(new Event('input', { bubbles: true }));
    count.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
    const { attach } = await import('./form');
    expect(await attach(form, modelOf([{ ...COUNT, required: true }]), heard)).not.toBeNull();
    await vi.waitFor(() => expect(form.querySelector('[data-jfb-field="k-count"] [data-jfb-error]')).not.toBeNull());
  });

  it("keeps a slider's value with its thumb, on a form without a calculation too", async () => {
    document.body.innerHTML =
      '<form id="jfb-1" data-jfb-form="f" data-jfb-contract="1"><section data-jfb-page="p1"><div data-jfb-group="g1">' +
      '<div data-jfb-field="k-hours"><input type="range" id="hours" name="hours" min="1" max="8" value="4">' +
      '<output for="hours" aria-hidden="true">4 hours</output></div></div></section></form>';
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const { attach } = await import('./form');
    const hours: ModelField = { key: 'k-hours', type: 'slider', alias: 'hours', settings: { unit: 'hours' } };
    expect(await attach(form, modelOf([hours]))).not.toBeNull();
    const slider = document.getElementById('hours') as HTMLInputElement;
    slider.value = '6';
    slider.dispatchEvent(new Event('input', { bubbles: true }));
    expect(slider.getAttribute('aria-valuetext')).toBe('6 hours');
    expect(form.querySelector('output')?.textContent).toBe('6 hours');
  });

  it('enhances a form whose only feature is a text that recalls answers, which then follows them', async () => {
    document.body.innerHTML = RECALLING;
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const { attach } = await import('./form');
    const session = await attach(form, { ...modelOf([COUNT, NOTE]), mergeTags: tagsOf() });
    expect(session?.rules).not.toBeNull();
    const count = document.getElementById('count') as HTMLInputElement;
    count.value = '12';
    count.dispatchEvent(new Event('input', { bubbles: true }));
    await Promise.resolve();
    expect(document.getElementById('label')?.textContent).toBe('Notes for 12');
  });

  it('leaves a form whose merge tags cannot load to the post without JavaScript', async () => {
    vi.doMock('./recall', () => {
      throw new Error('offline');
    });
    document.body.innerHTML = RECALLING;
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const { attach } = await import('./form');
    expect(await attach(form, { ...modelOf([COUNT, NOTE]), mergeTags: tagsOf() })).toBeNull();
  });

  it('fills in a default the server marked before the rules first decide, so a calculation counts it at once', async () => {
    window.history.replaceState(null, '', '/?n=5&other=9');
    try {
      document.body.innerHTML = MARKED;
      const form = document.querySelector('form');
      if (form === null) throw new Error('no form');
      const { attach } = await import('./form');
      const session = await attach(form, DEFAULTED);
      expect(session).not.toBeNull();
      expect((document.getElementById('count') as HTMLInputElement).value).toBe('5');
      expect(document.getElementById('total')?.textContent).toBe('10.00');
      expect(form.querySelector('[data-jfb-default]'), 'filled once').toBeNull();
    } finally {
      window.history.replaceState(null, '', '/');
    }
  });

  it('loads the filler of defaults only for a form the server marked, once for the page', async () => {
    document.body.innerHTML = FORM;
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const { attach, loadDefaults } = await import('./form');
    const filler = vi.fn(loadDefaults);
    expect(await attach(form, DEFAULTED, [], filler)).not.toBeNull();
    expect(filler).not.toHaveBeenCalled();
    const once = loadDefaults();
    expect(loadDefaults()).toBe(once);
    await once;
  });

  it('leaves a form whose filler of defaults cannot load to the post without JavaScript', async () => {
    document.body.innerHTML = MARKED;
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const { attach } = await import('./form');
    // Without a calculation, so no import of the language outlives the test.
    expect(await attach(form, defaultedOf(), [], () => Promise.reject(new Error('offline')))).toBeNull();
    expect((document.getElementById('count') as HTMLInputElement).value).toBe('');
  });

  it('leaves a form whose calculation language cannot load to the post without JavaScript', async () => {
    vi.doMock('@jfb/logic/calc', () => {
      throw new Error('offline');
    });
    document.body.innerHTML = FORM;
    const form = document.querySelector('form');
    if (form === null) throw new Error('no form');
    const { attach } = await import('./form');
    expect(await attach(form, modelOf([COUNT, TOTAL]))).toBeNull();
    expect(document.getElementById('total')?.textContent).toBe('');
  });
});
