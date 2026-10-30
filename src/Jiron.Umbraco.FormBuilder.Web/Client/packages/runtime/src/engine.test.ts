// The form engine as the page runs it (ROADMAP 6.4f, ADR-0114): the calculation language loads only for a form with a
// calculation, once for the page; the form chunk enhances a form once its language is there, and leaves a form whose
// language cannot load to the post without JavaScript.
import type { Context } from '@jfb/logic';
import { FORMULAS } from '@jfb/logic/calc';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { engineOf, loadEngine, loadFormulas } from './engine';
import type { FormModel, ModelField } from './model';
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

afterEach(() => {
  document.body.innerHTML = '';
  vi.doUnmock('@jfb/logic/calc');
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
