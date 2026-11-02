// The field behaviours (ROADMAP 6.4o, ADR-0154): a repeatable group's rows added and removed in place, a ranking moved by its
// buttons, a counter, the "Other" text, a text area that grows, and a cascading field's options; each leaves the page as a post
// would render it.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { enhanceFields } from './fields-chunk';
import type { FormModel, ModelField } from './model';
import { ENGLISH } from './testing';

const modelOf = (fields: ModelField[]): FormModel => ({
  alias: 'hire',
  revision: 1,
  culture: 'en-US',
  timeZone: 'UTC',
  open: true,
  form: { key: 'f', settings: {}, pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }] },
  fields: Object.fromEntries(
    fields
      .flatMap((each) => [each, ...(each.fields ?? [])])
      .map((each) => [each.key, { kind: 'text', canBeRequired: true, builtIn: true }]),
  ),
  consents: {},
  ...ENGLISH,
});

const NAME: ModelField = { key: 'k-name', type: 'text', alias: 'name', settings: {} };
const GUESTS: ModelField = { key: 'k-guests', type: 'repeater', alias: 'guests', settings: {}, fields: [NAME] };

const rowOf = (row: number, hidden = false): string =>
  `<fieldset class="jfb-row" id="jfb-1-guests-${String(row)}" data-jfb-row="${String(row)}"><legend>Guest ${String(row + 1)}</legend>` +
  `<button type="submit" name="jfb-nav" value="remove:guests:${String(row)}"${hidden ? ' hidden' : ''}>Remove<span class="jfb-visually-hidden"> Guest ${String(row + 1)}</span></button>` +
  `<div data-jfb-field="k-name" data-jfb-alias="guests[${String(row)}].name"><label for="jfb-1-guests-${String(row)}-name">Name</label>` +
  `<input id="jfb-1-guests-${String(row)}-name" name="guests[${String(row)}].name" aria-describedby="jfb-1-guests-${String(row)}-name-count"></div></fieldset>`;

const REPEATER = (rows: number, least = 1, most = 3, addHidden = false): string =>
  `<div data-jfb-field="k-guests" data-jfb-alias="guests"><fieldset><legend>Guests</legend><div class="jfb-repeater" id="jfb-1-guests" tabindex="-1" ` +
  `data-jfb-least="${String(least)}" data-jfb-most="${String(most)}" data-jfb-legend="Guest {0}" data-jfb-removed="{0} removed.">` +
  `<input type="hidden" name="jfb-rows-guests" value="${String(rows)}">` +
  Array.from({ length: rows }, (_, row) => rowOf(row, rows <= least)).join('') +
  `<template>${rowOf(rows)}</template>` +
  `<button type="submit" name="jfb-nav" value="add:guests"${addHidden ? ' hidden' : ''}>Add a row</button></div></fieldset></div>`;

function wake(fields: ModelField[], html: string, api: string | null = '/jfb/api/v1') {
  document.body.innerHTML = `<form id="jfb-1" data-jfb-form="f" data-jfb-content="c1">${html}<div data-jfb-live></div></form>`;
  const form = document.querySelector('form') as HTMLFormElement;
  const added = vi.fn();
  const heard = vi.fn();
  form.addEventListener('input', heard);
  form.addEventListener('change', heard);
  enhanceFields(form, modelOf(fields), { api: api ?? undefined, added });
  return {
    form,
    added,
    heard,
    live: (): string => form.querySelector('[data-jfb-live]')?.textContent ?? '',
    press: (value: string): void => {
      form.querySelector<HTMLElement>(`button[value="${value}"]`)?.click();
    },
  };
}

const until = (check: () => void): Promise<void> => vi.waitFor(check, { timeout: 2000, interval: 2 });

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  document.body.innerHTML = '';
});

