// What the rules builder reads from Umbraco (ROADMAP 9.5a, ADR-0175): the member groups' names, asked for only of a
// user with the Members section, whom Umbraco lets read them.
import { of } from '@umbraco-cms/backoffice/external/rxjs';
import type { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { readMemberGroups } from './rule-data';

const asked = vi.hoisted(() => ({ count: 0 }));

vi.mock('@umbraco-cms/backoffice/external/backend-api', () => ({
  MemberGroupService: {
    getMemberGroup: () => {
      asked.count++;
      return Promise.resolve({ data: { items: [{ name: 'Silver' }, { name: 'Gold' }], total: 2 } });
    },
  },
}));
vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

const hostWith = (sections: string[] | null) =>
  ({
    getContext: () =>
      sections ? Promise.resolve({ allowedSections: of(sections) }) : Promise.reject(new Error('none')),
  }) as unknown as UmbLitElement;

beforeEach(() => {
  asked.count = 0;
});

describe('the member groups', () => {
  it('are read, in alphabetical order, for a user with the Members section', async () => {
    expect(await readMemberGroups(hostWith(['Umb.Section.Members', 'Jiron.FormBuilder.Section']))).toEqual([
      'Gold',
      'Silver',
    ]);
    expect(asked.count).toBe(1);
  });

  it('are not asked for without the Members section, nor without a current user', async () => {
    expect(await readMemberGroups(hostWith(['Jiron.FormBuilder.Section']))).toBeUndefined();
    expect(await readMemberGroups(hostWith(null))).toBeUndefined();
    expect(asked.count).toBe(0);
  });
});
