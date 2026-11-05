// After sending's list (ROADMAP 10.1b, ADR-0185): each outcome by what it gives visitors and when, its problems counted
// in the colour of the heaviest, the pages' names in the language shown, the default message where some visitors get it,
// the list's own problems above Add outcome, the limit in its place, and the moves with the keyboard, which say where
// the outcome went and keep the focus on it.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import type { DesignDefinition } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import { inspectorRuleWords } from '../rules/rule-names';
import type { DesignOutcome } from './outcome-model';
import type { Flag, Shown } from './problem-lines';

// The pages the redirects lead to, as Umbraco names them.
const pages = vi.hoisted(() => ({ asked: [] as string[][] }));

vi.mock('@umbraco-cms/backoffice/document', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/document')>()),
  UmbDocumentItemRepository: class {
    async requestItems(keys: string[]) {
      pages.asked.push(keys);
      return {
        data: keys.map((key) => ({
          unique: key,
          variants: [
            { culture: 'de-DE', name: 'Danke' },
            { culture: 'en-US', name: 'Thanks page' },
          ],
        })),
      };
    }
  },
}));

const { OutcomeListElement } = await import('./outcome-list.element');

const DEFINITION: DesignDefinition = {
  pages: [
    {
      key: 'p1',
      groups: [
        {
          key: 'g1',
          fields: [
            {
              key: 'topic',
              type: 'dropdown',
              alias: 'topic',
              label: { '': 'Topic' },
              options: { items: [{ value: 'press', label: { '': 'Press' } }] },
            },
          ],
        },
      ],
    },
  ],
};

const press: DesignOutcome = {
  key: 'o1',
  kind: 'redirect',
  redirect: { url: '/press/thanks', delaySeconds: 0 },
  rules: [
    {
      key: 'r1',
      effect: 'use',
      enabled: true,
      match: 'all',
      conditions: [{ field: 'topic', op: 'equals', value: 'press' }],
    },
  ],
};
const thanks: DesignOutcome = {
  key: 'o2',
  kind: 'message',
  message: {
    '': '<p>Thank you, {{ field.name }}. We have received your message and answer within two working days.</p>',
  },
};
const page: DesignOutcome = { key: 'o3', kind: 'redirect', redirect: { content: 'page1', delaySeconds: 0 } };

interface Extra {
  readonly flags?: ReadonlyMap<string, Flag>;
  readonly problems?: readonly Shown[];
  readonly limit?: number;
}

async function listOf(outcomes: readonly DesignOutcome[], extra: Extra = {}) {
  const host = testHost();
  const term = (key: string, ...args: unknown[]) => host.localize.term(key, ...args);
  const list = new OutcomeListElement();
  const opened: (string | null)[] = [];
  const moves: [number, number][] = [];
  list.outcomes = outcomes;
  list.culture = 'en-US';
  list.defaultMessage = 'Thank you. We have received your message.';
  list.words = inspectorRuleWords(DEFINITION, builtInTypes, 'en-US', [], term);
  list.flags = extra.flags ?? new Map();
  list.problems = extra.problems ?? [];
  list.weight = (severity) => (severity === 'warning' ? 'Warning:' : 'Suggestion:');
  list.limit = extra.limit ?? 0;
  list.open = (key) => opened.push(key);
  list.move = (from, to) => {
    moves.push([from, to]);
    const next = [...list.outcomes];
    const [taken] = next.splice(from, 1);
    if (taken) next.splice(to, 0, taken);
    list.outcomes = next;
  };
  host.append(list);
  await settle(list);
  const root = shadow(list);
  const rows = () => [...root.querySelectorAll<HTMLElement>('.outcome-row')];
  const said = () => text(root.querySelector('[role="status"]'));
  return { list, root, rows, opened, moves, said };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  pages.asked = [];
});