describe('a row added in place', () => {
  const names = (form: HTMLFormElement): string[] =>
    [...form.querySelectorAll<HTMLInputElement>('.jfb-repeater > fieldset input')].map((each) => each.name);

  it('comes from the template at its place, its names and ids as the server writes them', () => {
    const { form, press, added, heard } = wake([GUESTS], REPEATER(1));
    press('add:guests');

    const row = form.querySelector<HTMLElement>('[data-jfb-row="1"]') as HTMLElement;
    expect(row.id).toBe('jfb-1-guests-1');
    expect(row.querySelector('legend')?.textContent).toBe('Guest 2');
    expect(row.querySelector('input')?.id).toBe('jfb-1-guests-1-name');
    expect(row.querySelector('label')?.getAttribute('for')).toBe('jfb-1-guests-1-name');
    expect(row.querySelector('input')?.getAttribute('aria-describedby')).toBe('jfb-1-guests-1-name-count');
    expect(row.querySelector('[data-jfb-field]')?.getAttribute('data-jfb-alias')).toBe('guests[1].name');
    expect(row.querySelector('button')?.value).toBe('remove:guests:1');
    expect(names(form)).toEqual(['guests[0].name', 'guests[1].name']);
    expect(form.querySelector<HTMLInputElement>('[name="jfb-rows-guests"]')?.value).toBe('2');
    expect(added).toHaveBeenCalledWith(row);
    expect(heard).toHaveBeenCalled();
  });

  it('takes the focus to its first control, after the last row and before Add', async () => {
    const { form, press } = wake([GUESTS], REPEATER(1));
    press('add:guests');

    await until(() => expect(document.activeElement?.id).toBe('jfb-1-guests-1-name'));
    expect(form.querySelector('[value="add:guests"]')?.previousElementSibling?.localName).toBe('template');
    expect(form.querySelector('.jfb-repeater > template')?.previousElementSibling?.getAttribute('data-jfb-row')).toBe(
      '1',
    );
    const rows = [...form.querySelectorAll('.jfb-repeater > fieldset')];
    expect(rows.map((each) => each.getAttribute('data-jfb-row'))).toEqual(['0', '1']);
    expect(rows[1]?.nextElementSibling?.localName).toBe('template');
  });

  it('takes the focus past what the rules turn off and what does not show', async () => {
    const { form, press } = wake(
      [GUESTS],
      REPEATER(0, 0, 3).replace(
        '</fieldset></template>',
        '<div hidden><input id="gone"></div><input id="late"></fieldset></template>',
      ),
    );
    form.addEventListener('input', () => {
      form
        .querySelector<HTMLInputElement>('.jfb-repeater > fieldset [name="guests[0].name"]')
        ?.toggleAttribute('disabled', true);
    });
    press('add:guests');

    await until(() => expect(document.activeElement?.id).toBe('late'));
  });

  it('names what the templates of its row hold, for the row added next as well', () => {
    const nested = (row: number): string =>
      `<template data-jfb-cascade><input id="jfb-1-guests-${String(row)}-x" name="guests[${String(row)}].x"></template>`;
    const { form, press } = wake(
      [GUESTS],
      REPEATER(1, 0, 3).replace('</fieldset></template>', `${nested(1)}</fieldset></template>`),
    );
    const held = (row: number): HTMLInputElement | null | undefined =>
      form
        .querySelector<HTMLTemplateElement>(`[data-jfb-row="${String(row)}"] template`)
        ?.content.querySelector('input');
    press('add:guests');
    press('add:guests');

    expect(held(1)?.name).toBe('guests[1].x');
    expect(held(2)?.name).toBe('guests[2].x');
    expect(held(2)?.id).toBe('jfb-1-guests-2-x');
  });

  it('shows Remove once the group has more than its least, and hides Add at its most', () => {
    const { form, press } = wake([GUESTS], REPEATER(1, 1, 2));
    const remove = (row: number): HTMLElement =>
      form.querySelector(`[value="remove:guests:${String(row)}"]`) as HTMLElement;
    expect(remove(0).hidden).toBe(true);
    press('add:guests');

    expect(remove(0).hidden).toBe(false);
    expect(remove(1).hidden).toBe(false);
    expect(form.querySelector<HTMLElement>('[value="add:guests"]')?.hidden).toBe(true);
  });

  it('posts nothing, nor does a row removed', () => {
    const { form, press } = wake([GUESTS], REPEATER(2, 0, 3));
    const submitted = vi.fn((event: Event) => {
      event.preventDefault();
    });
    form.addEventListener('submit', submitted);
    press('add:guests');
    press('remove:guests:0');

    expect(submitted).not.toHaveBeenCalled();
  });

  it('starts as a page does: an "Other" of its own hides until its choice is chosen', () => {
    const other =
      '<div class="jfb-choice"><input type="radio" id="o" name="guests[0].x" value=""><label for="o">Other</label></div>' +
      '<div class="jfb-other"><input id="ot" name="guests[0].x.other"></div>';
    const { form, press } = wake(
      [GUESTS],
      REPEATER(0, 0, 3).replace('</fieldset></template>', `${other}</fieldset></template>`),
    );
    press('add:guests');

    expect(form.querySelector<HTMLElement>('.jfb-repeater > fieldset .jfb-other')?.hidden).toBe(true);
  });

  it('starts from what was filled into it: a default of "Other" shows its text', () => {
    const other =
      '<div class="jfb-choice"><input type="radio" id="o" name="guests[0].x" value=""><label for="o">Other</label></div>' +
      '<div class="jfb-other"><input id="ot" name="guests[0].x.other"></div>';
    const { form, press, added } = wake(
      [GUESTS],
      REPEATER(0, 0, 3).replace('</fieldset></template>', `${other}</fieldset></template>`),
    );
    added.mockImplementation((row: Element) => {
      (row.querySelector('[type="radio"]') as HTMLInputElement).checked = true;
    });
    press('add:guests');

    expect(form.querySelector<HTMLElement>('.jfb-repeater > fieldset .jfb-other')?.hidden).toBe(false);
  });
});

