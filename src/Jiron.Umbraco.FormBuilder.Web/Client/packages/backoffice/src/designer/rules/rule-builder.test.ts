// The rules builder (ROADMAP 9.5a, IA 5.1, ADR-0175): its first line, a condition's question, operator and value as
// each kind takes them, another answer and a day counted from today, groups, why a rule can't be kept said at its row
// or its first line, a rule moved to another element, Try it, the parts of the visit, and Delete.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import type { DesignDefinition, DesignField, DesignRule } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import { RuleBuilderModalElement } from './rule-builder.modal.element';
import type { RuleBuilderModalData, RuleBuilderModalValue } from './rule-builder.modal.token';
import { elementNames, typeFactsOf } from './rule-names';

const groups = vi.hoisted(() => ({ names: ['Gold', 'Silver'] as string[] | undefined }));

vi.mock('./rule-data', () => ({ readMemberGroups: async () => groups.names }));
vi.mock('../inspector/option-data', () => ({
  resolveOptions: async (_host: unknown, _form: string, field: DesignField) => ({
    isAvailable: true,
    options:
      field.key === 'topics'
        ? [
            { value: 'shipping', label: 'Shipping', parentValues: [] },
            { value: 'press', label: 'Press', parentValues: [] },
          ]
        : [
            { value: 'de', label: 'Germany', parentValues: [] },
            { value: 'other', label: 'Other', parentValues: [] },
          ],
  }),
}));

const field = (key: string, type: string, label: string, extra: Partial<DesignField> = {}): DesignField => ({
  key,
  type,
  alias: key,
  label: { '': label },
  ...extra,
});

const definitionWith = (rules: readonly DesignRule[] = []): DesignDefinition => ({
  pages: [
    {
      key: 'p1',
      title: { '': 'About you' },
      groups: [
        {
          key: 'g1',
          fields: [
            field('country', 'dropdown', 'Country'),
            field('which', 'text', 'Which', { rules }),
            field('topics', 'checkboxes', 'Topics'),
            field('agree', 'checkbox', 'Agree'),
          ],
        },
      ],
    },
    {
      key: 'p2',
      title: { '': 'Your trip' },
      groups: [
        {
          key: 'g2',
          fields: [
            field('arrival', 'date', 'Arrival'),
            field('departure', 'date', 'Departure'),
            field('guests', 'number', 'Guests'),
          ],
        },
      ],
    },
  ],
  settings: { developer: { queryValues: ['ref'] } },
});

function modalContext() {
  const state = new UmbObjectState<Partial<RuleBuilderModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: RuleBuilderModalValue) => state.setValue(next),
    updateValue: (partial: Partial<RuleBuilderModalValue>) => state.update(partial),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

const term = (key: string) => key;

function dataOf(extra: Partial<RuleBuilderModalData> = {}): RuleBuilderModalData {
  const definition = extra.definition ?? definitionWith();
  return {
    definition,
    form: 'form',
    origin: 'which',
    rule: null,
    effect: 'show',
    culture: 'en-US',
    languages: [
      { culture: 'en-US', name: 'English' },
      { culture: 'de-DE', name: 'German' },
    ],
    types: typeFactsOf(builtInTypes),
    names: elementNames(definition, '', term),
    refusal: () => null,
    ...extra,
  };
}

async function builderOf(extra: Partial<RuleBuilderModalData> = {}) {
  const modal = modalContext();
  const element = new RuleBuilderModalElement();
  element.modalContext = modal as never;
  element.data = dataOf(extra);
  document.body.append(element);
  await settle(element);
  return { element, modal, root: shadow(element) };
}

const pick = async (element: RuleBuilderModalElement, select: HTMLSelectElement, value: string) => {
  select.value = value;
  select.dispatchEvent(new Event('change'));
  await settle(element);
};

const typeIn = async (
  element: RuleBuilderModalElement,
  input: HTMLInputElement | HTMLTextAreaElement,
  value: string,
) => {
  input.value = value;
  input.dispatchEvent(new Event('input'));
  await settle(element);
};

const row = (root: ShadowRoot, at = 0) =>
  must(root.querySelectorAll<HTMLElement>('.condition')[at], `condition ${at + 1}`);
const inRow = <E extends Element>(root: ShadowRoot, selector: string, at = 0) =>
  must(row(root, at).querySelector<E>(selector), `${selector} of condition ${at + 1}`);
