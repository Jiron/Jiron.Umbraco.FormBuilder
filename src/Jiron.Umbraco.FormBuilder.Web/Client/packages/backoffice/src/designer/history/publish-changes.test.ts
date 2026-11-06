// What the publish dialog says of the draft (ROADMAP 10.3d, IA 5.7): on a first publish what the first version holds;
// otherwise what changed since the version visitors get, the draft as it is now compared with it, in History's parts,
// each with its first few lines, and a sentence that says which parts changed; since the last published version for a
// form taken offline; nothing changed; and that it can't be said, where an answer is missing.
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { beforeAll, describe, expect, it, vi } from 'vitest';
import type { DefinitionChangeResponseModel } from '../../api';
import { registerEnglish, testHost } from '../../testing';
import type { DesignDefinition } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import historyEn from '../../lang/history.en';
import { flatten } from '../actions/action-words';
import { loadHistoryWords } from './history-words';

/** What the API answers, and what it was asked. */
const fake = vi.hoisted(() => ({
  published: [] as unknown[] | undefined,
  live: undefined as unknown,
  compared: undefined as unknown,
  comparedWith: [] as number[],
  sent: [] as unknown[],
  read: [] as number[],
  listed: [] as unknown[],
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  versionList: async (options: { query: unknown }) => {
    fake.listed.push(options.query);
    return { data: fake.published ? { total: fake.published.length, items: fake.published } : undefined };
  },
  versionGet: async (options: { path: { number: number } }) => {
    fake.read.push(options.path.number);
    return { data: fake.live };
  },
  versionCompareWith: async (options: { path: { number: number }; body: { definition: unknown } }) => {
    fake.comparedWith.push(options.path.number);
    fake.sent.push(options.body.definition);
    return { data: fake.compared };
  },
  actionTypeList: async () => ({ data: { types: [] } }),
}));

vi.mock('../preview/look', async (original) => ({
  ...(await original<typeof import('../preview/look')>()),
  readLook: async () => undefined,
}));

const { changesSinceLive, holdings, MOST_LINES } = await import('./publish-changes');

const field = (key: string, label: string, more: Record<string, unknown> = {}) => ({
  key,
  type: 'text',
  alias: key,
  label: { '': label },
  ...more,
});
const formOf = (...fields: ReturnType<typeof field>[]): DesignDefinition => ({
  pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }],
});

const LIVE = formOf(field('f1', 'Name'));
const DRAFT = formOf(
  field('f1', 'Full name'),
  field('f2', 'Email'),
  field('f3', 'Phone'),
  field('f4', 'Company'),
  field('f5', 'Street'),
);
const added = (key: string, label: string): DefinitionChangeResponseModel => ({
  kind: 'field',
  key,
  parentKey: 'g1',
  change: 'added',
  moved: false,
  label,
  properties: [],
});

/** The workspace as far as the dialog's words ask it, its draft not saved yet. */
function workspaceOf(publishedVersion: number | null, definition: DesignDefinition = DRAFT) {
  const host = testHost();
  const workspace = Object.assign(document.createElement('jfb-test-host'), {
    getUnique: () => 'f',
    getData: () => ({ definition, publishedVersion }),
    getFieldTypes: () => builtInTypes,
  });
  host.append(workspace);
  return workspace as never;
}

/** What the dialog says, read through the workspace's element as the host that opens it. */
const said = (workspace: never) =>
  changesSinceLive(workspace, workspace, new UmbLocalizationController(workspace as never));

/** History's English words, as the dialog reads them. */
const english = (key: string, ...args: unknown[]): string => {
  const word = (flatten('en', historyEn) as Record<string, unknown>)[key];
  return typeof word === 'function' ? String((word as (...values: unknown[]) => unknown)(...args)) : String(word);
};

const version = (number: number) => ({
  version: {
    number,
    kind: 'published',
    isPinned: false,
    createdUtc: '2026-11-06T10:00:00Z',
    isDraft: false,
    isLive: false,
  },
  changes: null,
  by: 'Ana Novak',
});

beforeAll(async () => {
  registerEnglish();
  await loadHistoryWords('en');
});

