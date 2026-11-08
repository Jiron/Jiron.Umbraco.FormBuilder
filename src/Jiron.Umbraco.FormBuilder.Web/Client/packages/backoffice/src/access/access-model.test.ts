// What Access works out (ROADMAP 12.4a, ADR-0013, ADR-0218): a box ticked brings what it needs and one unticked takes
// what needs it; a save sends only the groups that changed, from what the editor saw; and each folder's or form's
// sentence says who alone may see it, or who may also.
import { describe, expect, it } from 'vitest';
import { changedGrants, grantsOf, sentenceOf, withPermission } from './access-model';

const NEEDS = [
  { permission: 'manageActions', needs: 'design' },
  { permission: 'viewSensitive', needs: 'viewEntries' },
  { permission: 'editEntries', needs: 'viewEntries' },
  { permission: 'export', needs: 'viewEntries' },
];
const ORDER = ['design', 'manageActions', 'viewEntries', 'viewSensitive', 'editEntries', 'export'];

describe('a box of the matrix', () => {
  it('ticked, brings the permission it needs', () => {
    const { granted, besides } = withPermission(new Set(), 'editEntries', true, NEEDS);
    expect([...granted].sort()).toEqual(['editEntries', 'viewEntries']);
    expect(besides).toEqual(['viewEntries']);
  });

  it('unticked, takes every permission that needs it', () => {
    const { granted, besides } = withPermission(
      new Set(['viewEntries', 'editEntries', 'export', 'design']),
      'viewEntries',
      false,
      NEEDS,
    );
    expect([...granted]).toEqual(['design']);
    expect(besides).toEqual(['editEntries', 'export']);
  });

  it('changes nothing else when nothing depends on it', () => {
    expect(withPermission(new Set(['design']), 'viewEntries', true, NEEDS).besides).toEqual([]);
    expect(withPermission(new Set(['design']), 'design', true, NEEDS).granted).toEqual(new Set(['design']));
  });
});

describe('a save', () => {
  it("sends only the groups that changed, each from what the editor saw, in the server's order", () => {
    const before = grantsOf([
      { key: 'a', permissions: ['viewEntries'] },
      { key: 'b', permissions: ['design'] },
      { key: 'c', permissions: [] },
    ]);
    const now = new Map([
      ['a', new Set(['export', 'viewEntries'])],
      ['b', new Set(['design'])],
      ['c', new Set<string>()],
    ]);
    expect(changedGrants(before, now, ORDER)).toEqual([
      { group: 'a', expected: ['viewEntries'], permissions: ['viewEntries', 'export'] },
    ]);
  });
});

describe('a folder or form with access of its own', () => {
  const names = (key: string) => ({ hr: 'HR team', sales: 'Sales' })[key] ?? key;

  it('restricted, says who alone sees it, or that only administrators do', () => {
    expect(sentenceOf({ isRestricted: true, grants: [{ group: 'hr' }] }, names)).toEqual({
      kind: 'only',
      groups: ['HR team'],
    });
    expect(sentenceOf({ isRestricted: true, grants: [] }, names)).toEqual({ kind: 'onlyAdministrators' });
  });

  it('otherwise, says who has more there', () => {
    expect(sentenceOf({ isRestricted: false, grants: [{ group: 'sales' }] }, names)).toEqual({
      kind: 'also',
      groups: ['Sales'],
    });
  });
});