describe('a row removed in place', () => {
  it('gives the rows after it its place, with every name, id and word', () => {
    const { form, press, heard } = wake([GUESTS], REPEATER(3, 0, 12));
    form.querySelector<HTMLInputElement>('[name="guests[2].name"]')?.setAttribute('data-kept', '');
    press('remove:guests:1');

    const rows = [...form.querySelectorAll<HTMLElement>('.jfb-repeater > fieldset')];
    expect(rows.map((each) => each.dataset['jfbRow'])).toEqual(['0', '1']);
    const moved = rows[1] as HTMLElement;
    expect(moved.id).toBe('jfb-1-guests-1');
    expect(moved.querySelector('input')?.name).toBe('guests[1].name');
    expect(moved.querySelector('input')?.hasAttribute('data-kept')).toBe(true);
    expect(moved.querySelector('legend')?.textContent).toBe('Guest 2');
    expect(moved.querySelector('button')?.value).toBe('remove:guests:1');
    expect(moved.querySelector('.jfb-visually-hidden')?.textContent).toBe(' Guest 2');
    expect(form.querySelector<HTMLInputElement>('[name="jfb-rows-guests"]')?.value).toBe('2');
    expect(heard).toHaveBeenCalled();
  });

  it('does not take a row of one digit for one of two', () => {
    const { form, press } = wake([GUESTS], REPEATER(12, 0, 12));
    press('remove:guests:1');

    const last = form.querySelector('[data-jfb-row="10"]');
    expect(last?.id).toBe('jfb-1-guests-10');
    expect(last?.querySelector('input')?.name).toBe('guests[10].name');
    expect(form.querySelector('[data-jfb-row="0"] input')?.id).toBe('jfb-1-guests-0-name');
    expect(form.querySelector('[data-jfb-row="1"] input')?.id).toBe('jfb-1-guests-1-name');
  });

  it("says so, and gives the focus to the next row's Remove", () => {
    const { form, press, live } = wake([GUESTS], REPEATER(3, 0, 12));
    press('remove:guests:0');

    expect(live()).toBe('Guest 1 removed.');
    expect(document.activeElement).toBe(form.querySelector('[value="remove:guests:0"]'));
  });

  it('gives the focus to the row before when it was the last', () => {
    const { form, press } = wake([GUESTS], REPEATER(2, 0, 12));
    press('remove:guests:1');

    expect(document.activeElement).toBe(form.querySelector('[value="remove:guests:0"]'));
  });

  it('gives the focus to Add when it was the only one, and to the group at its least', () => {
    const { form, press } = wake([GUESTS], REPEATER(1, 0, 12));
    press('remove:guests:0');
    expect(document.activeElement).toBe(form.querySelector('[value="add:guests"]'));

    const least = wake([GUESTS], REPEATER(2, 1, 2, true));
    least.press('remove:guests:1');
    expect(document.activeElement).toBe(least.form.querySelector('[value="add:guests"]'));
    expect(least.form.querySelector<HTMLElement>('[value="remove:guests:0"]')?.hidden).toBe(true);
  });

  it('renames what the templates of the rows after it hold', () => {
    const { form, press } = wake(
      [GUESTS],
      REPEATER(2, 0, 3).replace(
        '<input id="jfb-1-guests-1-name"',
        '<template><input id="jfb-1-guests-1-x" name="guests[1].x"></template><input id="jfb-1-guests-1-name"',
      ),
    );
    press('remove:guests:0');

    const held = form.querySelector<HTMLTemplateElement>('[data-jfb-row="0"] template')?.content.querySelector('input');
    expect(held?.name).toBe('guests[0].x');
    expect(held?.id).toBe('jfb-1-guests-0-x');
  });

  it('takes away the error summary, whose links name the rows as they were', () => {
    const { form, press } = wake([GUESTS], `<div data-jfb-summary></div>${REPEATER(2, 0, 12)}`);
    const first = form.firstElementChild as HTMLElement;
    expect(first.hasAttribute('data-jfb-summary')).toBe(true);
    press('remove:guests:0');

    expect(form.querySelector('[data-jfb-summary]')).toBeNull();
  });
});

describe('a ranking moved by its buttons', () => {
  const FIELD: ModelField = { key: 'k-rank', type: 'ranking', alias: 'rank', settings: { top: 2 } };
  const item = (value: string, at: number, ranked = at < 2): string =>
    `<li id="jfb-1-rank-place-${String(at + 1)}"><span>${ranked ? String(at + 1) : ''}</span><span>${value.toUpperCase()}</span>` +
    `<input type="hidden" name="jfb-order-rank" value="${value}">` +
    (ranked ? `<input type="hidden" name="rank.${value}" value="${String(at + 1)}">` : '') +
    `<button type="submit" name="jfb-nav" value="up:rank:${value}"${at === 0 ? ' aria-disabled="true"' : ''}>Up</button>` +
    `<button type="submit" name="jfb-nav" value="down:rank:${value}"${at === 2 ? ' aria-disabled="true"' : ''}>Down</button></li>`;
  const RANKING =
    `<div data-jfb-field="k-rank" data-jfb-alias="rank"><ol class="jfb-ranking" data-jfb-name="rank" data-jfb-ranked="2" data-jfb-moved="{0} moved to place {1}.">` +
    `${['a', 'b', 'c'].map((value, at) => item(value, at)).join('')}</ol></div>`;

  const order = (form: HTMLFormElement): (string | undefined)[] =>
    [...form.querySelectorAll<HTMLInputElement>('[name="jfb-order-rank"]')].map((each) => each.value);
  const places = (form: HTMLFormElement): string[] =>
    [...form.querySelectorAll('li > input:not([name="jfb-order-rank"])')].map(
      (each) => `${(each as HTMLInputElement).name}=${(each as HTMLInputElement).value}`,
    );

  it('moves up, with its order, its number and its position as the server writes them', () => {
    const { form, press, heard } = wake([FIELD], RANKING);
    press('up:rank:b');

    expect(order(form)).toEqual(['b', 'a', 'c']);
    expect(places(form)).toEqual(['rank.b=1', 'rank.a=2']);
    expect([...form.querySelectorAll('li > span:first-child')].map((each) => each.textContent)).toEqual(['1', '2', '']);
    expect(form.querySelector('li')?.id).toBe('jfb-1-rank-place-1');
    expect(heard).toHaveBeenCalled();
  });

  it("gives the field's id to the first place's first button, where the summary's link leads", () => {
    wake([FIELD], RANKING.replace('value="up:rank:a"', 'id="jfb-1-rank" value="up:rank:a"')).press('up:rank:b');

    expect((document.getElementById('jfb-1-rank') as HTMLButtonElement).value).toBe('up:rank:b');
    expect(document.querySelectorAll('[id="jfb-1-rank"]')).toHaveLength(1);
  });

  it('moves down into a place that has no position, which goes', () => {
    const { form, press } = wake([FIELD], RANKING);
    press('down:rank:b');

    expect(order(form)).toEqual(['a', 'c', 'b']);
    expect(places(form)).toEqual(['rank.a=1', 'rank.c=2']);
    expect(form.querySelector('li:last-child > input:not([name="jfb-order-rank"])')).toBeNull();
  });

  it('posts nothing', () => {
    const { form, press } = wake([FIELD], RANKING);
    const submitted = vi.fn((event: Event) => {
      event.preventDefault();
    });
    form.addEventListener('submit', submitted);
    press('up:rank:b');
    press('down:rank:b');

    expect(submitted).not.toHaveBeenCalled();
  });

  it('marks the last end for the one that comes there, and frees the one that left', () => {
    const { form, press } = wake([FIELD], RANKING);
    press('down:rank:b');

    expect(form.querySelector('[value="down:rank:b"]')?.getAttribute('aria-disabled')).toBe('true');
    expect(form.querySelector('[value="down:rank:c"]')?.getAttribute('aria-disabled')).toBe('false');
  });

  it('moves up into a place that has one, which comes', () => {
    const { form, press } = wake([FIELD], RANKING);
    press('up:rank:c');

    expect(places(form)).toEqual(['rank.a=1', 'rank.c=2']);
  });

  it('says where it went, keeps the focus on its button, and marks the ends', () => {
    const { form, press, live } = wake([FIELD], RANKING);
    press('up:rank:b');
    const up = form.querySelector<HTMLElement>('[value="up:rank:b"]');

    expect(live()).toBe('B moved to place 1.');
    expect(document.activeElement).toBe(up);
    expect(up?.getAttribute('aria-disabled')).toBe('true');
    expect(form.querySelector('[value="up:rank:a"]')?.getAttribute('aria-disabled')).toBe('false');
    expect(form.querySelector('[value="down:rank:c"]')?.getAttribute('aria-disabled')).toBe('true');
  });

  it('stays at its end', () => {
    const { form, press, live, heard } = wake([FIELD], RANKING);
    heard.mockClear();
    press('up:rank:a');

    expect(order(form)).toEqual(['a', 'b', 'c']);
    expect(live()).toBe('');
    expect(heard).not.toHaveBeenCalled();
  });

  it('keeps a position it adds off while the field is off', () => {
    const { form, press } = wake(
      [FIELD],
      RANKING.replaceAll('name="jfb-order-rank"', 'name="jfb-order-rank" disabled'),
    );
    press('up:rank:c');

    expect(form.querySelector<HTMLInputElement>('[name="rank.c"]')?.disabled).toBe(true);
  });

  it('leaves other buttons to the post', () => {
    const { form } = wake(
      [FIELD],
      `${RANKING}<button type="submit" name="jfb-nav" value="submit" id="send">Send</button>`,
    );
    const submitted = vi.fn((event: Event) => {
      event.preventDefault();
    });
    form.addEventListener('submit', submitted);
    document.getElementById('send')?.click();

    expect(submitted).toHaveBeenCalled();
  });
});