describe("After sending's list", () => {
  it('names each outcome by what visitors get and says when they get it, no visitor left out', async () => {
    const { rows, root } = await listOf([press, thanks, page], {
      flags: new Map([
        ['o1', { count: 2, error: true }],
        ['o2', { count: 1, error: false }],
      ]),
    });
    await settle();
    expect(rows().map((row) => text(row.querySelector('.row-name')))).toEqual([
      'Redirect to /press/thanks',
      // Cut at a word, so the row wraps a name rather than cutting it.
      `Message: "Thank you, {{ field.name }}. We have received your message${String.fromCharCode(0x2026)}"`,
      // The page's name in the language shown, once Umbraco gives it.
      'Redirect to Thanks page',
    ]);
    expect(rows().map((row) => text(row.querySelector('.row-line')))).toEqual([
      `2 problems ${String.fromCharCode(0xb7)} Used when Topic is Press`,
      `1 problem ${String.fromCharCode(0xb7)} Otherwise`,
      'Never used: an outcome above is used for every visitor',
    ]);
    expect(pages.asked).toEqual([['page1']]);
    // The count in an error's colour where one is among them, else a warning's.
    expect(rows().map((row) => row.querySelector('.flag')?.getAttribute('data-severity') ?? null)).toEqual([
      'error',
      'warning',
      null,
    ]);
    // Every visitor gets an outcome: the default message is said nowhere.
    expect(root.querySelector('.default')).toBeNull();
    // Each row's button is named by all it shows, its line too (WCAG 2.5.3); its handle by the outcome alone.
    const open = must(rows()[0]?.querySelector<HTMLButtonElement>('.row-open'), 'the first row');
    expect(open.hasAttribute('aria-labelledby')).toBe(false);
    expect(rows()[0]?.querySelector('.row-handle')?.getAttribute('aria-label')).toBe('Move Redirect to /press/thanks');
  });

  it('names the pages in the language shown, asked for once', async () => {
    const { list, rows } = await listOf([page]);
    await settle();
    list.culture = 'de-DE';
    await settle(list);
    expect(text(rows()[0]?.querySelector('.row-name'))).toBe('Redirect to Danke');
    expect(pages.asked).toEqual([['page1']]);
  });

  it("shows the list's own problems above Add outcome, which they describe, and the limit in its place", async () => {
    const problems: Shown[] = [{ text: 'Too many outcomes.', severity: 'error' }];
    const { root } = await listOf([press], { problems });
    expect(text(root.querySelector('#outcomes-why-0'))).toBe('Too many outcomes.');
    expect(root.querySelector('.add')?.getAttribute('aria-describedby')).toBe('outcomes-why-0');
    document.body.replaceChildren();
    const full = await listOf([press, thanks], { limit: 2 });
    expect(full.root.querySelector('.add')).toBeNull();
    expect(text(full.root.querySelector('.default'))).toBe('A form has at most 2 outcomes.');
    // With nothing to add, the focus goes to the last outcome.
    expect(await full.list.focusOutcome(null)).toBe(true);
    expect(full.root.activeElement?.closest('.outcome-row')?.getAttribute('data-key')).toBe('o2');
  });

  it('says what visitors get where no outcome is for every visitor, or where there is none', async () => {
    const some = await listOf([press]);
    expect(text(some.root.querySelector('.default'))).toBe(
      'Otherwise, visitors get the default message: "Thank you. We have received your message."',
    );
    document.body.replaceChildren();
    const none = await listOf([]);
    expect(none.root.querySelector('ol')).toBeNull();
    expect(text(none.root.querySelector('.default'))).toBe(
      'Visitors get the default message: "Thank you. We have received your message."',
    );
    expect(text(none.root.querySelector('.add'))).toBe('Add outcome');
  });

  it('opens an outcome by its row, and a new one by Add outcome', async () => {
    const { root, rows, opened } = await listOf([press, thanks]);
    must(rows()[1]?.querySelector<HTMLButtonElement>('.row-open'), 'the second row').click();
    must(root.querySelector<HTMLButtonElement>('.add'), 'Add outcome').click();
    expect(opened).toEqual(['o2', null]);
  });

  it('moves an outcome with Alt and the arrows, says where it went and keeps the focus on it', async () => {
    const { list, root, rows, moves, said } = await listOf([press, thanks]);
    const handle = must(rows()[0]?.querySelector<HTMLButtonElement>('.row-handle'), 'the handle');
    handle.focus();
    handle.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }));
    await settle(list);
    expect(moves).toEqual([[0, 1]]);
    expect(said()).toBe('Moved. Redirect to /press/thanks: position 2 of 2.');
    expect(root.activeElement?.closest('.outcome-row')?.getAttribute('data-key')).toBe('o1');
    const moved = must(rows()[1]?.querySelector<HTMLButtonElement>('.row-handle'), 'the moved handle');
    moved.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }));
    await settle(list);
    expect([moves.length, said()]).toEqual([1, "It can't move any further."]);
    // Without Alt the arrows do nothing here.
    moved.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
    expect(moves.length).toBe(1);
  });

  it('puts the focus on an outcome, or on Add outcome where it is gone', async () => {
    const { list, root } = await listOf([press]);
    expect(await list.focusOutcome('o1')).toBe(true);
    expect(root.activeElement?.classList.contains('row-open')).toBe(true);
    await list.focusOutcome('gone');
    expect(root.activeElement?.classList.contains('add')).toBe(true);
  });
});
