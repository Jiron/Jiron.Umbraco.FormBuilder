// The rules overview (ROADMAP 9.5c, IA 5.1, ADR-0178): every rule by page, a row leading to its element, the outcomes'
// under After sending, a conflict said on its row, Add rule asking first what it affects, and the empty sentence.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, text } from '../../testing';
import type { DesignDefinition, DesignField, DesignRule } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import { RulesOverviewModalElement } from './rules-overview.modal.element';
import type { RulesOverviewModalData, RulesOverviewModalValue } from './rules-overview.modal.token';

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      title: { '': 'Your details' },
      groups: [
        {
          key: 'g1',
          fields: [
            {
              key: 'topic',
              type: 'dropdown',
              alias: 'topic',
              label: { '': 'Topic' },
              options: {
                items: [
                  { value: 'shipping', label: { '': 'Shipping' } },
                  { value: 'press', label: { '': 'Press' } },
                ],
              },
            },
            {
              key: 'attachment',
              type: 'file',
              alias: 'attachment',
              label: { '': 'Attachment' },
              rules: [
                { key: 'h', effect: 'hide', conditions: [{ field: 'topic', op: 'equals', value: 'press' }] },
                {
                  key: 'r',
                  effect: 'require',
                  conditions: [{ field: 'topic', op: 'isOneOf', value: ['shipping', 'press'] }],
                },
              ],
            },
          ],
        },
      ],
    },
    {
      key: 'p2',
      groups: [
        {
          key: 'g2',
          title: { '': 'Delivery' },
          rules: [
            {
              key: 'g',
              effect: 'show',
              enabled: false,
              conditions: [{ field: 'topic', op: 'equals', value: 'shipping' }],
            },
          ],
          fields: [{ key: 'email', type: 'email', alias: 'email', label: { '': 'Email' } }],
        },
      ],
    },
    { key: 'p3', title: { '': 'Nothing here' }, groups: [{ key: 'g3', fields: [] }] },
  ],
  validationRules: [
    {
      key: 'v',
      field: 'email',
      message: { '': 'Use your work address' },
      conditions: [{ field: 'email', op: 'endsWith', value: 'gmail.com' }],
    },
  ],
  outcomes: [
    {
      key: 'o1',
      kind: 'redirect',
      redirect: { url: '/press/thanks' },
      rules: [{ key: 'u', effect: 'use', conditions: [{ field: 'topic', op: 'equals', value: 'press' }] }],
    },
    { key: 'o2', kind: 'message', message: { '': '<p>Thanks</p>' } },
  ],
};

