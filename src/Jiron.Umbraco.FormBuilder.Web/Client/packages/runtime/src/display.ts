/**
 * Errors written on the page (ROADMAP 6.4e, ADR-0113) as Web's views write them: an inline error last in its field
 * (in the field's fieldset when a legend labels it), `aria-invalid` and the error's id in `aria-describedby` on the
 * control the error names, or on a group of choices' boxes and radio buttons, as a fieldset's role takes no invalid
 * state (6.8c), and the error summary before the required note and the pages, each from the form's
 * templates, which the theme's views render, or the markup contract's when a site's own form view has none.
 */
import { summaryHeading, type MessageWords } from '@jfb/logic';
import type { ShownError } from './messages';

const FIELD = '[data-jfb-field]';
const FOCUSABLE =
  'input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]';

/** The template of a piece of the form's markup, or null when the form has none. */
function templateOf(form: HTMLFormElement, selector: string): Element | null {
  const templates = form.querySelector<HTMLTemplateElement>(':scope > template[data-jfb-templates]');
  return templates?.content.querySelector(selector) ?? null;
}

/** A copy of a template in the form's document. */
const copyOf = (form: HTMLFormElement, template: Element): HTMLElement =>
  form.ownerDocument.importNode(template, true) as HTMLElement;

/** The elements of a field that are its own, not a field's inside it (a repeatable group's rows). */
const ownOf = (field: HTMLElement, selector: string): HTMLElement[] =>
  [...field.querySelectorAll<HTMLElement>(selector)].filter((each) => each.closest(FIELD) === field);

/** The error a field shows, or null. */
export const errorIn = (field: HTMLElement): HTMLElement | null => ownOf(field, '[data-jfb-error]')[0] ?? null;

/** Whether the form shows an error at any field. */
export const showsErrors = (form: HTMLFormElement): boolean => form.querySelector('[data-jfb-error]') !== null;

/** Adds a token to an attribute that lists ids, once. */
function addId(element: Element, name: string, id: string): void {
  const ids = (element.getAttribute(name) ?? '').split(' ').filter((each) => each.length > 0);
  if (!ids.includes(id)) element.setAttribute(name, [...ids, id].join(' '));
}

/** Takes a token out of an attribute that lists ids, and the attribute with its last one. */
function removeId(element: Element, name: string, id: string): void {
  const ids = (element.getAttribute(name) ?? '').split(' ').filter((each) => each.length > 0 && each !== id);
  if (ids.length === 0) element.removeAttribute(name);
  else element.setAttribute(name, ids.join(' '));
}

/**
 * Shows a field's error, or changes the text of the one it shows: the paragraph, `aria-invalid` and the paragraph's
 * id on the control the error names, on each box and radio button of a group of choices (a grid's of the row the
 * error names), on an email address's repeat input; the paragraph's id on another group's fieldset, and
 * `aria-invalid` on a name's or an address's parts as the error marks them.
 */
export function showFieldError(form: HTMLFormElement, field: HTMLElement, error: ShownError): void {
  const id = `${error.target}-error`;
  const fieldset = field.querySelector<HTMLElement>(':scope > fieldset');
  let paragraph = errorIn(field);
  if (paragraph === null) {
    const template = templateOf(form, '[data-jfb-error]');
    paragraph = template === null ? fallbackError(form) : copyOf(form, template);
    (fieldset ?? field).append(paragraph);
  } else if (paragraph.id !== id) {
    clearFieldError(field);
    showFieldError(form, field, error);
    return;
  }
  paragraph.id = id;
  if (paragraph.textContent !== error.message) paragraph.textContent = error.message;

  const control = form.ownerDocument.getElementById(error.target);
  const choices = ownOf(field, 'input[type="radio"], input[type="checkbox"]');
  const marked =
    fieldset === null
      ? control !== null && field.contains(control)
        ? [control]
        : []
      : choices.filter(
          (each) => error.gridRow === undefined || (each.getAttribute('name') ?? '').endsWith(`.${error.gridRow}`),
        );
  for (const each of marked) {
    each.setAttribute('aria-invalid', 'true');
    addId(each, 'aria-describedby', id);
  }
  if (fieldset !== null && choices.length === 0) addId(fieldset, 'aria-describedby', id);
  const alias = field.dataset['jfbAlias'];
  if (alias === undefined) return;
  const named = ownOf(field, 'input, select, textarea').filter((each) =>
    (each.getAttribute('name') ?? '').startsWith(`${alias}.`),
  );
  if (fieldset === null) {
    for (const each of named) {
      each.setAttribute('aria-invalid', 'true');
      addId(each, 'aria-describedby', id);
    }
  } else if (error.parts !== undefined) {
    for (const each of named) {
      const part = (each.getAttribute('name') ?? '').slice(alias.length + 1);
      if (error.parts.includes(part)) each.setAttribute('aria-invalid', 'true');
      else each.removeAttribute('aria-invalid');
    }
  }
}