describe('what the publish dialog says of the draft', () => {
  it('counts what a version holds: its pages and fields, nested ones too, and its rules and actions where it has any', () => {
    const term = english;
    const definition = {
      pages: [
        {
          key: 'p1',
          rules: [{ key: 'r1' }],
          groups: [
            {
              key: 'g1',
              fields: [
                field('a', 'A', { rules: [{ key: 'r2' }] }),
                field('row', 'Row', { fields: [field('b', 'B'), field('c', 'C')] }),
              ],
            },
          ],
        },
        { key: 'p2', groups: [] },
      ],
      validationRules: [{ key: 'v1' }],
      actions: [{ key: 'x1', rules: [{ key: 'r3' }] }],
    } as unknown as DesignDefinition;
    // A check is a check, not a rule (copy.md).
    expect(holdings(definition, term)).toEqual(['2 pages', '4 fields', '3 rules', '1 check', '1 action']);
    expect(holdings(formOf(field('a', 'A')), term)).toEqual(['1 page', '1 field']);
  });

  it('says what the first version holds where nothing was ever published', async () => {
    fake.published = [];
    const answer = await said(workspaceOf(null, formOf(field('a', 'A'), field('b', 'B'))));
    expect(answer).toEqual({ headline: null, parts: [], said: 'The first version holds 1 page and 2 fields.' });
    expect(fake.listed.at(-1)).toEqual({ includeDrafts: false, skip: 0, take: 1 });
  });

  it('lists what changed since the version visitors get, the draft as it is now, each part with its first lines', async () => {
    fake.comparedWith = [];
    fake.live = { definition: LIVE };
    fake.compared = {
      fromVersion: 3,
      changes: [
        {
          kind: 'field',
          key: 'f1',
          change: 'changed',
          moved: false,
          label: 'Full name',
          properties: [{ path: ['label', ''], from: 'Name', to: 'Full name' }],
        },
        added('f2', 'Email'),
        added('f3', 'Phone'),
        added('f4', 'Company'),
        added('f5', 'Street'),
        { kind: 'field', key: 'f9', parentKey: 'g1', change: 'removed', moved: false, label: 'Fax', properties: [] },
        { kind: 'field', key: 'f8', parentKey: 'g1', change: 'removed', moved: false, label: 'Pager', properties: [] },
      ],
      definition: DRAFT,
    };
    fake.sent = [];
    fake.read = [];
    const answer = await said(workspaceOf(3));
    // The draft as it is now, with the changes not saved yet, against the version visitors get.
    expect([fake.comparedWith, fake.read, fake.sent]).toEqual([[3], [3], [DRAFT]]);
    expect(answer.headline).toBe('Since the published version');
    expect(answer.said).toBe('Changed since the published version: Fields.');
    expect(answer.parts).toEqual([
      {
        name: 'Fields',
        lines: ['Fields added: Email, Phone, Company, Street', 'Fields removed: Fax, Pager', 'Full name: Label'],
        more: null,
      },
    ]);
    expect(MOST_LINES).toBe(3);
  });

  it('says how many more lines a part holds than it shows', async () => {
    fake.live = { definition: LIVE };
    const changed = (key: string, label: string): DefinitionChangeResponseModel => ({
      kind: 'field',
      key,
      change: 'changed',
      moved: false,
      label,
      properties: [{ path: ['required'], from: false, to: true }],
    });
    fake.compared = {
      fromVersion: 3,
      changes: [changed('f1', 'A'), changed('f2', 'B'), changed('f3', 'C'), changed('f4', 'D'), changed('f5', 'E')],
      definition: DRAFT,
    };
    const answer = await said(workspaceOf(3));
    expect(answer.parts[0]?.lines).toHaveLength(MOST_LINES);
    expect(answer.parts[0]?.more).toBe('And 2 more.');
  });

  it('compares a form taken offline with the last version visitors got', async () => {
    fake.comparedWith = [];
    fake.published = [version(5), version(2)];
    fake.live = { definition: LIVE };
    fake.compared = { fromVersion: 5, changes: [added('f2', 'Email')], definition: DRAFT };
    fake.read = [];
    const answer = await said(workspaceOf(null));
    expect([fake.comparedWith, fake.read]).toEqual([[5], [5]]);
    expect(fake.listed.at(-1)).toEqual({ includeDrafts: false, skip: 0, take: 1 });
    expect(answer.headline).toBe('Since the last published version');
    expect(answer.said).toBe('Changed since the last published version: Fields.');
    fake.compared = { fromVersion: 5, changes: [], definition: LIVE };
    expect((await said(workspaceOf(null))).said).toBe('Nothing changed since the last published version.');
  });

  it("says nothing changed, and says where it can't be read", async () => {
    fake.live = { definition: LIVE };
    fake.compared = { fromVersion: 3, changes: [], definition: LIVE };
    expect((await said(workspaceOf(3))).said).toBe('Nothing changed since the published version.');

    const failed = "What changed couldn't be read. Publishing works all the same.";
    fake.compared = undefined;
    expect((await said(workspaceOf(3))).said).toBe(failed);
    fake.compared = { fromVersion: 3, changes: [], definition: LIVE };
    fake.live = { definition: null };
    expect((await said(workspaceOf(3))).said).toBe(failed);
    fake.published = undefined;
    expect((await said(workspaceOf(null))).said).toBe(failed);
  });
});