describe('a counter', () => {
  const FIELD: ModelField = { key: 'k-note', type: 'textarea', alias: 'note', settings: {} };
  const of = (value = ''): string =>
    `<div data-jfb-field="k-note"><textarea id="jfb-1-note" name="note">${value}</textarea>` +
    `<p class="jfb-counter" id="jfb-1-note-count" data-jfb-limit="1000" data-jfb-left="{0} characters left" data-jfb-left-one="{0} character left" ` +
    `data-jfb-too-many="{0} characters too many" data-jfb-too-many-one="{0} character too many">Up to 1000 characters</p></div>`;
  const type = (text: string): void => {
    const area = document.getElementById('jfb-1-note') as HTMLTextAreaElement;
    area.value = text;
    area.dispatchEvent(new Event('input', { bubbles: true }));
  };
  const counter = (): HTMLElement => document.getElementById('jfb-1-note-count') as HTMLElement;

  it("keeps the server's words while the text is empty, and comes back to them", () => {
    wake([FIELD], of());
    expect(counter().textContent).toBe('Up to 1000 characters');
    type('a');
    type('');

    expect(counter().textContent).toBe('Up to 1000 characters');
  });

  it("says what is left from the start for a text the page came with, in the page's numbers", () => {
    wake([FIELD], of('a'.repeat(2)));

    expect(counter().textContent).toBe('998 characters left');
  });

  it('says one, and past the limit by how many, marked', () => {
    wake([FIELD], of());
    type('a'.repeat(999));
    expect(counter().textContent).toBe('1 character left');
    expect(counter().hasAttribute('data-jfb-over')).toBe(false);

    type('a'.repeat(1000));
    expect(counter().textContent).toBe('0 characters left');
    expect(counter().hasAttribute('data-jfb-over')).toBe(false);

    type('a'.repeat(1001));
    expect(counter().textContent).toBe('1 character too many');
    expect(counter().hasAttribute('data-jfb-over')).toBe(true);

    type('a'.repeat(3200));
    expect(counter().textContent).toBe('2,200 characters too many');

    type('a');
    expect(counter().hasAttribute('data-jfb-over')).toBe(false);
  });

  it("leaves alone another field's element named so", () => {
    wake([FIELD], '<input id="jfb-1-x" name="x"><p id="jfb-1-x-count">Mine</p>');
    const input = document.getElementById('jfb-1-x') as HTMLInputElement;
    input.value = 'a';
    input.dispatchEvent(new Event('input', { bubbles: true }));

    expect(document.getElementById('jfb-1-x-count')?.textContent).toBe('Mine');
  });
});

