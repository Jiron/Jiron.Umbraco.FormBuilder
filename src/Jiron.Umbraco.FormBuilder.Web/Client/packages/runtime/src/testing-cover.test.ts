/**
 * The cover of the forms the tests make (testing-cover.ts), held to what it must do, so that it cannot stop covering
 * unseen: a read by the website code meets a control and a write by it throws, as on the page, while the test's own read
 * and a read through `proto(form)` get the form's own member. The website code here is a function whose source the
 * engine names as a file of the runtime's (`//# sourceURL=`), so its frame is the one a module of the runtime leaves,
 * and the cover tells it apart by nothing else.
 */
import { proto } from '@jfb/logic/proto';
import { afterEach, describe, expect, it } from 'vitest';

/** A function of the website code: its frame carries a path of the runtime's sources. */
function website<T>(body: string): (form: HTMLFormElement) => T {
  const run = new Function('form', 'proto', `${body}\n//# sourceURL=/packages/runtime/src/cover-probe.ts`) as (
    form: HTMLFormElement,
    view: typeof proto,
  ) => T;
  return (form) => run(form, proto);
}

/** A form as the tests make one: written as markup into the document, so the cover takes it on the way in. */
function covered(): HTMLFormElement {
  document.body.innerHTML = '<form id="jfb-1" data-jfb-form="f"><input name="note"></form>';
  return document.querySelector('form') as HTMLFormElement;
}

afterEach(() => {
  document.body.replaceChildren();
});

describe('the cover of a form the tests make', () => {
  it('answers a plain read by the website code with a control of that name', () => {
    const form = covered();
    const read = website<unknown>('return form.dataset;')(form);
    expect(read).toBeInstanceOf(HTMLInputElement);
    expect((read as HTMLInputElement).name).toBe('dataset');
    expect(website<unknown>('return form.id;')(form)).toBeInstanceOf(HTMLInputElement);
  });

  it('refuses a plain write by the website code, as the browser does', () => {
    const form = covered();
    expect(() => website<unknown>('form.id = "jfb-2";')(form)).toThrow(TypeError);
    expect(form.getAttribute('id')).toBe('jfb-1');
  });

  it("gives the test's own read the form's own member", () => {
    const form = covered();
    expect(Object.getOwnPropertyDescriptor(form, 'dataset')?.get, 'the form is covered').toBeTypeOf('function');
    expect(form.id).toBe('jfb-1');
    expect(form.dataset['jfbForm']).toBe('f');
  });

  it("gives a read through proto(form) by the website code the form's own member", () => {
    const form = covered();
    expect(website<unknown>('return [proto(form).id, proto(form).dataset.jfbForm];')(form)).toEqual(['jfb-1', 'f']);
  });
});
