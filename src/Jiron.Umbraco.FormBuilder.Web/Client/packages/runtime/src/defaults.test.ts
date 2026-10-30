// The default values that hold merge tags, filled in as the form is shown (ROADMAP 6.4h, ADR-0118): only the fields
// the server marked, once, from the query values the form allows and the endpoint's values, a refused tag written as it
// stands and a default the endpoint does not list as it is written; never a control the visitor changed already.
import type { Context } from '@jfb/logic';
import { afterEach, describe, expect, it } from 'vitest';
import { fillDefaults } from './defaults';
import type { FormModel, ModelField, ModelText } from './model';
import { ENGLISH } from './testing';

const context = (query: Record<string, string[]> = {}): Context => ({
  now: '2026-10-30T09:00:00Z',
  timeZone: 'Europe/London',
  culture: 'en-US',
  member: { signedIn: false, groups: [] },
  query,
  hidden: [],
});

/** A model of these fields, whose merge tags list the given defaults, with the endpoint's values for `site.name`. */
function modelOf(fields: ModelField[], listed: ModelText[]): FormModel {
  return {
    alias: 'booking',
    revision: 1,
    culture: 'en-US',
    timeZone: 'Europe/London',
    open: true,
    form: { key: 'f', pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }] },
    fields: {},
    consents: {},
    mergeTags: { texts: listed, values: { text: { 'site.name': 'Library' } }, recall: { pages: [] } },
    ...ENGLISH,
  };
}

const listed = (owner: string, refused?: string[]): ModelText => ({
  owner,
  text: 'defaultValue',
  context: 'text',
  ...(refused === undefined ? {} : { refused }),
});

const field = (key: string, type: string, defaultValue: string | string[]): ModelField => ({
  key,
  type,
  alias: key,
  settings: {},
  defaultValue,
});

/** A form of these fields' markup, each field's element marked unless `marked` leaves it out. */
function formOf(fields: Record<string, string>, marked = Object.keys(fields)): HTMLFormElement {
  document.body.innerHTML =
    '<form data-jfb-form="f" data-jfb-contract="1">' +
    Object.entries(fields)
      .map(
        ([key, controls]) =>
          `<div data-jfb-field="${key}"${marked.includes(key) ? ' data-jfb-default' : ''}>${controls}</div>`,
      )
      .join('') +
    '</form>';
  const form = document.querySelector('form');
  if (form === null) throw new Error('no form');
  return form;
}

const value = (name: string): string => (document.querySelector(`[name="${name}"]`) as HTMLInputElement).value;
const ticked = (name: string): string[] =>
  [...document.querySelectorAll<HTMLInputElement>(`[name="${name}"]`)]
    .filter((box) => box.checked)
    .map((box) => box.value);

afterEach(() => {
  document.body.replaceChildren();
});