describe('the "Other" text', () => {
  const FIELD: ModelField = { key: 'k-room', type: 'radio', alias: 'room', settings: { other: true } };
  const of = (checked: boolean): string =>
    `<div data-jfb-field="k-room" data-jfb-alias="room"><div class="jfb-choices"><div class="jfb-choice"><input type="radio" id="r1" name="room" value="a"><label for="r1">A</label></div>` +
    `<div class="jfb-choice"><input type="radio" id="r2" name="room" value=""${checked ? ' checked' : ''}><label for="r2">Other</label></div>` +
    `<div class="jfb-other"><label for="jfb-1-room-other">Your own</label><input id="jfb-1-room-other" name="room.other"></div></div></div>`;
  const box = (): HTMLElement => document.querySelector('.jfb-other') as HTMLElement;
  const choose = (id: string): void => {
    const input = document.getElementById(id) as HTMLInputElement;
    input.checked = true;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  };

  it('shows only while its choice is chosen, and is posted only then', () => {
    wake([FIELD], of(false));
    const text = document.getElementById('jfb-1-room-other') as HTMLInputElement;
    expect(box().hidden).toBe(true);
    expect(text.hasAttribute('name')).toBe(false);

    choose('r2');
    expect(box().hidden).toBe(false);
    expect(text.name).toBe('room.other');

    text.value = 'The attic';
    choose('r1');
    expect(box().hidden).toBe(true);
    expect(text.hasAttribute('name')).toBe(false);
    expect(text.value, 'kept for coming back').toBe('The attic');

    choose('r2');
    expect(text.name).toBe('room.other');
  });

  it('shows from the start for a choice the page came with', () => {
    wake([FIELD], of(true));

    expect(box().hidden).toBe(false);
  });

  it('moves no focus', () => {
    wake([FIELD], of(false));
    const chosen = document.getElementById('r2') as HTMLInputElement;
    chosen.focus();
    choose('r2');

    expect(document.activeElement).toBe(chosen);
  });
});

describe('a text area that grows', () => {
  const FIELD: ModelField = { key: 'k-note', type: 'textarea', alias: 'note', settings: {} };
  const area = (grows: boolean): string =>
    `<div data-jfb-field="k-note"><textarea id="n" name="note" rows="2"${grows ? ' data-jfb-grow' : ''}></textarea></div>`;
  const shape = (element: HTMLTextAreaElement, scroll: number): void => {
    Object.defineProperty(element, 'scrollHeight', { value: scroll, configurable: true });
    Object.defineProperty(element, 'offsetHeight', { value: 42, configurable: true });
    Object.defineProperty(element, 'clientHeight', { value: 40, configurable: true });
  };
  const typed = (): HTMLTextAreaElement => document.getElementById('n') as HTMLTextAreaElement;

  it('takes the height of its text, its border with it', () => {
    wake([FIELD], area(true));
    shape(typed(), 300);
    typed().dispatchEvent(new Event('input', { bubbles: true }));

    expect(typed().style.blockSize).toBe('302px');
  });

  it('keeps its rows with the setting off', () => {
    wake([FIELD], area(false));
    shape(typed(), 300);
    typed().dispatchEvent(new Event('input', { bubbles: true }));

    expect(typed().style.blockSize).toBe('');
  });

  it('leaves its height alone while it does not show', () => {
    wake([FIELD], area(true));
    shape(typed(), 0);
    typed().dispatchEvent(new Event('input', { bubbles: true }));

    expect(typed().style.blockSize).toBe('auto');
  });

  it.each(['input', 'change'])('grows once an %s elsewhere has shown it', async (kind) => {
    const { form } = wake([FIELD], area(true));
    shape(typed(), 0);
    typed().dispatchEvent(new Event('input', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 5));
    shape(typed(), 300);
    expect(typed().style.blockSize).toBe('auto');
    form.dispatchEvent(new Event(kind, { bubbles: true }));

    await until(() => expect(typed().style.blockSize).toBe('302px'));
  });
});

