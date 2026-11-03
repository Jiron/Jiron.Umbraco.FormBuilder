// What the canvas asks the server for a draft's options (ROADMAP 9.4b, ADR-0172): an answer is kept by the form, the
// field, what its options hold and the culture, and one that failed or found its source unavailable is asked again.
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { DesignField } from '../definition';

const server = vi.hoisted(() => ({ asked: [] as unknown[], answers: [] as unknown[] }));

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => ({ data: await request }),
}));

vi.mock('../../api', () => ({
  draftOptionsResolve: async (request: unknown) => {
    server.asked.push(request);
    return server.answers.shift();
  },
  optionListList: async () => ({ items: [] }),
  optionSourceList: async () => ({ kinds: [], sources: [] }),
}));

const { resolveOptions } = await import('./option-data');

const host = {} as never;
const months: DesignField = { key: 'm', type: 'dropdown', options: { source: 'code', config: { source: 'months' } } };
const available = { options: [{ value: '1', label: 'January', parentValues: [] }], isAvailable: true };
const unavailable = { options: [], isAvailable: false };

beforeEach(() => {
  server.asked.length = 0;
  server.answers.length = 0;
});

describe("a draft's options", () => {
  it('are asked once for the same form, field, options and culture, and again for another culture', async () => {
    server.answers.push(available, available);
    expect(await resolveOptions(host, 'form1', months, 'en-US')).toEqual(available);
    expect(await resolveOptions(host, 'form1', months, 'en-US')).toEqual(available);
    expect(server.asked).toEqual([
      {
        path: { key: 'form1' },
        body: { field: 'm', type: 'dropdown', options: months['options'], culture: 'en-US' },
      },
    ]);
    await resolveOptions(host, 'form1', months, 'de-DE');
    expect(server.asked).toHaveLength(2);
  });

  it('are asked again when the source was unavailable or the read failed', async () => {
    const weekdays: DesignField = { ...months, key: 'w' };
    server.answers.push(unavailable, undefined, available);
    expect(await resolveOptions(host, 'form2', weekdays, 'en-US')).toEqual(unavailable);
    expect(await resolveOptions(host, 'form2', weekdays, 'en-US')).toBeUndefined();
    expect(await resolveOptions(host, 'form2', weekdays, 'en-US')).toEqual(available);
    expect(await resolveOptions(host, 'form2', weekdays, 'en-US')).toEqual(available);
    expect(server.asked).toHaveLength(3);
  });
});