function modalContext() {
  const state = new UmbObjectState<Partial<RulesOverviewModalValue>>({});
  return {
    value: state.asObservable(),
    setValue: (next: RulesOverviewModalValue) => state.setValue(next),
    updateValue: (partial: Partial<RulesOverviewModalValue>) => state.update(partial as never),
    getValue: () => state.getValue(),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function overviewOf(definition = DEFINITION, extra: Partial<RulesOverviewModalData> = {}) {
  const modal = modalContext();
  const element = new RulesOverviewModalElement();
  element.modalContext = modal as never;
  element.data = {
    definition,
    types: builtInTypes,
    culture: 'en-US',
    languages: [{ culture: 'en-US', name: 'English' }],
    ...extra,
  };
  document.body.append(element);
  await settle(element);
  return { element, modal, root: shadow(element) };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('the rules overview', () => {
  it('lists every rule by page, each row naming its element, then the outcomes under After sending', async () => {
    const opened = vi.fn();
    const closed = vi.fn();
    const { element, root } = await overviewOf(DEFINITION, { opened, closed });
    expect(opened).toHaveBeenCalledOnce();
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Rules');
    const boxes = all<HTMLElement>(element, 'uui-box');
    expect(boxes.map((box) => box.getAttribute('headline'))).toEqual([
      'Page 1: Your details',
      'Page 2',
      'After sending',
    ]);
    const rows = boxes.map((box) => [...box.querySelectorAll<HTMLButtonElement>('button.row')].map((row) => text(row)));
    expect(rows[1]).toEqual([
      'Delivery (group) Shown when Topic is Shipping Off',
      'Email Shows "Use your work address" when Email ends with "gmail.com"',
    ]);
    expect(rows[2]).toEqual(['Redirect to /press/thanks Used when Topic is Press']);
    element.remove();
    expect(closed).toHaveBeenCalledOnce();
  });

  it('warns on the row of a field whose hide and require rules hold for the same answer', async () => {
    const { element } = await overviewOf();
    const row = must(
      element.shadowRoot?.querySelector<HTMLButtonElement>('button.row[data-key="attachment"]'),
      'the row',
    );
    expect(text(row.querySelector('.conflict'))).toBe(
      'Conflict: when Topic is Press, one rule hides it and the other requires it. A hidden field is never required.',
    );
    expect([...row.querySelectorAll('.said')].map((said) => text(said))).toEqual([
      'Hidden when Topic is Press',
      'Required when Topic is one of Shipping, Press',
    ]);
  });

  it("says an answer the rules don't name as none of those they do, and files by how many", async () => {
    const text1 = (key: string, label: string, rules: DesignRule[] = []): DesignField => ({
      key,
      type: 'text',
      alias: key,
      label: { '': label },
      rules,
    });
    const definition: DesignDefinition = {
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                text1('name', 'Name'),
                { key: 'upload', type: 'file', alias: 'upload', label: { '': 'Upload' } },
                text1('extra', 'Extra', [
                  { key: 'h1', effect: 'hide', conditions: [{ field: 'name', op: 'notEquals', value: 'Ann' }] },
                  { key: 'r1', effect: 'require', conditions: [{ field: 'name', op: 'isNotEmpty' }] },
                ]),
                text1('more', 'More', [
                  { key: 'h2', effect: 'hide', conditions: [{ field: 'upload', op: 'countAtLeast', value: '2' }] },
                  { key: 'r2', effect: 'require', conditions: [{ field: 'upload', op: 'isNotEmpty' }] },
                ]),
              ],
            },
          ],
        },
      ],
    };
    const { root } = await overviewOf(definition);
    const conflict = (key: string) => text(root.querySelector(`button.row[data-key="${key}"] .conflict`));
    expect(conflict('extra')).toMatch(/^Conflict: when Name is none of "Ann", one rule hides it/);
    expect(conflict('more')).toMatch(/^Conflict: when Upload has 2 files, one rule hides it/);
  });

  it('says no conflict for an answer the form hides, as on the website, and one for an answer it shows', async () => {
    // The guests are asked only about the press, and more than four hide the notes; the form works out a total, which
    // the form engine needs the calculation language for.
    const formFor = (required: readonly string[]): DesignDefinition => ({
      pages: [
        {
          key: 'p',
          groups: [
            {
              key: 'g',
              fields: [
                {
                  key: 'topic',
                  type: 'dropdown',
                  alias: 'topic',
                  label: { '': 'Topic' },
                  options: { items: [{ value: 'delivery' }, { value: 'press' }] },
                },
                {
                  key: 'guests',
                  type: 'number',
                  alias: 'guests',
                  label: { '': 'Guests' },
                  rules: [{ key: 's', effect: 'show', conditions: [{ field: 'topic', op: 'equals', value: 'press' }] }],
                },
                {
                  key: 'total',
                  type: 'calculation',
                  alias: 'total',
                  label: { '': 'Total' },
                  settings: { formula: 'guests * 2' },
                },
                {
                  key: 'notes',
                  type: 'text',
                  alias: 'notes',
                  label: { '': 'Notes' },
                  rules: [
                    { key: 'h', effect: 'hide', conditions: [{ field: 'guests', op: 'greaterThan', value: '4' }] },
                    { key: 'r', effect: 'require', conditions: [{ field: 'topic', op: 'isOneOf', value: required }] },
                  ],
                },
              ],
            },
          ],
        },
      ],
    });
    const conflict = (root: ShadowRoot) => root.querySelector('button.row[data-key="notes"] .conflict');
    expect(conflict((await overviewOf(formFor(['delivery']))).root)).toBeNull();
    document.body.replaceChildren();
    expect(text(conflict((await overviewOf(formFor(['delivery', 'press']))).root))).toMatch(
      /^Conflict: when Guests is 5 and Topic is press, one rule hides it/,
    );
  });

  it('leads a row to its element and an outcome to Settings', async () => {
    const { modal, root } = await overviewOf();
    must(root.querySelector<HTMLButtonElement>('button.row[data-key="email"]'), 'the row').click();
    expect([modal.getValue(), modal.submit.mock.calls.length]).toEqual([{ go: 'element', key: 'email' }, 1]);
    must(root.querySelector<HTMLButtonElement>('button.row[data-outcome="0"]'), 'the outcome').click();
    expect(modal.getValue()).toEqual({ go: 'outcomes', index: 0 });
  });

  it('asks first what a new rule affects, says when nothing is chosen, and leads to it', async () => {
    const { element, modal, root } = await overviewOf();
    const add = must(root.querySelector<HTMLElement>('#add-rule'), 'Add rule');
    expect(add.getAttribute('slot')).toBe('action-menu');
    add.click();
    await settle(element);
    const affects = must(root.querySelector<HTMLSelectElement>('#affects'), 'what it affects');
    expect(root.activeElement).toBe(affects);
    expect(text(root.querySelector('label[for="affects"]'))).toBe('What should the rule affect?');
    expect([...affects.querySelectorAll('optgroup')].map((group) => group.label)).toEqual([
      'Your details',
      'Page 2',
      'Nothing here',
    ]);
    // Delivery decides already whether it shows, so it takes no new rule, as in the inspector.
    expect([...affects.options].map((option) => text(option))).toEqual([
      'Choose an element',
      'Your details (page)',
      'Group 1 (group)',
      'Topic',
      'Attachment',
      'Page 2 (page)',
      'Email',
      'Nothing here (page)',
      'Group 1 (group)',
    ]);
    must(root.querySelector<HTMLElement>('.add uui-button[look="primary"]'), 'Continue').click();
    await settle(element);
    expect(modal.submit).not.toHaveBeenCalled();
    expect([affects.getAttribute('aria-invalid'), text(root.querySelector('#affects-why'))]).toEqual([
      'true',
      'Choose what the rule affects.',
    ]);
    affects.value = 'topic';
    affects.dispatchEvent(new Event('change'));
    await settle(element);
    must(root.querySelector<HTMLElement>('.add uui-button[look="primary"]'), 'Continue').click();
    await settle(element);
    expect(modal.getValue()).toEqual({ go: 'add', key: 'topic' });

    // Cancel goes back to Add rule.
    const again = await overviewOf();
    must(again.root.querySelector<HTMLElement>('#add-rule'), 'Add rule').click();
    await settle(again.element);
    must(again.root.querySelector<HTMLElement>('.add uui-button:not([look="primary"])'), 'Cancel').click();
    await settle(again.element);
    expect(again.root.querySelector('.add')).toBeNull();
    expect(again.root.activeElement?.id).toBe('add-rule');
  });

  it('says in one sentence what most rules do where the form has none, and closes', async () => {
    const { element, modal, root } = await overviewOf({ pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }] });
    expect(text(root.querySelector('.empty'))).toBe(
      'No rules yet. Most rules show a question only when another answer calls for it.',
    );
    expect(root.querySelector('uui-box')).toBeNull();
    must(root.querySelector<HTMLElement>('[slot="actions"] uui-button'), 'Close').click();
    await settle(element);
    expect(modal.reject).toHaveBeenCalled();
    // A form whose only rules are an outcome's lists them.
    const outcomes = await overviewOf({
      pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }],
      outcomes: DEFINITION['outcomes'],
    });
    expect(outcomes.root.querySelector('.empty')).toBeNull();
    expect([...outcomes.root.querySelectorAll('uui-box')].map((box) => box.getAttribute('headline'))).toEqual([
      'After sending',
    ]);
  });
});
