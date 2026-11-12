// The Overview's Forms at a glance (ROADMAP 11.5, IA 2, ADR-0211): it reads the last 30 days against the 30 before, and
// lists each form with its views, starts, entries and conversion, each with its change said short, the form opening
// its Insights view; its menu reads another period, a calendar one against the same days before; with no visit it says
// when numbers appear; and a read that fails offers Try again.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { InsightGlanceItemResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

const api = vi.hoisted(() => ({
  answers: [] as unknown[],
  asked: [] as { span: { from: string; to: string }; compare: { from: string; to: string } }[],
}));

vi.mock('./insight-api', async (original) => ({
  ...(await original<typeof import('./insight-api')>()),
  readGlance: async (_host: unknown, span: { from: string; to: string }, compare: { from: string; to: string }) => {
    api.asked.push({ span, compare });
    return api.answers.length > 1 ? api.answers.shift() : api.answers[0];
  },
}));

const { InsightGlanceElement } = await import('./insight-glance.element');

const item = (name: string, views: number, before: number): InsightGlanceItemResponseModel => ({
  key: `key-${name}`,
  name,
  totals: { views, starts: 10, entries: 4, spam: 0, conversion: views > 0 ? 4 / views : null },
  before: { views: before, starts: 10, entries: 0, spam: 0, conversion: before > 0 ? 0 : null },
});

async function boxOf(...answers: unknown[]) {
  api.answers = answers;
  api.asked = [];
  const host = testHost();
  const box = new InsightGlanceElement();
  host.append(box);
  await vi.waitFor(() => expect(shadow(box).querySelector('table, p, [role="alert"]')).not.toBeNull());
  await settle(box);
  return { box, root: shadow(box) };
}

/** A table row's cells, each as its text, apart by one space. */
const cells = (row: Element): string => [...row.children].map((cell) => text(cell)).join(' ');

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe('Forms at a glance', () => {
  it('lists each form with its numbers and their changes, each form opening its Insights view', async () => {
    const { root } = await boxOf([item('Contact us', 120, 100), item('Newsletter', 0, 0)]);
    const [first] = api.asked;
    expect((first?.compare.to ?? '') < (first?.span.from ?? '')).toBe(true);
    const rows = [...root.querySelectorAll('tbody tr')];
    expect(rows.map(cells)).toEqual([
      'Contact us 120 up 20% 10 no change 4 up from none 3.3% up 3.3 points',
      'Newsletter 0 no change 10 no change 4 up from none no data',
    ]);
    expect(root.querySelector('tbody a')?.getAttribute('href')).toBe(
      'section/jfb-forms/workspace/jfb-form/edit/key-Contact us/view/insights',
    );
    expect(text(root.querySelector('caption'))).toBe('Forms at a glance');
  });

  it("ends its period on the UTC day the stats count, not the user's own", async () => {
    const early = Date.UTC(2026, 10, 7, 0, 30);
    const instants = [early, Date.UTC(2026, 10, 7, 23, 30)];
    const now = instants.find((at) => new Date(at).getDate() !== new Date(at).getUTCDate()) ?? early;
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(now);
    try {
      await boxOf([]);
      expect(api.asked[0]?.span.to).toBe('2026-11-07');
    } finally {
      vi.useRealTimers();
    }
  });

  it('reads another period from its menu, and says when numbers appear while there is none', async () => {
    const { box, root } = await boxOf([], []);
    expect(text(root.querySelector('p'))).toBe(
      'No visits yet. Numbers appear after the first visit to a page with a form.',
    );
    const select = must(root.querySelector<HTMLSelectElement>('select'), 'the period');
    expect([...select.options].map((option) => text(option))).toEqual([
      'Last 7 days',
      'Last 30 days',
      'This month',
      'Last month',
      'This year',
    ]);
    select.value = 'thisMonth';
    select.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(api.asked).toHaveLength(2));
    await settle(box);
    const second = must(api.asked[1], 'the second read');
    expect(second.span.from.endsWith('-01')).toBe(true);
    expect(second.compare.from.endsWith('-01')).toBe(true);
  });

  it('offers Try again after a read that failed', async () => {
    const { root } = await boxOf(undefined);
    expect(text(root.querySelector('[role="alert"]'))).toMatch(/^The forms at a glance couldn't be loaded\./);
  });
});