describe("a cascading field's options", () => {
  const BRANCH: ModelField = {
    key: 'k-branch',
    type: 'dropdown',
    alias: 'branch',
    settings: {},
    options: { items: [{ value: 'north' }, { value: 'south' }] },
  };
  const DESK: ModelField = {
    key: 'k-desk',
    type: 'dropdown',
    alias: 'desk',
    settings: {},
    options: { parent: 'k-branch' },
  };
  const CORNER: ModelField = {
    key: 'k-corner',
    type: 'radio',
    alias: 'corner',
    settings: {},
    options: { parent: 'k-desk' },
  };
  const BRANCH_MARKUP =
    '<div data-jfb-field="k-branch" data-jfb-alias="branch"><select id="b" name="branch"><option value="">Choose</option><option value="north">North</option><option value="south">South</option></select></div>';
  const DESK_MARKUP =
    '<div data-jfb-field="k-desk" data-jfb-alias="desk"><select id="d" name="desk" data-jfb-cascade data-jfb-prompt="Choose" data-jfb-none="Nothing yet." data-jfb-unavailable="Try later." disabled><option value="">Nothing yet.</option></select></div>';
  const CORNER_MARKUP =
    '<div data-jfb-field="k-corner" data-jfb-alias="corner"><p id="c">Nothing yet.</p><template data-jfb-cascade data-jfb-none="Nothing yet." data-jfb-unavailable="Try later."><div class="jfb-choices"><div class="jfb-choice" data-jfb-sample>' +
    '<input type="radio" id="c" name="corner" value="" aria-describedby="c-description"><label for="c"></label><p id="c-description"></p></div></div><p id="c"></p></template></div>';

  const answers = (body: unknown, status = 200): Response => new Response(JSON.stringify(body), { status });
  function serve(answer: (url: string) => Response | Promise<Response>): string[] {
    const asked: string[] = [];
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string) => {
        asked.push(url);
        return Promise.resolve(answer(url));
      }),
    );
    return asked;
  }
  const chooseBranch = (value: string): void => {
    const select = document.getElementById('b') as HTMLSelectElement;
    select.value = value;
    select.dispatchEvent(new Event('change', { bubbles: true }));
  };
  const desk = (): HTMLSelectElement => document.getElementById('d') as HTMLSelectElement;
  const NORTH = {
    available: true,
    options: [
      { value: 'd1', label: 'Desk one' },
      { value: 'd2', label: 'Desk two' },
    ],
  };

  it('asks nothing for what the page came with', async () => {
    const asked = serve(() => answers(NORTH));
    wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(asked).toEqual([]);
  });

  it('asks at the start for a parent that holds an answer by then, whose options the server may not have written', async () => {
    const asked = serve(() => answers(NORTH));
    wake([BRANCH, DESK], BRANCH_MARKUP.replace('value="north"', 'value="north" selected') + DESK_MARKUP);
    await until(() => expect(desk().options).toHaveLength(3));

    expect(asked).toEqual(['/jfb/api/v1/forms/f/options/k-desk?culture=en-US&contentKey=c1&parent.k-branch=north']);
  });

  it('writes nothing when the options are those that show, and the focus stays', async () => {
    const asked = serve(() => answers(NORTH));
    wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() => expect(desk().options).toHaveLength(3));
    const option = desk().options[1];
    chooseBranch('south');
    await until(() => expect(asked).toHaveLength(2));
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(desk().options[1]).toBe(option);
  });

  it('asks for each field from one reading, not waiting for the answer of another', async () => {
    const asked = serve(() => new Promise<Response>(() => undefined));
    wake([BRANCH, DESK, CORNER], BRANCH_MARKUP + DESK_MARKUP + CORNER_MARKUP);
    chooseBranch('north');

    await until(() => expect(asked).toHaveLength(2));
  });

  it.each(['ranking', 'grid'])('leaves a %s below a parent to the post', async (type) => {
    const asked = serve(() => answers(NORTH));
    wake(
      [BRANCH, { key: 'k-other', type, alias: 'other', settings: {}, options: { parent: 'k-branch' } }],
      `${BRANCH_MARKUP}<div data-jfb-field="k-other" data-jfb-alias="other"></div>`,
    );
    chooseBranch('north');
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(asked).toEqual([]);
  });

  it('asks the endpoint when the parent changes, with the culture and the page, and offers what comes', async () => {
    const asked = serve(() => answers(NORTH));
    wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() => expect(desk().options).toHaveLength(3));

    expect(asked).toEqual(['/jfb/api/v1/forms/f/options/k-desk?culture=en-US&contentKey=c1&parent.k-branch=north']);
    expect([...desk().options].map((each) => [each.value, each.textContent])).toEqual([
      ['', 'Choose'],
      ['d1', 'Desk one'],
      ['d2', 'Desk two'],
    ]);
    expect(desk().disabled).toBe(false);
  });

  it("asks once for one change, and not for another field's", async () => {
    const asked = serve(() => answers(NORTH));
    const { form } = wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() => expect(asked).toHaveLength(1));
    await until(() => expect(desk().options).toHaveLength(3));
    form.dispatchEvent(new Event('change', { bubbles: true }));
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(asked).toHaveLength(1);
  });

  it('keeps an answer still offered, and clears one no longer offered, telling the rules', async () => {
    serve((url) =>
      answers(url.includes('north') ? NORTH : { available: true, options: [{ value: 'd2', label: 'Desk two' }] }),
    );
    const { heard } = wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() => expect(desk().options).toHaveLength(3));
    desk().value = 'd2';
    chooseBranch('south');
    await until(() => expect(desk().options).toHaveLength(2));
    expect(desk().value).toBe('d2');

    desk().value = 'd2';
    serve(() => answers({ available: true, options: [{ value: 'd3', label: 'Desk three' }] }));
    heard.mockClear();
    chooseBranch('north');
    await until(() => expect(desk().options[1]?.value).toBe('d3'));

    expect(desk().value).toBe('');
    await until(() =>
      expect(
        heard.mock.calls.some(
          ([event]) => (event as Event).target === document.querySelector('[data-jfb-field="k-desk"]'),
        ),
      ).toBe(true),
    );
  });

  it('says there is nothing to choose, or that the options could not be had, and turns the list off', async () => {
    serve((url) =>
      answers(url.includes('north') ? { available: true, options: [] } : { available: false, options: [] }),
    );
    wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() => expect(desk().options[0]?.textContent).toBe('Nothing yet.'));
    expect(desk().disabled).toBe(true);

    chooseBranch('south');
    await until(() => expect(desk().options[0]?.textContent).toBe('Try later.'));
    expect(desk().disabled).toBe(true);
  });

  it('marks a list with nothing to offer for the rules to keep off, and frees one that has', async () => {
    let offered: unknown = { available: true, options: [] };
    serve(() => answers(offered));
    wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() => expect(desk().hasAttribute('data-jfb-disabled')).toBe(true));

    offered = NORTH;
    chooseBranch('south');
    await until(() => expect(desk().hasAttribute('data-jfb-disabled')).toBe(false));
    expect(desk().disabled).toBe(false);
  });

  it('keeps what it writes off while a rule hides the field', async () => {
    serve(() => answers(NORTH));
    const { form } = wake(
      [BRANCH, DESK, CORNER],
      `${BRANCH_MARKUP}<div hidden data-jfb-hidden>${DESK_MARKUP}${CORNER_MARKUP}</div>`,
    );
    chooseBranch('north');
    await until(() => expect(desk().options).toHaveLength(3));
    await until(() => expect(form.querySelectorAll('[name="corner"]')).toHaveLength(2));

    expect(desk().disabled).toBe(true);
    expect(desk().hasAttribute('data-jfb-disabled')).toBe(false);
    expect([...form.querySelectorAll<HTMLInputElement>('[name="corner"]')].every((each) => each.disabled)).toBe(true);
  });

  it('tells the rules of every write, an answer lost or not', async () => {
    serve(() => answers(NORTH));
    const { heard } = wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() =>
      expect(
        heard.mock.calls.some(
          ([event]) => (event as Event).target === document.querySelector('[data-jfb-field="k-desk"]'),
        ),
      ).toBe(true),
    );
  });

  describe('with an "Other"', () => {
    const OTHER_CHOICE =
      '<div class="jfb-choice"><input type="radio" id="c-other" name="corner" value=""><label for="c-other">Other</label></div>' +
      '<div class="jfb-other"><label for="c-text">Your own</label><input id="c-text" name="corner.other"></div>';
    const MARKUP = CORNER_MARKUP.replace(
      '<p id="c">Nothing yet.</p>',
      () => `<div class="jfb-choices">${OTHER_CHOICE}</div>`,
    ).replace('</div></div><p id="c"></p></template>', () => `</div>${OTHER_CHOICE}</div><p id="c"></p></template>`);
    const TWO = {
      available: true,
      options: [
        { value: 'x', label: 'X' },
        { value: 'y', label: 'Y' },
      ],
    };
    const other = (form: HTMLFormElement): HTMLInputElement =>
      form.querySelector('[name="corner"][value=""]') as HTMLInputElement;

    it('keeps it as the visitor left it, chosen and with its text, after what is offered', async () => {
      serve(() => answers(TWO));
      const { form } = wake([BRANCH, DESK, CORNER], BRANCH_MARKUP + DESK_MARKUP + MARKUP);
      other(form).checked = true;
      other(form).dispatchEvent(new Event('change', { bubbles: true }));
      await new Promise((resolve) => setTimeout(resolve, 5));
      (form.querySelector('.jfb-other input') as HTMLInputElement).value = 'The attic';
      chooseBranch('north');
      await until(() => expect(form.querySelectorAll('[name="corner"]')).toHaveLength(3));

      expect(other(form).checked).toBe(true);
      expect((form.querySelector('.jfb-other input') as HTMLInputElement).value).toBe('The attic');
      expect((form.querySelector('.jfb-other') as HTMLElement).hidden).toBe(false);
      expect(form.querySelectorAll('.jfb-other')).toHaveLength(1);
      expect([...form.querySelectorAll<HTMLInputElement>('[name="corner"]')].map((each) => each.value)).toEqual([
        'x',
        'y',
        '',
      ]);
    });

    it('gives its choice the place after the others, its label with it', async () => {
      serve(() => answers(TWO));
      const { form } = wake([BRANCH, DESK, CORNER], BRANCH_MARKUP + DESK_MARKUP + MARKUP);
      chooseBranch('north');
      await until(() => expect(form.querySelectorAll('[name="corner"]')).toHaveLength(3));

      expect(other(form).id).toBe('c-3');
      expect(other(form).nextElementSibling?.getAttribute('for')).toBe('c-3');
      expect(new Set([...form.querySelectorAll('[name="corner"]')].map((each) => each.id)).size).toBe(3);
    });

    it("takes the template's when none showed, not chosen and so hidden", async () => {
      serve(() => answers(TWO));
      const { form } = wake(
        [BRANCH, DESK, CORNER],
        BRANCH_MARKUP +
          DESK_MARKUP +
          MARKUP.replace(
            `<div class="jfb-choices">${OTHER_CHOICE}</div><template`,
            '<p id="c">Nothing yet.</p><template',
          ),
      );
      expect(other(form)).toBeNull();
      chooseBranch('north');
      await until(() => expect(form.querySelectorAll('[name="corner"]')).toHaveLength(3));

      expect((form.querySelector('.jfb-other') as HTMLElement).hidden).toBe(true);
      expect(form.querySelector('.jfb-other input')?.hasAttribute('name')).toBe(false);
    });

    it('stays when nothing else is offered', async () => {
      let offered: unknown = TWO;
      serve(() => answers(offered));
      const { form } = wake([BRANCH, DESK, CORNER], BRANCH_MARKUP + DESK_MARKUP + MARKUP);
      chooseBranch('north');
      await until(() => expect(form.querySelectorAll('[name="corner"]')).toHaveLength(3));

      offered = { available: true, options: [] };
      chooseBranch('south');
      await until(() => expect(form.querySelectorAll('[name="corner"]')).toHaveLength(1));
      expect(other(form).id).toBe('c');
    });
  });

  it('leaves what shows when no answer comes', async () => {
    serve(() => answers(NORTH, 500));
    wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    const before = desk().innerHTML;
    chooseBranch('north');
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(desk().innerHTML).toBe(before);
  });

  it('counts only the answer to what was asked last', async () => {
    let first: (response: Response) => void = () => undefined;
    const asked = serve((url) =>
      url.includes('north')
        ? new Promise<Response>((resolve) => {
            first = resolve;
          })
        : answers({ available: true, options: [{ value: 's', label: 'South desk' }] }),
    );
    wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP);
    chooseBranch('north');
    await until(() => expect(asked).toHaveLength(1));
    chooseBranch('south');
    await until(() => expect(desk().options[1]?.value).toBe('s'));
    first(answers(NORTH));
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(desk().options[1]?.value).toBe('s');
  });

  it('names every level of a chain', async () => {
    const asked = serve(() => answers({ available: true, options: [] }));
    wake([BRANCH, DESK, CORNER], BRANCH_MARKUP + DESK_MARKUP + CORNER_MARKUP);
    chooseBranch('north');
    await until(() => expect(asked).toHaveLength(2));

    expect(asked).toContain('/jfb/api/v1/forms/f/options/k-corner?culture=en-US&contentKey=c1&parent.k-branch=north');
  });

  it('writes radio buttons from the sample, each with its id, label and description, and a word for none', async () => {
    serve(() =>
      answers({
        available: true,
        options: [
          { value: 'x', label: 'By the door', description: 'Quiet' },
          { value: 'y', label: 'By the window' },
        ],
      }),
    );
    const { form } = wake([BRANCH, DESK, CORNER], BRANCH_MARKUP + DESK_MARKUP + CORNER_MARKUP);
    chooseBranch('north');
    await until(() => expect(form.querySelectorAll('[data-jfb-alias="corner"] .jfb-choice')).toHaveLength(2));

    const inputs = [...form.querySelectorAll<HTMLInputElement>('[name="corner"]')];
    expect(inputs.map((each) => [each.id, each.value, each.type])).toEqual([
      ['c', 'x', 'radio'],
      ['c-2', 'y', 'radio'],
    ]);
    expect(form.querySelector('[for="c-2"]')?.textContent).toBe('By the window');
    expect(form.querySelector('#c-description')?.textContent).toBe('Quiet');
    expect(inputs[0]?.getAttribute('aria-describedby')).toBe('c-description');
    expect(inputs[1]?.hasAttribute('aria-describedby')).toBe(false);
    expect(form.querySelector('#c-2-description')).toBeNull();
    expect(form.querySelector('[data-jfb-alias="corner"] p#c')).toBeNull();
    expect(form.querySelector('[data-jfb-sample]')).toBeNull();
  });

  it('keeps a radio button still offered, and shows the word when none is', async () => {
    let offered: { value: string; label: string }[] = [{ value: 'x', label: 'X' }];
    serve(() => answers({ available: true, options: offered }));
    const { form } = wake([BRANCH, DESK, CORNER], BRANCH_MARKUP + DESK_MARKUP + CORNER_MARKUP);
    chooseBranch('north');
    await until(() => expect(form.querySelector('[name="corner"]')).not.toBeNull());
    (form.querySelector('[name="corner"]') as HTMLInputElement).checked = true;

    offered = [
      { value: 'x', label: 'X' },
      { value: 'y', label: 'Y' },
    ];
    chooseBranch('south');
    await until(() => expect(form.querySelectorAll('[name="corner"]')).toHaveLength(2));
    expect((form.querySelector('[name="corner"][value="x"]') as HTMLInputElement).checked).toBe(true);

    offered = [];
    chooseBranch('north');
    await until(() => expect(form.querySelector('[data-jfb-alias="corner"] > p')?.textContent).toBe('Nothing yet.'));
  });

  it('stays as the server wrote it without the address of the API', async () => {
    const asked = serve(() => answers(NORTH));
    const { form } = wake([BRANCH, DESK], BRANCH_MARKUP + DESK_MARKUP, null);
    chooseBranch('north');
    await new Promise((resolve) => setTimeout(resolve, 5));

    expect(asked).toEqual([]);
    expect(form.querySelector('#d')?.hasAttribute('disabled')).toBe(true);
  });
});

