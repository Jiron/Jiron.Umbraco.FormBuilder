/**
 * The field behaviours (ROADMAP 6.4o, ADR-0154), a chunk that only a form with one of them loads: a repeatable group's rows
 * added and removed in place, a ranking moved by its buttons, a character counter, the "Other" text, a text area that grows,
 * and a cascading field's options, which the `options` endpoint answers for its parents' answers. Each works on what the
 * server's views write, whose buttons post without JavaScript, and leaves the page as a post would render it: the same names,
 * ids and order, so that the rules, the checks and the send read it as ever.
 */
import { removeSummary } from './display';
import type { FormModel, ModelField } from './model';
import { fieldsOf, readPage, type PageReading } from './page';

const FIELD = '[data-jfb-field]';
const NAV = 'button[name="jfb-nav"]';
const OTHER_NAME = 'data-jfb-name';

export interface FieldOptions {
  /** The public API's base; without it a cascading field stays as the server wrote it. */
  readonly api: string | undefined;
  /** What a row added brings with it: the uploads' zones, the defaults that hold merge tags. */
  readonly added: (row: HTMLElement) => void;
}

/** One option of the `options` endpoint's answer. */
interface Offered {
  readonly value: string;
  readonly label: string;
  readonly description?: string;
}

const said = (form: HTMLFormElement, text: string): void => {
  const live = form.querySelector(':scope > [data-jfb-live]');
  if (live !== null) live.textContent = text;
};

const told = (element: Element): void => {
  element.dispatchEvent(new Event('input', { bubbles: true }));
};

const replacing = (word: string, value: string, more = ''): string => word.replace('{0}', value).replace('{1}', more);