/** Takes a field's error away, with every mark it put on the field's own controls. */
export function clearFieldError(field: HTMLElement): void {
  const paragraph = errorIn(field);
  if (paragraph === null) return;
  const id = paragraph.id;
  paragraph.remove();
  for (const each of ownOf(field, '[aria-invalid]')) each.removeAttribute('aria-invalid');
  if (id.length > 0) for (const each of ownOf(field, '[aria-describedby]')) removeId(each, 'aria-describedby', id);
}

/**
 * Shows the error summary with a link to each error: in place of the one the form shows, which keeps its place and
 * its entrance, or before the required note and the pages, as the form view places it.
 */
export function showSummary(form: HTMLFormElement, errors: readonly ShownError[], words: MessageWords): HTMLElement {
  const template = templateOf(form, '[data-jfb-summary]');
  const made = template === null ? fallbackSummary(form) : copyOf(form, template);
  const labelledBy = made.getAttribute('aria-labelledby');
  const heading =
    (labelledBy === null ? null : made.querySelector(`[id="${labelledBy}"]`)) ??
    made.querySelector('h1, h2, h3, h4, h5, h6');
  if (heading !== null) heading.textContent = summaryHeading(errors.length, words);
  const sample = made.querySelector('a[href]');
  const item = sample?.closest('li') ?? sample;
  const list = item?.parentElement;
  if (sample !== null && sample !== undefined && item !== null && item !== undefined && list != null) {
    item.remove();
    for (const error of errors) {
      const entry = item.cloneNode(true) as HTMLElement;
      const link = entry.matches('a') ? entry : entry.querySelector('a');
      if (link === null) continue;
      link.setAttribute('href', `#${error.target}`);
      link.textContent = error.link;
      list.append(entry);
    }
  }

  const shown = form.querySelector<HTMLElement>(':scope > [data-jfb-summary]');
  if (shown !== null) {
    shown.replaceChildren(...made.childNodes);
    return shown;
  }
  let before = form.querySelector(':scope > [data-jfb-page]');
  while (before?.previousElementSibling?.matches('p') === true) before = before.previousElementSibling;
  if (before === null) form.append(made);
  else form.insertBefore(made, before);
  return made;
}

/** Takes the error summary away. */
export function removeSummary(form: HTMLFormElement): void {
  form.querySelector(':scope > [data-jfb-summary]')?.remove();
}

/**
 * Moves the focus to the control an error's link names, or the first control inside it, with the field's label or
 * legend brought into view at once; false when the page has no such control.
 */
export function focusTarget(form: HTMLFormElement, id: string): boolean {
  const target = form.ownerDocument.getElementById(id);
  if (target === null || !form.contains(target)) return false;
  const control = target.matches(FOCUSABLE) ? target : target.querySelector<HTMLElement>(FOCUSABLE);
  if (control === null) return false;
  const field = target.closest<HTMLElement>(FIELD);
  const label = field?.querySelector(':scope > label, :scope > fieldset > legend') ?? control;
  if (typeof label.scrollIntoView === 'function') label.scrollIntoView({ block: 'start', behavior: 'instant' });
  control.focus({ preventScroll: true });
  return form.ownerDocument.activeElement === control;
}

/** The markup contract's error paragraph, for a form view without templates. */
function fallbackError(form: HTMLFormElement): HTMLElement {
  const paragraph = form.ownerDocument.createElement('p');
  paragraph.className = 'jfb-error';
  paragraph.setAttribute('data-jfb-error', '');
  return paragraph;
}

/** The markup contract's error summary, for a form view without templates. */
function fallbackSummary(form: HTMLFormElement): HTMLElement {
  const document = form.ownerDocument;
  const summary = document.createElement('div');
  summary.className = 'jfb-error-summary';
  summary.setAttribute('data-jfb-summary', '');
  summary.setAttribute('role', 'alert');
  summary.setAttribute('tabindex', '-1');
  summary.setAttribute('aria-labelledby', `${form.id}-summary`);
  const heading = document.createElement('h2');
  heading.id = `${form.id}-summary`;
  const list = document.createElement('ul');
  list.setAttribute('role', 'list');
  const item = document.createElement('li');
  const link = document.createElement('a');
  link.setAttribute('href', '#');
  item.append(link);
  list.append(item);
  summary.append(heading, list);
  return summary;
}
