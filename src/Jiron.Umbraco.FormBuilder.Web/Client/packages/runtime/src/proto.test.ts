/**
 * The view of an element's members through its prototype chain (@jfb/logic/proto), which the website code reads every
 * member of a form through: a browser answers a plain read on a form with its control of that name. Its tests live here,
 * where there is a DOM, as the logic's have none.
 */
import { proto } from '@jfb/logic/proto';
import { afterEach, describe, expect, it } from 'vitest';
import { stubMembers } from './testing-cover';

afterEach(() => {
  document.body.replaceChildren();
});

/** A form whose instance answers the names a browser would answer with its controls, as own members do. */
function shadowed(): { form: HTMLFormElement; control: HTMLInputElement } {
  document.body.innerHTML =
    '<form id="jfb-1" class="jfb-form" action="/sent" data-jfb-api="/jfb/api/v1"><input name="note"></form>';
  const form = document.querySelector('form') as HTMLFormElement;
  const control = document.createElement('input');
  for (const name of ['id', 'action', 'dataset', 'classList', 'querySelector', 'dispatchEvent', 'hidden', 'noValidate'])
    Object.defineProperty(form, name, { configurable: true, value: control });
  return { form, control };
}

describe('the view of an element through its prototype chain', () => {
  it('reads what the getters answer for the element, past the members of its own', () => {
    const { form, control } = shadowed();
    expect(form.id, 'a plain read meets the shadow').toBe(control);

    expect(proto(form).id).toBe('jfb-1');
    expect(proto(form).action).toBe(new URL('/sent', document.baseURI).href);
    expect(proto(form).dataset['jfbApi']).toBe('/jfb/api/v1');
    expect([...proto(form).classList]).toEqual(['jfb-form']);
  });

  it('writes through the setters to the element', () => {
    const { form } = shadowed();

    proto(form).id = 'jfb-2';
    proto(form).hidden = true;
    proto(form).noValidate = true;

    expect(form.getAttribute('id')).toBe('jfb-2');
    expect(form.hasAttribute('hidden')).toBe(true);
    expect(form.hasAttribute('novalidate')).toBe(true);
  });

  it('gives the methods bound to the element, so the browser takes them as its own', () => {
    const { form } = shadowed();
    const heard: string[] = [];
    form.addEventListener('jfb:ready', (event) => heard.push(event.type));

    const { querySelector, dispatchEvent } = proto(form);

    expect(querySelector('input')?.getAttribute('name')).toBe('note');
    expect(dispatchEvent(new Event('jfb:ready'))).toBe(true);
    expect(heard).toEqual(['jfb:ready']);
  });

  it('answers `in` from the prototype chain', () => {
    const { form } = shadowed();
    Object.defineProperty(form, 'note', { configurable: true, value: document.createElement('input') });

    expect('note' in form).toBe(true);
    expect('note' in proto(form)).toBe(false);
    expect('requestSubmit' in proto(form)).toBe(true);
  });

  it('reads any other element as a plain read does', () => {
    const div = document.createElement('div');
    div.id = 'box';
    div.append(document.createElement('span'));

    expect(proto(div).id).toBe('box');
    expect(proto(div).querySelector('span')).toBe(div.firstElementChild);
    expect(proto(document).getElementById('nothing')).toBeNull();
  });

  it('is the same view each time for the same element, and another for another', () => {
    const one = document.createElement('form');
    const other = document.createElement('form');

    expect(proto(one)).toBe(proto(one));
    expect(proto(one)).not.toBe(proto(other));
  });

  it('reads a link of the chain added after the view was made, as the tests stub members', () => {
    const form = document.createElement('form');
    const view = proto(form);
    const sent: string[] = [];

    const released = stubMembers(form, { requestSubmit: () => sent.push('stubbed') });
    view.requestSubmit();
    released();

    expect(sent).toEqual(['stubbed']);
    expect(Object.getPrototypeOf(form)).toBe(HTMLFormElement.prototype);
  });
});
