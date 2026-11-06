// One trigger's cards (ROADMAP 10.2a, ADR-0188): each action's name, what it does and its rule, its problems counted;
// its switch and "..." with Duplicate, Move to another trigger... and Delete; moves with the keyboard, which say where the
// action went and keep the focus on it; and read-only, a card that still opens and says only whether it is off.
import { afterEach, beforeAll, describe, expect, it } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { ActionListElement, type ActionCard, type CardRequest } from './action-list.element';
import { loadActionWords } from './action-words';

const card = (key: string, more: Partial<ActionCard> = {}): ActionCard => ({
  key,
  name: `Action ${key}`,
  icon: 'icon-message',
  summary: 'To sales@example.com',
  rule: null,
  on: true,
  ...more,
});

async function listOf(cards: readonly ActionCard[], readOnly = false, full = false) {
  const host = testHost();
  const list = new ActionListElement();
  const opened: string[] = [];
  const moves: [number, number][] = [];
  const toggles: [string, boolean][] = [];
  const requests: [string, CardRequest][] = [];
  list.cards = cards;
  list.label = 'Submitted';
  list.readOnly = readOnly;
  list.full = full;
  list.open = (key) => opened.push(key);
  list.move = (from, to) => {
    moves.push([from, to]);
    const next = [...list.cards];
    const [taken] = next.splice(from, 1);
    if (taken) next.splice(to, 0, taken);
    list.cards = next;
  };
  list.toggle = (key, on) => toggles.push([key, on]);
  list.request = (key, request) => requests.push([key, request]);
  host.append(list);
  await settle(list);
  const root = shadow(list);
  const rows = () => [...root.querySelectorAll<HTMLElement>('.card')];
  const said = () => text(root.querySelector('[role="status"]'));
  return { list, root, rows, opened, moves, toggles, requests, said };
}