/** Enhances the form's field behaviours. */
export function enhanceFields(form: HTMLFormElement, model: FormModel, options: FieldOptions): void {
  const counted = new WeakMap<Element, string>();
  const numbers = new Intl.NumberFormat(model.culture);

  /** A row's name, ids and words for another place among its group's rows. */
  function moveRow(row: HTMLElement, box: HTMLElement, from: number, to: number): void {
    const alias = box.closest<HTMLElement>(FIELD)?.dataset['jfbAlias'] ?? '';
    const name = (elements: Iterable<Element>): void => {
      for (const element of elements) {
        for (const attribute of [...element.attributes]) {
          const moved = attribute.value
            .replaceAll(`${alias}[${String(from)}]`, `${alias}[${String(to)}]`)
            .replaceAll(`${box.id}-${String(from)}`, `${box.id}-${String(to)}`)
            .replace(`remove:${alias}:${String(from)}`, `remove:${alias}:${String(to)}`);
          if (moved !== attribute.value) element.setAttribute(attribute.name, moved);
        }
        // What a template holds is written into the row later, under the names it has by then.
        if (element instanceof HTMLTemplateElement) name(element.content.querySelectorAll('*'));
      }
    };
    name([row, ...row.querySelectorAll('*')]);
    row.dataset['jfbRow'] = String(to);
    const legend = replacing(box.dataset['jfbLegend'] ?? '', String(to + 1));
    const title = row.querySelector(':scope > legend');
    if (title !== null) title.textContent = legend;
    const context = row.querySelector(':scope > button .jfb-visually-hidden');
    if (context !== null) context.textContent = ` ${legend}`;
  }

  const rowsOf = (box: HTMLElement): HTMLElement[] => [...box.querySelectorAll<HTMLElement>(':scope > [data-jfb-row]')];

  /** The row count and the buttons, for the number of rows the group now has. */
  function countRows(box: HTMLElement): void {
    const rows = rowsOf(box);
    const count = box.querySelector<HTMLInputElement>(':scope > input[type="hidden"]');
    if (count !== null) count.value = String(rows.length);
    const add = box.querySelector<HTMLElement>(`:scope > ${NAV}`);
    if (add !== null) add.hidden = rows.length >= Number(box.dataset['jfbMost']);
    for (const row of rows) {
      const remove = row.querySelector<HTMLElement>(`:scope > ${NAV}`);
      if (remove !== null) remove.hidden = rows.length <= Number(box.dataset['jfbLeast']);
    }
  }

  function addRow(box: HTMLElement): void {
    const template = box.querySelector<HTMLTemplateElement>(':scope > template');
    const row = template?.content.querySelector<HTMLElement>('[data-jfb-row]')?.cloneNode(true) as
      HTMLElement | undefined;
    if (row === undefined) return;
    const at = rowsOf(box).length;
    moveRow(row, box, Number(row.dataset['jfbRow']), at);
    template?.before(row);
    countRows(box);
    options.added(row);
    tidy(row);
    told(box);
    // The rules decide for the row first, which may hide or turn off what it starts with.
    setTimeout(() => {
      [...row.querySelectorAll<HTMLInputElement>('input:not([type="hidden"]), select, textarea')]
        .find((each) => !each.disabled && each.closest('[hidden]') === null)
        ?.focus();
    });
  }

  function removeRow(box: HTMLElement, row: HTMLElement): void {
    const place = Number(row.dataset['jfbRow']);
    const legend = row.querySelector(':scope > legend')?.textContent ?? '';
    row.remove();
    const rows = rowsOf(box);
    rows.forEach((each, at) => {
      if (at >= place) moveRow(each, box, at + 1, at);
    });
    countRows(box);
    // Its links name the rows as they were; the next send lists them anew.
    removeSummary(form);
    told(box);
    said(form, replacing(box.dataset['jfbRemoved'] ?? '', legend));
    const next = (rows[place] ?? rows[place - 1])?.querySelector<HTMLElement>(`:scope > ${NAV}:not([hidden])`);
    (next ?? box.querySelector<HTMLElement>(`:scope > ${NAV}:not([hidden])`) ?? box).focus();
  }

  /** A ranking's places, for the order its items now have, as the server writes them. */
  function numberPlaces(list: HTMLElement): void {
    const items = [...list.children] as HTMLElement[];
    const ranked = Number(list.dataset['jfbRanked']);
    // The field's id is the first place's first button's, wherever that option went: the summary's link leads there.
    const named = list.querySelector('button[id]');
    const id = named?.id;
    named?.removeAttribute('id');
    const first = items[0]?.querySelector('button');
    if (id !== undefined && first != null) first.id = id;
    items.forEach((item, at) => {
      item.id = item.id.replace(/-place-\d+$/, `-place-${String(at + 1)}`);
      const [order, position] = item.querySelectorAll<HTMLInputElement>('input');
      const number = item.querySelector('span');
      if (number !== null) number.textContent = at < ranked ? String(at + 1) : '';
      if (at < ranked) {
        const input = position ?? item.appendChild(form.ownerDocument.createElement('input'));
        input.type = 'hidden';
        input.name = `${list.dataset['jfbName'] ?? ''}.${order?.value ?? ''}`;
        input.value = String(at + 1);
        input.disabled = order?.disabled === true;
      } else {
        position?.remove();
      }
      const [up, down] = item.querySelectorAll('button');
      up?.setAttribute('aria-disabled', String(at === 0));
      down?.setAttribute('aria-disabled', String(at === items.length - 1));
    });
  }

  function moveItem(button: HTMLButtonElement, up: boolean): void {
    if (button.getAttribute('aria-disabled') === 'true') return;
    const item = button.closest('li');
    const list = item?.parentElement;
    if (!item || !list) return;
    if (up) item.previousElementSibling?.before(item);
    else item.nextElementSibling?.after(item);
    numberPlaces(list);
    told(list);
    button.focus();
    const place = [...list.children].indexOf(item) + 1;
    said(
      form,
      replacing(list.dataset['jfbMoved'] ?? '', item.querySelectorAll('span')[1]?.textContent ?? '', String(place)),
    );
  }

  /** A counter's line for the text its control holds; an empty control keeps the server's words. */
  function count(control: HTMLInputElement | HTMLTextAreaElement): void {
    const counter = form.ownerDocument.getElementById(`${control.id}-count`);
    if (counter === null || !counter.matches('.jfb-counter')) return;
    if (!counted.has(counter)) counted.set(counter, counter.textContent);
    const data = counter.dataset;
    const left = Number(data['jfbLimit']) - control.value.length;
    counter.toggleAttribute('data-jfb-over', left < 0);
    const many = Math.abs(left);
    const words =
      left < 0
        ? many === 1
          ? data['jfbTooManyOne']
          : data['jfbTooMany']
        : many === 1
          ? data['jfbLeftOne']
          : data['jfbLeft'];
    counter.textContent =
      control.value.length === 0 ? (counted.get(counter) ?? '') : replacing(words ?? '', numbers.format(many));
  }

  const grow = (area: HTMLTextAreaElement): void => {
    area.style.blockSize = 'auto';
    const height = area.scrollHeight + area.offsetHeight - area.clientHeight;
    if (area.scrollHeight > 0) area.style.blockSize = `${String(height)}px`;
  };

  /** One that did not show when it last grew, which a rule may have shown since. */
  const growShown = (): void => {
    for (const area of form.querySelectorAll<HTMLTextAreaElement>('textarea[data-jfb-grow]')) {
      if (area.style.blockSize === 'auto') grow(area);
    }
  };

  /** The "Other" text shows and answers only while its choice is chosen. */
  function other(box: HTMLElement): void {
    const choice = box.previousElementSibling?.querySelector<HTMLInputElement>('input');
    const text = box.querySelector('input');
    if (!choice || !text) return;
    if (!text.hasAttribute(OTHER_NAME)) text.setAttribute(OTHER_NAME, text.name);
    box.hidden = !choice.checked;
    if (choice.checked) text.name = text.getAttribute(OTHER_NAME) ?? '';
    else text.removeAttribute('name');
  }

  /** What each behaviour shows at the start, for a form as it comes, or a row added. */
  function tidy(root: Element): void {
    for (const box of root.querySelectorAll<HTMLElement>('.jfb-other')) other(box);
    for (const control of root.querySelectorAll<HTMLInputElement | HTMLTextAreaElement>('input, textarea'))
      count(control);
    for (const area of root.querySelectorAll<HTMLTextAreaElement>('textarea[data-jfb-grow]')) grow(area);
  }

  form.addEventListener('click', (event) => {
    const button = (event.target as Element).closest?.<HTMLButtonElement>(NAV);
    if (!button) return;
    const [action] = button.value.split(':');
    const box = button.closest<HTMLElement>('.jfb-repeater');
    if (action === 'up' || action === 'down') {
      event.preventDefault();
      moveItem(button, action === 'up');
    } else if (action === 'add' && box !== null) {
      event.preventDefault();
      addRow(box);
    } else if (action === 'remove' && box !== null) {
      event.preventDefault();
      const row = button.closest<HTMLElement>('[data-jfb-row]');
      if (row !== null) removeRow(box, row);
    }
  });

  form.addEventListener('input', (event) => {
    const target = event.target;
    if (target instanceof HTMLTextAreaElement && target.hasAttribute('data-jfb-grow')) grow(target);
    if (target instanceof HTMLInputElement || target instanceof HTMLTextAreaElement) count(target);
    setTimeout(growShown);
  });
  form.addEventListener('jfb:page', () => setTimeout(growShown));
  form.addEventListener('change', (event) => {
    const field = (event.target as Element).closest?.(FIELD);
    if (field) for (const box of field.querySelectorAll<HTMLElement>('.jfb-other')) other(box);
    setTimeout(growShown);
  });

  tidy(form);
  followParents(form, model, options.api);
}