describe('a form whose files lie only in rows yet to come', () => {
  const UPLOAD = 'k-file';
  const SIGNATURE = 'k-sign';
  const uploadIn = (row: number): string =>
    `<fieldset class="jfb-row" id="jfb-1-guests-${String(row)}" data-jfb-row="${String(row)}"><legend>Guest</legend>` +
    `<div data-jfb-field="${UPLOAD}" data-jfb-alias="guests[${String(row)}].plan" data-jfb-type="file"><div class="jfb-upload">` +
    `<input type="file" id="jfb-1-guests-${String(row)}-plan" name="guests[${String(row)}].plan"></div>` +
    '<template data-jfb-file><ul class="jfb-files"></ul><li class="jfb-file"><strong></strong><p></p><progress></progress></li></template></div></fieldset>';
  const signatureIn = (row: number): string =>
    `<fieldset class="jfb-row" id="jfb-1-guests-${String(row)}" data-jfb-row="${String(row)}"><legend>Guest</legend>` +
    `<div data-jfb-field="${SIGNATURE}" data-jfb-alias="guests[${String(row)}].sign" data-jfb-type="signature"><fieldset><legend>Sign</legend>` +
    '<div class="jfb-signature"><div class="jfb-signature-way" data-jfb-drawn><p>Sign here</p>' +
    '<canvas class="jfb-signature-pad" width="900" height="300"></canvas><div class="jfb-signature-tools">' +
    '<button type="button" data-jfb-clear data-jfb-undo="Undo clear">Clear</button>' +
    '<button type="button" data-jfb-to-typed>Type your name instead</button></div></div>' +
    `<div class="jfb-signature-way" data-jfb-typed><label for="s${String(row)}">Your name</label><input id="s${String(row)}" name="guests[${String(row)}].sign.typed">` +
    '<div class="jfb-signature-tools"><button type="button" data-jfb-to-drawn>Sign by hand instead</button></div></div></div></fieldset></div></fieldset>';

  it.each([
    ['an upload', '.jfb-upload', UPLOAD, 'file', uploadIn],
    ['a signature pad', '.jfb-signature', SIGNATURE, 'signature', signatureIn],
  ])('still takes on %s when a row brings one', async (_name, selector, key, type, rowOfIt) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.resolve(new Response(JSON.stringify({ token: 't', expiresIn: 600 })))),
    );
    document.body.innerHTML =
      '<form id="jfb-1" data-jfb-form="f" data-jfb-api="/jfb/api/v1"><template data-jfb-templates data-jfb-sending="Sending"></template><section data-jfb-page="p1">' +
      '<div data-jfb-field="k-guests" data-jfb-alias="guests"><div class="jfb-repeater" id="jfb-1-guests" data-jfb-least="0" data-jfb-most="3" data-jfb-legend="Guest {0}" data-jfb-removed="{0} removed.">' +
      `<input type="hidden" name="jfb-rows-guests" value="0"><template>${rowOfIt(0)}</template>` +
      '<button type="submit" name="jfb-nav" value="add:guests">Add a row</button></div></div></section><div data-jfb-live></div></form>';
    const form = document.querySelector('form') as HTMLFormElement;
    const inner: ModelField = { key, type, alias: 'inner', settings: {} };
    const model = modelOf([{ ...GUESTS, fields: [inner] }]);
    const { attach } = await import('./form');
    await attach(form, {
      ...model,
      fields: { ...model.fields, [key]: { kind: 'files', canBeRequired: true, builtIn: true } },
    });
    form.querySelector<HTMLElement>('[value="add:guests"]')?.click();

    expect(form.querySelector(`.jfb-repeater > fieldset ${selector}`)?.hasAttribute('data-jfb-enhanced')).toBe(true);
  });
});