beforeAll(async () => {
  registerEnglish();
  await loadActionWords('en');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe("a trigger's cards", () => {
  it('offer Test beside the switch, busy while it runs and saying so, a second press waiting for the first (ADR-0190)', async () => {
    const { list, root, rows, said } = await listOf([card('a'), card('b')]);
    expect(root.querySelector('.test')).toBeNull();
    let finish: () => void = () => undefined;
    const tested: string[] = [];
    list.test = (key) => {
      tested.push(key);
      return new Promise<void>((done) => (finish = done));
    };
    await settle(list);
    const test = must(rows()[0]?.querySelector<HTMLElement>('.test'), 'Test');
    expect([test.getAttribute('label'), text(test)]).toEqual(['Test Action a', 'Test']);
    test.click();
    await settle(list);
    expect(test.getAttribute('state')).toBe('waiting');
    expect(said()).toBe('Testing Action a\u2026');
    test.click();
    expect(tested).toEqual(['a']);
    finish();
    await settle(list);
    expect(test.hasAttribute('state')).toBe(false);
  });

  it('offer Test in the "..." too, for a card too narrow to show it beside the switch (IA 5.3)', async () => {
    const { list, root, rows } = await listOf([card('a')]);
    expect(root.querySelector('.test-item')).toBeNull();
    const tested: string[] = [];
    list.test = async (key) => void tested.push(key);
    await settle(list);
    const item = must(rows()[0]?.querySelector<HTMLElement>('.menu .test-item'), 'Test in the menu');
    expect(text(item)).toBe('Test');
    item.click();
    await settle(list);
    expect(tested).toEqual(['a']);
  });

  it('offer no Test where the actions are read-only', async () => {
    const { list, root } = await listOf([card('a')], true);
    list.test = async () => undefined;
    await settle(list);
    expect(root.querySelector('.test')).toBeNull();
  });

  it('say what each action does, its rule and its problems, in a list named by the trigger', async () => {
    const { root, rows } = await listOf([
      card('a'),
      card('b', { name: 'Send to the CRM', summary: 'Webhook to crm.example.com', rule: 'Runs when Topic is Sales' }),
      card('c', { flag: { count: 2, error: false } }),
    ]);
    expect(root.querySelector('ol')?.getAttribute('aria-label')).toBe('Submitted');
    expect(rows().map((row) => text(row.querySelector('.name')))).toEqual(['Action a', 'Send to the CRM', 'Action c']);
    expect(text(rows()[1]?.querySelector('.line'))).toBe('Webhook to crm.example.com');
    expect(text(rows()[1]?.querySelector('.rule'))).toBe('Runs when Topic is Sales');
    expect(rows()[0]?.querySelector('.rule')).toBeNull();
    // The heaviest problem's colour, with words: a warning's.
    const flag = must(rows()[2]?.querySelector<HTMLElement>('.flag'), 'the flag');
    expect(flag.dataset['severity']).toBe('warning');
    expect(text(rows()[2]?.querySelector('.line'))).toMatch(/^2 problems/);
  });

  it('draw no line where it would only repeat the name, and the problems alone where there are some', async () => {
    const { rows } = await listOf([
      card('a', { summary: '' }),
      card('b', { summary: '', flag: { count: 1, error: true } }),
    ]);
    expect(rows()[0]?.querySelector('.line')).toBeNull();
    expect(text(rows()[1]?.querySelector('.line'))).toBe('1 problem');
  });

  it('offer no Duplicate once the form holds the most actions it can have', async () => {
    const { rows } = await listOf([card('a')], false, true);
    const items = [...(rows()[0]?.querySelectorAll<HTMLButtonElement>('.menu button') ?? [])];
    expect(items.map((item) => text(item))).toEqual(['Move to another trigger\u2026', 'Delete']);
  });

  it('draw nothing for a trigger without actions', async () => {
    const { root } = await listOf([]);
    expect(root.querySelector('ol')).toBeNull();
  });

  it("open an action's sidebar by its name, and switch it on and off by its switch", async () => {
    const { rows, opened, toggles } = await listOf([card('a'), card('b', { on: false })]);
    must(rows()[0]?.querySelector<HTMLButtonElement>('.open'), 'the name').click();
    expect(opened).toEqual(['a']);
    const switches = rows().map((row) => must(row.querySelector<HTMLInputElement>('input[role="switch"]'), 'switch'));
    expect(switches.map((one) => [one.getAttribute('aria-label'), one.checked])).toEqual([
      ['Use Action a', true],
      ['Use Action b', false],
    ]);
    switches[1]?.click();
    expect(toggles).toEqual([['b', true]]);
  });

  it('offer Duplicate, Move to another trigger\u2026 and Delete in "\u2026", named for the action', async () => {
    const { rows, requests } = await listOf([card('a')]);
    const more = must(rows()[0]?.querySelector<HTMLButtonElement>('.more'), '"\u2026"');
    expect(more.getAttribute('aria-label')).toBe('More actions for Action a');
    const items = [...(rows()[0]?.querySelectorAll<HTMLButtonElement>('.menu button') ?? [])];
    expect(items.map((item) => text(item))).toEqual(['Duplicate', 'Move to another trigger\u2026', 'Delete']);
    for (const item of items) item.click();
    expect(requests).toEqual([
      ['a', 'duplicate'],
      ['a', 'moveTrigger'],
      ['a', 'delete'],
    ]);
  });

  it('move with Alt and the arrows, say where the action went, keep the focus on it, and say the edge', async () => {
    const { list, root, rows, moves, said } = await listOf([card('a'), card('b'), card('c')]);
    const handle = (key: string) => must(root.querySelector<HTMLButtonElement>(`[data-key="${key}"] .handle`), key);
    expect(handle('a').getAttribute('aria-label')).toBe('Move Action a');
    handle('a').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', altKey: true, bubbles: true }));
    await settle(list);
    expect(moves).toEqual([[0, 1]]);
    expect(rows().map((row) => row.dataset['key'])).toEqual(['b', 'a', 'c']);
    expect(said()).toBe('Moved. Action a: position 2 of 3.');
    expect(root.activeElement).toBe(handle('a'));
    handle('b').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', altKey: true, bubbles: true }));
    await settle(list);
    expect(moves).toHaveLength(1);
    expect(said()).toBe("It can't move any further.");
    // An arrow without Alt moves nothing.
    handle('c').dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowUp', bubbles: true }));
    expect(moves).toHaveLength(1);
  });

  it('put the focus on an action by its key, and say false for one it does not hold', async () => {
    const { list, root } = await listOf([card('a'), card('b')]);
    expect(await list.focusAction('b')).toBe(true);
    expect(root.activeElement).toBe(root.querySelector('[data-key="b"] .open'));
    expect(await list.focusAction('gone')).toBe(false);
  });

  it('read-only, still open a card, with no handle, switch or "\u2026", and say an action that is off', async () => {
    const { rows, opened } = await listOf([card('a'), card('b', { on: false })], true);
    expect(rows().map((row) => row.querySelector('.handle, input[role="switch"], .more'))).toEqual([null, null]);
    expect(rows().map((row) => text(row.querySelector('.end')))).toEqual(['', 'Off']);
    must(rows()[1]?.querySelector<HTMLButtonElement>('.open'), 'the name').click();
    expect(opened).toEqual(['b']);
  });
});