/** A field whose options a parent filters, with the fields whose answers it names. */
function cascadingFields(model: FormModel): readonly { field: ModelField; parents: readonly string[] }[] {
  const all = fieldsOf(model).flatMap((field) => [field, ...(field.fields ?? [])]);
  return all.flatMap((field) => {
    // A ranking and a grid below a parent stay with the post (ADR-0154).
    if (!['dropdown', 'radio', 'checkboxes'].includes(field.type)) return [];
    const parents: string[] = [];
    for (let key = field.options?.parent; key != null && !parents.includes(key);) {
      parents.push(key);
      key = all.find((each) => each.key === key)?.options?.parent;
    }
    return parents.length === 0 ? [] : [{ field, parents }];
  });
}

const answersOf = (reading: PageReading, key: string, row: number | null): readonly string[] => {
  for (const group of reading.groups.values()) {
    const child = row === null ? undefined : group.rows[row]?.get(key);
    if (child !== undefined) return child.values;
  }
  return reading.fields.get(key)?.values ?? [];
};

/** Asks for a cascading field's options when its parents' answers change, and writes what comes. */
function followParents(form: HTMLFormElement, model: FormModel, api: string | undefined): void {
  const fields = cascadingFields(model);
  if (fields.length === 0 || api === undefined) return;
  const last = new WeakMap<HTMLElement, string>();
  const asks = new WeakMap<HTMLElement, number>();
  let queued = false;

  // Every request from one reading of the page: an answer that comes later cannot name what has changed since.
  const look = (): void => {
    const reading = readPage(form, model);
    for (const { field, parents } of fields) {
      for (const element of form.querySelectorAll<HTMLElement>(`${FIELD}[data-jfb-field="${field.key}"]`)) {
        const row = element.closest<HTMLElement>('[data-jfb-row]')?.dataset['jfbRow'];
        const place = row === undefined ? null : Number(row);
        const query = new URLSearchParams({ culture: model.culture });
        const content = form.dataset['jfbContent'];
        if (content) query.set('contentKey', content);
        for (const parent of parents) {
          for (const value of answersOf(reading, parent, place)) query.append(`parent.${parent}`, value);
        }
        const asked = query.toString();
        const was = last.get(element);
        last.set(element, asked);
        // A field first seen with no parent answered holds what the server wrote; one with answers may not: a default,
        // what the browser restored, what was chosen before this came, or a row added below a parent outside it.
        if (was === asked || (was === undefined && !asked.includes('parent.'))) continue;
        const at = (asks.get(element) ?? 0) + 1;
        asks.set(element, at);
        void fetch(`${api}/forms/${form.dataset['jfbForm'] ?? ''}/options/${field.key}?${asked}`)
          .then((response) =>
            response.ok ? (response.json() as Promise<{ available?: boolean; options?: Offered[] }>) : null,
          )
          .catch(() => null)
          .then((answer) => {
            // A later answer of the parents has asked again: this one no longer counts. No answer leaves what shows.
            if (answer !== null && asks.get(element) === at)
              writeOptions(element, answer.options ?? [], answer.available !== false);
          });
      }
    }
  };

  const changed = (): void => {
    if (queued) return;
    queued = true;
    queueMicrotask(() => {
      queued = false;
      look();
    });
  };
  form.addEventListener('input', changed);
  form.addEventListener('change', changed);
  look();
}