describe('a default value with merge tags', () => {
  it('fills in the query values the form allows and the endpoint gives the rest, then drops the mark', () => {
    const form = formOf({ source: '<input name="source">', topic: '<input name="topic">' });
    const model = modelOf(
      [
        field('source', 'text', '{{ query.ref }} via {{ site.name }}'),
        field('topic', 'text', '{{ query.topic | join: ", " }}'),
      ],
      [listed('source'), listed('topic')],
    );
    fillDefaults(form, model, context({ ref: ['mail'], topic: ['news', 'events'] }));
    expect([value('source'), value('topic')]).toEqual(['mail via Library', 'news, events']);
    expect(form.querySelector('[data-jfb-default]')).toBeNull();
  });

  it('renders a query value the form does not allow, or the address lacks, as the empty text', () => {
    const form = formOf({ source: '<input name="source">' });
    const model = modelOf([field('source', 'text', '{{ query.ref | default: "web" }}')], [listed('source')]);
    fillDefaults(form, model, context({ other: ['mail'] }));
    expect(value('source')).toBe('web');
  });

  it('writes a tag its place refuses as it stands, and a default the endpoint does not list as it is written', () => {
    const form = formOf({ mixed: '<input name="mixed">', plain: '<input name="plain">' });
    const model = modelOf(
      [field('mixed', 'text', '{{ site.name }} for {{ member.email }}'), field('plain', 'text', '{{ member.name }}')],
      [listed('mixed', ['member.email'])],
    );
    fillDefaults(form, model, context());
    expect([value('mixed'), value('plain')]).toEqual(['Library for {{ member.email }}', '{{ member.name }}']);
  });

  it('ticks the choices whose values it renders: a radio, boxes from a list, a single and a multiple select', () => {
    const form = formOf({
      plan: '<input type="radio" name="plan" value="day"><input type="radio" name="plan" value="stay">',
      topics:
        '<input type="checkbox" name="topics" value="news"><input type="checkbox" name="topics" value="events">' +
        '<input type="checkbox" name="topics" value="offers">',
      room:
        '<select name="room"><option value="">Choose</option><option value="hall">Hall</option>' +
        '<option value="study">Study</option></select>',
      days:
        '<select name="days" multiple><option value="mon">Mon</option><option value="tue">Tue</option>' +
        '<option value="wed">Wed</option></select>',
    });
    const model = modelOf(
      [
        field('plan', 'radio', '{{ query.plan }}'),
        field('topics', 'checkboxes', ['{{ query.topic }}', 'news']),
        field('room', 'dropdown', '{{ query.room }}'),
        field('days', 'dropdown', ['{{ query.day }}', 'wed']),
      ],
      [listed('plan'), listed('topics'), listed('room'), listed('days')],
    );
    fillDefaults(form, model, context({ plan: ['stay'], topic: ['events'], room: ['study'], day: ['mon'] }));
    const days = document.querySelector<HTMLSelectElement>('[name="days"]');
    expect([ticked('plan'), ticked('topics'), value('room')]).toEqual([['stay'], ['news', 'events'], 'study']);
    expect([...(days?.selectedOptions ?? [])].map((option) => option.value)).toEqual(['mon', 'wed']);
  });

  it('leaves a choice whose rendering matches no option, and a field whose default renders empty, as they are', () => {
    const form = formOf({
      room: '<select name="room"><option value="">Choose</option><option value="hall">Hall</option></select>',
      days: '<select name="days" multiple><option value="mon" selected>Mon</option><option value="tue">Tue</option></select>',
      source: '<input name="source" value="">',
    });
    const model = modelOf(
      [
        field('room', 'dropdown', '{{ query.room }}'),
        field('days', 'dropdown', ['{{ query.day }}']),
        field('source', 'text', '{{ query.ref }}'),
      ],
      [listed('room'), listed('days'), listed('source')],
    );
    fillDefaults(form, model, context({ room: ['attic'], day: ['fri'] }));
    const days = document.querySelector<HTMLSelectElement>('[name="days"]');
    expect([value('room'), value('source')]).toEqual(['', '']);
    expect([...(days?.selectedOptions ?? [])].map((option) => option.value)).toEqual(['mon']);
  });

  it("never fills a control the visitor changed before the default was there, nor its field's other controls", () => {
    const form = formOf({
      source: '<input name="source">',
      plan: '<input type="radio" name="plan" value="day"><input type="radio" name="plan" value="stay">',
      room: '<select name="room"><option value="">Choose</option><option value="hall">Hall</option></select>',
    });
    (document.querySelector('[name="source"]') as HTMLInputElement).value = 'typed';
    (document.querySelector('[name="plan"][value="day"]') as HTMLInputElement).checked = true;
    (document.querySelector('[name="room"]') as HTMLSelectElement).value = 'hall';
    const model = modelOf(
      [
        field('source', 'text', '{{ site.name }}'),
        field('plan', 'radio', 'stay'),
        field('room', 'dropdown', '{{ query.room }}'),
      ],
      [listed('source'), listed('plan'), listed('room')],
    );
    fillDefaults(form, model, context({ room: ['hall'] }));
    expect([value('source'), ticked('plan'), value('room')]).toEqual(['typed', ['day'], 'hall']);
  });

  it("fills only the fields the server marked, a row's by its child, and never a hidden input", () => {
    const form = formOf(
      {
        kid: '<input name="kids[1].kid"><input type="hidden" name="jfb-order-kid" value="">',
        other: '<input name="other">',
      },
      ['kid'],
    );
    const kids: ModelField = {
      key: 'kids',
      type: 'repeater',
      alias: 'kids',
      settings: {},
      fields: [field('kid', 'text', '{{ site.name }} guest')],
    };
    const model = modelOf([kids, field('other', 'text', '{{ site.name }}')], [listed('kid'), listed('other')]);
    fillDefaults(form, model, context());
    expect([value('kids[1].kid'), value('jfb-order-kid'), value('other')]).toEqual(['Library guest', '', '']);
  });

  it('fills a field once: a second pass finds no mark', () => {
    const form = formOf({ source: '<input name="source">' });
    const model = modelOf([field('source', 'text', '{{ site.name }}')], [listed('source')]);
    fillDefaults(form, model, context());
    (document.querySelector('[name="source"]') as HTMLInputElement).value = '';
    fillDefaults(form, model, context());
    expect(value('source')).toBe('');
  });
});

describe('a field prefilled from the query (ADR-0124)', () => {
  const prefilled = (key: string, type: string, query: string, defaultValue: string | string[] = []): ModelField => ({
    ...field(key, type, defaultValue),
    prefill: { query },
  });

  it("takes the address's value of its query name over its default, and the default where the address has none", () => {
    const form = formOf({ source: '<input name="source">', topic: '<input name="topic">' });
    const model = modelOf(
      [prefilled('source', 'text', 'ref', '{{ site.name }}'), prefilled('topic', 'text', 'topic', '{{ site.name }}')],
      [listed('source'), listed('topic')],
    );
    fillDefaults(form, model, context({ ref: ['mail'] }));
    expect([value('source'), value('topic')]).toEqual(['mail', 'Library']);
    expect(form.querySelector('[data-jfb-default]')).toBeNull();
  });

  it('ticks the choices of several values, and leaves an empty value to the default', () => {
    const form = formOf({
      topics: '<input type="checkbox" name="topics" value="news"><input type="checkbox" name="topics" value="events">',
      source: '<input name="source">',
    });
    const model = modelOf(
      [prefilled('topics', 'checkboxList', 'topic'), prefilled('source', 'text', 'ref', 'web')],
      [],
    );
    fillDefaults(form, model, context({ topic: ['news', 'events'], ref: [''] }));
    expect([ticked('topics'), value('source')]).toEqual([['news', 'events'], 'web']);
  });

  it('reads only its own query name, so a value the form does not allow never reaches it', () => {
    const form = formOf({ source: '<input name="source">', shape: '<input name="shape">' });
    const model = modelOf([prefilled('source', 'text', 'ref'), prefilled('shape', 'text', 'constructor')], []);
    fillDefaults(form, model, context({ other: ['mail'] }));
    expect([value('source'), value('shape')]).toEqual(['', '']);
  });

  it('never fills a control the visitor changed already', () => {
    const form = formOf({ source: '<input name="source">' });
    (document.querySelector('[name="source"]') as HTMLInputElement).value = 'own';
    fillDefaults(form, modelOf([prefilled('source', 'text', 'ref')], []), context({ ref: ['mail'] }));
    expect(value('source')).toBe('own');
  });
});
