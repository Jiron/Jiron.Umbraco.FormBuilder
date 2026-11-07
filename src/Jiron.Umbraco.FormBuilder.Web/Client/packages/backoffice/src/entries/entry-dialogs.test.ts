// The dialogs of the entries lists (ROADMAP 11.1, IA 6.1, ADR-0207, ADR-0209): the filter's State part offering the
// states of its place, named in each language's headline; the column chooser's line where no single form is chosen; and
// the Form filter's search, ticks and refusals, against a fake of the forms search.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { EntryListFormResponseModel } from '../api';
import de from '../lang/entries.de';
import { all, must, registerEnglish, settle, text } from '../testing';
import { INBOX_STATES, emptyFilter } from './entry-list-model';
import { MOST_FORMS, type EntryFormsModalValue, type NamedForm } from './entry-modals';
import { loadEntryWords } from './entry-words';

/** The forms the user may view entries of, as the search finds them by name. */
const server = vi.hoisted(() => ({
  forms: [] as EntryListFormResponseModel[],
  searches: [] as string[],
  fails: false,
}));

vi.mock('./entries-api', () => ({
  findForms: async (_host: unknown, search: string) => {
    server.searches.push(search);
    if (server.fails) return undefined;
    const wanted = search.trim().toLowerCase();
    return server.forms.filter((form) => form.name.toLowerCase().includes(wanted));
  },
}));

await import('./entry-filter.modal.element');
await import('./entry-columns.modal.element');
await import('./entry-forms.modal.element');

const named = (key: string, name: string): EntryListFormResponseModel => ({ key, name, permissions: [] });

/** A modal context as Umbraco's holds the value: the dialog writes it, then submits. */
function modalContext() {
  const state = new UmbObjectState<object>({});
  return {
    value: state.asObservable(),
    setValue: (next: object) => state.setValue(next),
    updateValue: (partial: object) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function open(tag: string, data: unknown) {
  const context = modalContext();
  const element = document.createElement(tag) as HTMLElement & {
    modalContext: unknown;
    data: unknown;
    updateComplete: Promise<unknown>;
  };
  element.modalContext = context;
  element.data = data;
  document.body.append(element);
  await settle(element);
  const boxes = () => all<HTMLInputElement>(element, 'uui-checkbox');
  const box = (label: string) =>
    must(
      boxes().find((one) => one.getAttribute('label') === label),
      `the box ${label}`,
    );
  const tick = async (label: string, ticked = true) => {
    const one = box(label);
    one.checked = ticked;
    one.dispatchEvent(new Event('change'));
    await settle(element);
  };
  const button = (label: string) =>
    must(
      all(element, 'uui-button').find((one) => one.getAttribute('label') === label),
      `the button ${label}`,
    );
  return { element, context, boxes, box, tick, button };
}

beforeAll(async () => {
  registerEnglish();
  await loadEntryWords('en');
});

beforeEach(() => {
  server.forms = [named('f1', 'Contact'), named('f2', 'Job application'), named('f3', 'Newsletter')];
  server.searches = [];
  server.fails = false;
});

afterEach(() => {
  vi.useRealTimers();
  document.body.replaceChildren();
});

describe('the State part of the filter', () => {
  it('offers the states of its place, and names the part as each language writes it in its headline', async () => {
    const { element, boxes } = await open('jfb-entry-filter-modal', {
      part: 'state',
      filter: emptyFilter(),
      states: INBOX_STATES,
    });

    expect(boxes().map((one) => one.getAttribute('label'))).toEqual([
      'Waiting for confirmation',
      'Waiting for approval',
      'Approved',
      'Rejected',
    ]);
    expect(element.shadowRoot?.querySelector('uui-dialog-layout')?.getAttribute('headline')).toBe('Filter by state');
    // German writes its nouns with a capital, also within a sentence.
    const words = de.jfbEntries;
    expect(words.edit_headline(words.edit_part_state)).toBe('Nach Status filtern');
    expect(words.edit_headline(words.edit_part_form)).toBe('Nach Formular filtern');
  });
});

describe('the column chooser', () => {
  it('says in the fields\u2019 place that filtering by one form offers its fields', async () => {
    const { element } = await open('jfb-entry-columns-modal', { fields: [], columns: [], noForm: true });

    expect(text(element.shadowRoot?.querySelector('uui-box'))).toContain(
      "Filter the list by one form to choose that form's fields as columns.",
    );
  });
});

describe('the Form filter', () => {
  const chosenOf = (context: ReturnType<typeof modalContext>) =>
    (context.getValue() as EntryFormsModalValue | undefined)?.forms.map((form: NamedForm) => form.key);

  it('lists the chosen forms first, ticked, then the forms it finds, and answers the forms ticked', async () => {
    const { element, boxes, tick, button, context } = await open('jfb-entry-forms-modal', {
      chosen: [{ key: 'f3', name: 'Newsletter' }],
    });

    expect(boxes().map((one) => [one.getAttribute('label'), one.checked])).toEqual([
      ['Newsletter', true],
      ['Contact', false],
      ['Job application', false],
    ]);
    await tick('Job application');
    button('Apply filter').click();
    await settle(element);

    expect(context.submit).toHaveBeenCalled();
    expect(chosenOf(context)).toEqual(['f3', 'f2']);
  });

  it('searches as the user stops typing, at once on Enter, and says when nothing matches', async () => {
    const { element, boxes } = await open('jfb-entry-forms-modal', { chosen: [] });
    const search = must(element.shadowRoot?.querySelector<HTMLInputElement>('uui-input'), 'the search');

    search.value = 'job';
    search.dispatchEvent(new Event('input'));
    expect(server.searches).toEqual(['']);
    await vi.waitFor(() => expect(boxes().map((one) => one.getAttribute('label'))).toEqual(['Job application']));

    search.value = 'quiz';
    search.dispatchEvent(new Event('input'));
    search.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, composed: true }));
    await settle(element);
    expect(server.searches).toEqual(['', 'job', 'quiz']);
    expect(text(element.shadowRoot?.querySelector('[role="status"]'))).toBe(
      'No form you may view has \u201cquiz\u201d in its name.',
    );
  });

  it('refuses to apply without a form, and offers Try again when the search fails', async () => {
    server.fails = true;
    const { element, button, context } = await open('jfb-entry-forms-modal', { chosen: [] });

    expect(text(element.shadowRoot?.querySelector('[role="alert"]'))).toContain("The forms couldn't be found.");
    server.fails = false;
    button('Try again').click();
    await settle(element);
    expect(element.shadowRoot?.querySelector('[role="alert"]')).toBeNull();

    button('Apply filter').click();
    await settle(element);
    expect(context.submit).not.toHaveBeenCalled();
    expect(text(element.shadowRoot?.querySelector('.problem'))).toBe('Choose at least one.');
  });

  it('stops at the most forms a list is filtered by', async () => {
    const chosen = Array.from({ length: MOST_FORMS }, (_, index) => ({ key: `c${index}`, name: `Chosen ${index}` }));
    const { element, box } = await open('jfb-entry-forms-modal', { chosen });

    expect(box('Contact').hasAttribute('disabled')).toBe(true);
    expect(box('Chosen 0').hasAttribute('disabled')).toBe(false);
    expect(text(element.shadowRoot?.querySelector('.note'))).toBe('A list is filtered by up to 100 forms.');
  });
});