/** A field's options as the answer offers them, keeping what is still offered; an answer no longer offered goes. */
function writeOptions(field: HTMLElement, offered: readonly Offered[], available: boolean): void {
  const document = field.ownerDocument;
  const select = field.querySelector('select');
  const off = field.closest('[data-jfb-hidden]') !== null;
  const current = (
    select === null
      ? [...field.querySelectorAll<HTMLInputElement>('.jfb-choices > :not(.jfb-other) input')]
      : [...select.options]
  )
    .map((each) => each.value)
    .filter((value) => value !== '');
  // What is there stays there, the focus with it.
  if (offered.length > 0 && current.join('\n') === offered.map((each) => each.value).join('\n')) return;
  const held = new Set(
    select === null
      ? [...field.querySelectorAll<HTMLInputElement>('input:checked')].map((each) => each.value)
      : [...select.selectedOptions].map((each) => each.value),
  );
  const offers = (value: string): boolean => held.has(value);

  if (select !== null) {
    const data = select.dataset;
    const line = (value: string, label: string, chosen = false): HTMLOptionElement => {
      const option = document.createElement('option');
      option.value = value;
      option.textContent = label;
      option.selected = chosen;
      return option;
    };
    const none = offered.length === 0;
    select.replaceChildren(
      ...(none
        ? [line('', (available ? data['jfbNone'] : data['jfbUnavailable']) ?? '')]
        : [
            ...(data['jfbPrompt'] === undefined ? [] : [line('', data['jfbPrompt'])]),
            ...offered.map((each) => line(each.value, each.label, offers(each.value))),
          ]),
    );
    select.disabled = none || off;
    // As the server marks a list with nothing to offer, which the rules keep off.
    select.toggleAttribute('data-jfb-disabled', none);
  } else {
    const template = field.querySelector<HTMLTemplateElement>('template[data-jfb-cascade]');
    const shown = template?.previousElementSibling;
    const list = template?.content.firstElementChild?.cloneNode(true) as HTMLElement | undefined;
    const sample = list?.querySelector<HTMLElement>('[data-jfb-sample]');
    if (template == null || !list || !sample) return;
    const base = sample.querySelector('input')?.id ?? '';
    if (offered.length === 0 && list.querySelector('.jfb-other') === null) {
      const empty = template.content.querySelector('p')?.cloneNode(true) as HTMLElement;
      empty.textContent = (available ? template.dataset['jfbNone'] : template.dataset['jfbUnavailable']) ?? '';
      shown?.replaceWith(empty);
    } else {
      const idOf = (at: number): string => (at === 0 ? base : `${base}-${String(at + 1)}`);
      const rename = (choice: Element, from: string, to: string): void => {
        for (const element of choice.querySelectorAll('*')) {
          for (const name of ['id', 'for', 'aria-describedby']) {
            const value = element.getAttribute(name);
            if (value !== null && value.startsWith(from)) element.setAttribute(name, to + value.slice(from.length));
          }
        }
      };
      offered.forEach((each, at) => {
        const choice = sample.cloneNode(true) as HTMLElement;
        choice.removeAttribute('data-jfb-sample');
        rename(choice, base, idOf(at));
        const input = choice.querySelector('input') as HTMLInputElement;
        input.value = each.value;
        input.checked = offers(each.value);
        const label = choice.querySelector('label');
        if (label !== null) label.textContent = each.label;
        const description = choice.querySelector('p');
        if (description !== null) {
          if (each.description === undefined) {
            description.remove();
            input.removeAttribute('aria-describedby');
          } else {
            description.textContent = each.description;
          }
        }
        sample.before(choice);
      });
      sample.remove();
      // "Other" stays as the visitor left it, chosen and with its text, after the options it now follows.
      const box = list.querySelector(':scope > .jfb-other');
      const was = shown?.querySelector(':scope > .jfb-other');
      if (box && was) {
        const choice = was.previousElementSibling;
        if (choice) box.previousElementSibling?.replaceWith(choice);
        box.replaceWith(was);
      }
      const last = list.querySelector(':scope > .jfb-other')?.previousElementSibling;
      const id = last?.querySelector('input')?.id;
      if (last && id !== undefined) rename(last, id, idOf(offered.length));
      for (const input of list.querySelectorAll('input')) input.disabled = off;
      shown?.replaceWith(list);
    }
  }
  // The rules and the checks read it anew, what it now requires and what no longer holds an answer.
  field.dispatchEvent(new Event('change', { bubbles: true }));
}