const options = (select: HTMLSelectElement) => [...select.options].map((option) => text(option));
const keep = (root: ShadowRoot) => must(root.querySelector<HTMLElement>('uui-button[look="primary"]'), 'Add or Update');

beforeAll(registerEnglish);

beforeEach(() => {
  groups.names = ['Gold', 'Silver'];
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the rules builder', () => {
  it('opens a new rule on its element with one empty condition, which takes the focus', async () => {
    const opened = vi.fn();
    const closed = vi.fn();
    const { element, root } = await builderOf({ opened, closed });
    // The Design view waits for the builder to be gone before it sets the focus (ADR-0175).
    expect([opened.mock.calls.length, closed.mock.calls.length]).toEqual([1, 0]);
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('New rule');
    expect(options(must(root.querySelector<HTMLSelectElement>('#effect'), 'what it does'))).toEqual([
      'Show',
      'Hide',
      'Require',
    ]);
    const target = must(root.querySelector<HTMLSelectElement>('#target'), 'to what');
    expect(target.value).toBe('which');
    expect(text(target.options[0])).toBe('this field: Which');
    expect(options(target)).toContain('About you (page)');
    expect(text(root.querySelector('.first-line'))).toMatch(/when$/);
    expect(root.querySelectorAll('.condition')).toHaveLength(1);
    const subject = inRow<HTMLSelectElement>(root, 'select.subject');
    expect([...subject.querySelectorAll('optgroup')].map((group) => group.label)).toEqual([
      'About you',
      'Your trip',
      'About the visit',
    ]);
    // The rule's own element and what a show rule would wait on are not offered.
    expect(options(subject)).not.toContain('Which');
    expect(options(subject).slice(-5)).toEqual([
      'The language',
      'The visitor',
      'Member groups',
      'The query value ref',
      "Today's date",
    ]);
    expect(row(root).querySelector('select.operator')).toBeNull();
    // "All" or "any" says nothing of one condition.
    expect(root.querySelector('.match')).toBeNull();
    await new Promise((done) => requestAnimationFrame(() => done(null)));
    expect(root.activeElement).toBe(subject);
    // A new rule has nothing to delete.
    expect(root.querySelector('uui-button[slot="footer-info"]')).toBeNull();
    element.remove();
    expect([opened.mock.calls.length, closed.mock.calls.length]).toEqual([1, 1]);
  });

  it('builds a condition from a question, its operators and an option, and adds the rule', async () => {
    const { element, modal, root } = await builderOf();
    await pick(element, inRow(root, 'select.subject'), 'field:country');
    const operator = inRow<HTMLSelectElement>(root, 'select.operator');
    expect(options(operator)).toEqual(['is', 'is not', 'is one of', 'is none of', 'is empty', 'is not empty']);
    const value = inRow<HTMLSelectElement>(root, 'select.plain');
    expect(options(value)).toEqual(['Choose a value', 'Germany', 'Other']);
    await pick(element, value, 'other');
    keep(root).click();
    await settle(element);
    expect(modal.getValue()).toEqual({
      target: 'which',
      rule: expect.objectContaining({
        effect: 'show',
        enabled: true,
        match: 'all',
        conditions: [{ field: 'country', op: 'equals', value: 'other' }],
      }) as unknown,
    });
    expect(modal.submit).toHaveBeenCalled();
  });

  it('ticks options for "is one of", and takes texts one per line', async () => {
    const { element, modal, root } = await builderOf();
    await pick(element, inRow(root, 'select.subject'), 'field:topics');
    expect(options(inRow(root, 'select.operator'))[0]).toBe('has ticked');
    await pick(element, inRow(root, 'select.operator'), 'isOneOf');
    const boxes = [...row(root).querySelectorAll<HTMLInputElement>('fieldset input[type="checkbox"]')];
    expect(boxes.map((box) => text(box.parentElement))).toEqual(['Shipping', 'Press']);
    boxes[1]?.click();
    await settle(element);
    boxes[0]?.click();
    await settle(element);
    keep(root).click();
    await settle(element);
    expect(modal.getValue().rule?.conditions).toEqual([
      { field: 'topics', op: 'isOneOf', value: ['shipping', 'press'] },
    ]);
  });

  it("refuses Add while a row lacks something or the engine's check refuses it, saying why at the row", async () => {
    const { element, modal, root } = await builderOf();
    keep(root).click();
    await settle(element);
    expect(modal.submit).not.toHaveBeenCalled();
    const subject = inRow<HTMLSelectElement>(root, 'select.subject');
    expect(text(row(root).querySelector('.problem'))).toBe('Choose what this condition looks at.');
    expect(subject.getAttribute('aria-invalid')).toBe('true');
    expect(subject.getAttribute('aria-describedby')).toBe(row(root).querySelector('.problem')?.id);
    expect(root.activeElement).toBe(subject);
    await pick(element, subject, 'field:guests');
    await pick(element, inRow(root, 'select.operator'), 'greaterThan');
    await typeIn(element, inRow(root, 'input.plain'), '2,5');
    keep(root).click();
    await settle(element);
    expect(text(row(root).querySelector('.problem'))).toBe('Give a number, such as 12 or 12.5.');
    expect(root.activeElement).toBe(row(root).querySelector('input.plain'));
    await typeIn(element, inRow(root, 'input.plain'), '2.5');
    expect(row(root).querySelector('.problem')).toBeNull();
    keep(root).click();
    await settle(element);
    expect(modal.submit).toHaveBeenCalled();
  });

  it('compares with another answer of the same kind, or a day counted from today', async () => {
    const { element, modal, root } = await builderOf();
    await pick(element, inRow(root, 'select.subject'), 'field:departure');
    await pick(element, inRow(root, 'select.operator'), 'greaterThan');
    expect(options(inRow(root, 'select.operator'))).toContain('is after');
    const source = inRow<HTMLSelectElement>(root, 'select.source');
    expect(options(source)).toEqual(['a value', 'a day counted from today', 'the answer to Arrival']);
    await pick(element, source, 'field:arrival');
    expect(row(root).querySelector('input')).toBeNull();
    keep(root).click();
    await settle(element);
    expect(modal.getValue().rule?.conditions).toEqual([
      { field: 'departure', op: 'greaterThan', value: { field: 'arrival' } },
    ]);

    const counted = await builderOf();
    await pick(counted.element, inRow(counted.root, 'select.subject'), 'field:arrival');
    await pick(counted.element, inRow(counted.root, 'select.operator'), 'lessThan');
    await pick(counted.element, inRow(counted.root, 'select.source'), 'today');
    await typeIn(counted.element, inRow(counted.root, 'input.amount'), '2');
    const [unit, direction] = [...row(counted.root).querySelectorAll<HTMLSelectElement>('.value select:not(.source)')];
    await pick(counted.element, must(unit, 'the unit'), 'w');
    await pick(counted.element, must(direction, 'before or after'), 'before');
    keep(counted.root).click();
    await settle(counted.element);
    expect(counted.modal.getValue().rule?.conditions).toEqual([
      { field: 'arrival', op: 'lessThan', value: { today: '-2w' } },
    ]);
  });

  it('groups conditions, all or any, and takes a group away with its last condition', async () => {
    const { element, root } = await builderOf();
    const adds = () => all<HTMLElement>(element, '.adds uui-button:not(.remove-group)');
    expect(adds().map((button) => button.getAttribute('label'))).toEqual(['Add condition', 'Add condition group']);
    adds()[0]?.click();
    await settle(element);
    expect(root.querySelectorAll('.condition')).toHaveLength(2);
    expect(root.activeElement).toBe(inRow(root, 'select.subject', 1));
    expect(options(must(root.querySelector<HTMLSelectElement>('.match select'), 'the match'))).toEqual([
      'all of these are true',
      'any of these is true',
    ]);
    expect(text(root.querySelector('.joiner'))).toBe('and');
    adds()[1]?.click();
    await settle(element);
    const nested = all<HTMLElement>(element, '.nested');
    expect(nested.map((group) => group.getAttribute('aria-label'))).toEqual(['Condition group 1', 'Condition group 2']);
    // The rule's own two conditions are the first group; any of the groups now.
    expect(
      must(root.querySelector<HTMLSelectElement>('[data-id="root"] > .match select'), 'the rule match').value,
    ).toBe('any');
    expect(text(root.querySelector('[data-id="root"] > .joiner'))).toBe('or');
    const removeGroup = must(nested[1]?.querySelector<HTMLElement>('.adds .remove-group'), 'Remove group 2');
    expect(removeGroup.getAttribute('label')).toBe('Remove condition group 2');
    must(nested[1]?.querySelector<HTMLElement>('.condition uui-button'), 'its condition').click();
    await settle(element);
    expect(all(element, '.nested')).toHaveLength(1);
    // A group removed leaves the focus on the condition before it.
    adds()[1]?.click();
    await settle(element);
    must(all<HTMLElement>(element, '.nested')[1]?.querySelector<HTMLElement>('.adds .remove-group'), 'remove').click();
    await settle(element);
    expect(root.activeElement).toBe(inRow(root, 'select.subject', 1));
  });

  it("says at the first line that the element has such a rule already, and doesn't keep it", async () => {
    const show: DesignRule = { key: 'old', effect: 'show', conditions: [{ field: 'agree', op: 'isChecked' }] };
    const { element, modal, root } = await builderOf({ definition: definitionWith([show]), effect: 'hide' });
    await pick(element, inRow(root, 'select.subject'), 'field:agree');
    expect(text(root.querySelector('#first-line-why'))).toBe(
      'One rule decides whether Which shows, and it has that rule already. Change that one instead.',
    );
    keep(root).click();
    await settle(element);
    expect(modal.submit).not.toHaveBeenCalled();
    expect(root.activeElement).toBe(root.querySelector('#effect'));
    await pick(element, must(root.querySelector<HTMLSelectElement>('#effect'), 'what it does'), 'require');
    expect(root.querySelector('#first-line-why')).toBeNull();
  });

  it('says at the first line that the element would wait on itself, as the editor model refuses it', async () => {
    const { element, modal, root } = await builderOf({ refusal: () => 'cycle' });
    await pick(element, inRow(root, 'select.subject'), 'field:agree');
    keep(root).click();
    await settle(element);
    expect(modal.submit).not.toHaveBeenCalled();
    expect(text(root.querySelector('#first-line-why'))).toBe(
      'With this rule, whether Which shows would depend on itself. Choose another question, or another element.',
    );
    const target = must(root.querySelector('#target'), 'to what');
    expect(root.activeElement).toBe(target);
    expect([target.getAttribute('aria-invalid'), target.getAttribute('aria-describedby')]).toEqual([
      'true',
      'first-line-why',
    ]);
  });

  it('moves the rule to another element, an empty one then looking at the question it was started from', async () => {
    const { element, modal, root } = await builderOf({ origin: 'country' });
    const target = must(root.querySelector<HTMLSelectElement>('#target'), 'to what');
    expect(text(target.options[0])).toBe('this field: Country');
    await pick(element, target, 'which');
    expect(inRow<HTMLSelectElement>(root, 'select.subject').value).toBe('field:country');
    await pick(element, inRow(root, 'select.plain'), 'other');
    keep(root).click();
    await settle(element);
    expect(modal.getValue().target).toBe('which');
  });

  it('takes the first effect a new element offers when it lacks the one chosen, and says the switch for it', async () => {
    const { element, root } = await builderOf({ effect: 'require' });
    expect(text(root.querySelector('.use .help'))).toBe(
      'Turn it off to keep the rule without using it. The field is then never required.',
    );
    await pick(element, must(root.querySelector<HTMLSelectElement>('#target'), 'to what'), 'p1');
    const effect = must(root.querySelector<HTMLSelectElement>('#effect'), 'what it does');
    expect([effect.value, ...options(effect)]).toEqual(['show', 'Show', 'Hide']);
    expect(text(root.querySelector('.use .help'))).toBe(
      'Turn it off to keep the rule without using it. The page then always shows.',
    );
  });

  it('offers no condition group inside the tenth level', async () => {
    const leaf = { field: 'agree', op: 'isChecked' };
    interface Nested {
      match: 'all';
      conditions: unknown[];
    }
    const level = (depth: number): Nested =>
      depth === 10 ? { match: 'all', conditions: [leaf] } : { match: 'all', conditions: [leaf, level(depth + 1)] };
    const rule = { key: 'r', effect: 'show', conditions: [leaf, level(2)] } as unknown as DesignRule;
    const { element } = await builderOf({ definition: definitionWith([rule]), rule });
    const labels = (adds: Element | undefined) =>
      [...must(adds, 'its adds').querySelectorAll('uui-button:not(.remove-group)')].map((button) =>
        button.getAttribute('label'),
      );
    // The innermost group's adds come first in the document, then its parent's.
    const adds = all<HTMLElement>(element, '.adds');
    expect(adds).toHaveLength(10);
    expect(labels(adds[0])).toEqual(['Add condition']);
    expect(labels(adds[1])).toEqual(['Add condition', 'Add condition group']);
  });

  it('tries the rule with answers given, saying what the element then does', async () => {
    const rule: DesignRule = {
      key: 'r',
      effect: 'show',
      conditions: [{ field: 'country', op: 'equals', value: 'other' }],
    };
    const { element, root } = await builderOf({ definition: definitionWith([rule]), rule });
    const tryIt = must(root.querySelector<HTMLButtonElement>('.try button.disclosure'), 'Try it');
    expect(tryIt.getAttribute('aria-controls')).toBe('try-panel');
    expect(tryIt.getAttribute('aria-expanded')).toBe('false');
    tryIt.click();
    await settle(element);
    expect(tryIt.getAttribute('aria-expanded')).toBe('true');
    const verdict = must(root.querySelector('.verdict'), 'the verdict');
    expect(verdict.getAttribute('role')).toBe('status');
    expect(text(verdict)).toBe('Which stays hidden.');
    const country = must(root.querySelector<HTMLSelectElement>('.try-boxes select'), "Country's box");
    await pick(element, country, 'other');
    expect(text(root.querySelector('.verdict'))).toBe('Which shows.');
    const use = must(root.querySelector<HTMLElement & { checked: boolean }>('uui-toggle'), 'Use this rule');
    use.checked = false;
    use.dispatchEvent(new Event('change'));
    await settle(element);
    // A rule that is off does nothing; the switch's help says why, once.
    expect(text(root.querySelector('.verdict'))).toBe('Which shows.');
    expect(root.querySelector('label[for="try-country"]')).not.toBeNull();
    expect(text(root.querySelector('.use .help'))).toBe(
      'Turn it off to keep the rule without using it. The field then always shows.',
    );
    must(row(root).querySelector<HTMLElement>('uui-button'), 'remove').click();
    await settle(element);
    expect(text(root.querySelector('.verdict'))).toBe('Complete every condition to try the rule.');
  });

  it("doesn't try a rule while a row says something is wrong, even one the engine would run", async () => {
    // An empty answer is a value the engine compares; the builder asks for one.
    const rule: DesignRule = {
      key: 'r',
      effect: 'show',
      conditions: [{ field: 'country', op: 'equals', value: '' }],
    };
    const { element, root } = await builderOf({ definition: definitionWith([rule]), rule });
    must(root.querySelector<HTMLButtonElement>('.try button.disclosure'), 'Try it').click();
    await settle(element);
    expect(text(root.querySelector('.verdict'))).toBe('Complete every condition to try the rule.');
  });

  it("looks at the visit: the site's languages, the member, the groups or a typed group, a query value, today", async () => {
    const { element, modal, root } = await builderOf({ effect: 'require' });
    await pick(element, inRow(root, 'select.subject'), 'context:culture');
    expect(options(inRow(root, 'select.plain'))).toEqual(['Choose a value', 'English', 'German']);
    await pick(element, inRow(root, 'select.subject'), 'context:memberSignedIn');
    expect(options(inRow(root, 'select.operator'))).toEqual(['is signed in', 'is not signed in']);
    expect(row(root).querySelector('.value')).toBeNull();
    await pick(element, inRow(root, 'select.subject'), 'context:memberGroup');
    expect(options(inRow(root, 'select.operator'))).toEqual([
      'include',
      "don't include",
      'include one of',
      'include none of',
    ]);
    expect(options(inRow(root, 'select.plain'))).toEqual(['Choose a value', 'Gold', 'Silver']);
    await pick(element, inRow(root, 'select.subject'), 'context:today');
    // Today's date is compared with a date, or with a date answer; never counted from itself.
    expect(options(inRow(root, 'select.source'))).toEqual([
      'a value',
      'the answer to Arrival',
      'the answer to Departure',
    ]);
    expect(inRow<HTMLInputElement>(root, 'input.plain').type).toBe('date');
    await pick(element, inRow(root, 'select.subject'), 'context:query.ref');
    await typeIn(element, inRow(root, 'input.plain'), 'web');
    keep(root).click();
    await settle(element);
    expect(modal.getValue().rule).toMatchObject({
      effect: 'require',
      conditions: [{ context: 'query.ref', op: 'equals', value: 'web' }],
    });

    groups.names = undefined;
    const typed = await builderOf();
    await pick(typed.element, inRow(typed.root, 'select.subject'), 'context:memberGroup');
    expect(inRow<HTMLInputElement>(typed.root, 'input.plain').type).toBe('text');
  });

  it('opens a broken rule with its rows saying why, each on the control it is about, and keeps what lists hold', async () => {
    const rule: DesignRule = {
      key: 'r',
      effect: 'show',
      conditions: [
        { field: 'departure', op: 'greaterThan', value: { field: 'gone' } },
        { field: 'agree', op: 'isChecked', value: 'true' },
        { field: 'country', op: 'isOneOf', value: ['de', { field: 'gone' }] },
      ],
    };
    const { element, modal, root } = await builderOf({ definition: definitionWith([rule]), rule });
    // Said as the rule opens, without Update pressed.
    expect(text(row(root, 0).querySelector('.problem'))).toBe(
      'The answer it compared with is gone. Choose what it compares with.',
    );
    const source = inRow<HTMLSelectElement>(root, 'select.source');
    expect([source.getAttribute('aria-invalid'), text(source.selectedOptions[0])]).toEqual([
      'true',
      'the answer to a field since deleted',
    ]);
    expect(inRow<HTMLSelectElement>(root, 'select.subject').getAttribute('aria-invalid')).toBe('false');
    expect(text(row(root, 1).querySelector('.problem'))).toBe(
      'This way of comparing takes no value, but the condition holds one.',
    );
    // A tick keeps the other answer the boxes can't show.
    expect(text(row(root, 2).querySelector('.value .help'))).toBe('Also kept: the answer to a field since deleted');
    must(row(root, 2).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[1], 'Other').click();
    await settle(element);
    keep(root).click();
    await settle(element);
    expect(modal.submit).not.toHaveBeenCalled();
    expect(root.activeElement).toBe(source);
    await pick(element, source, 'field:arrival');
    inRow<HTMLButtonElement>(root, 'button.remove-value', 1).click();
    await settle(element);
    expect(row(root, 1).querySelector('.problem')).toBeNull();
    must(row(root, 2).querySelector<HTMLElement>('uui-button'), 'remove').click();
    await settle(element);
    keep(root).click();
    await settle(element);
    expect(modal.getValue().rule?.conditions).toEqual([
      { field: 'departure', op: 'greaterThan', value: { field: 'arrival' } },
      { field: 'agree', op: 'isChecked' },
    ]);
  });

  it('keeps the other answers of a list as its options are ticked', async () => {
    const rule: DesignRule = {
      key: 'r',
      effect: 'show',
      conditions: [{ field: 'country', op: 'isOneOf', value: ['de', { field: 'agree' }] }],
    };
    const { element, modal, root } = await builderOf({ definition: definitionWith([rule]), rule });
    must(row(root).querySelectorAll<HTMLInputElement>('input[type="checkbox"]')[1], 'Other').click();
    await settle(element);
    keep(root).click();
    await settle(element);
    expect(modal.getValue().rule?.conditions).toEqual([
      { field: 'country', op: 'isOneOf', value: ['de', 'other', { field: 'agree' }] },
    ]);
  });

  it('opens a rule as it is, an operator and an option the lists lack kept, and deletes it', async () => {
    const rule: DesignRule = {
      key: 'r',
      effect: 'show',
      conditions: [
        { field: 'country', op: 'contains', value: 'x' },
        { field: 'topics', op: 'isChecked', value: 'gone' },
      ],
    };
    const { element, modal, root } = await builderOf({ definition: definitionWith([rule]), rule });
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Rule');
    expect(inRow<HTMLSelectElement>(root, 'select.operator').value).toBe('contains');
    expect(inRow<HTMLSelectElement>(root, 'select.plain', 1).value).toBe('gone');
    keep(root).click();
    await settle(element);
    // A type change can leave an operator the kind doesn't take (ADR-0174): the row says so.
    expect(text(row(root).querySelector('.problem'))).toBe(
      "These answers can't be compared this way. Choose another way.",
    );
    const remove = must(root.querySelector<HTMLElement>('uui-button[slot="footer-info"]'), 'Delete rule');
    expect(remove.getAttribute('label')).toBe('Delete rule');
    remove.click();
    await settle(element);
    expect(modal.getValue()).toEqual({ target: 'which', rule: null });
    expect(modal.submit).toHaveBeenCalled();
  });
});
