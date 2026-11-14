// What Settings > Access and a folder's or form's Permissions sidebar work out (ROADMAP 12.4a, ADR-0013, ADR-0218):
// a box ticked with the permission it needs, and one unticked with those that need it, as the server refuses a grant
// without its dependency; the grants a save sends, each changed group's from what the editor saw; and the sentence a
// folder or form with access of its own says on the screen's list.
import type { AccessNeedResponseModel } from '../api';

/** The grants of each group, by its key, as sets of permission names. */
export type Grants = ReadonlyMap<string, ReadonlySet<string>>;

/** A group's grant as a save sends it. */
export interface GrantEdit {
  readonly group: string;
  readonly expected: string[];
  readonly permissions: string[];
}

/** The permission names a grant holds, in the order the server names them. */
export const namesIn = (granted: ReadonlySet<string>, order: readonly string[]): string[] =>
  order.filter((name) => granted.has(name));

/**
 * A grant with one permission turned on or off: on, with the permission it needs, and the one that needs, and so on;
 * off, with every permission that needs it. Returns the grant and the permissions it changed besides the one asked.
 */
export function withPermission(
  granted: ReadonlySet<string>,
  permission: string,
  on: boolean,
  needs: readonly AccessNeedResponseModel[],
): { readonly granted: ReadonlySet<string>; readonly besides: readonly string[] } {
  const next = new Set(granted);
  const besides: string[] = [];
  const turn = (name: string, value: boolean) => {
    if (next.has(name) === value) return;
    if (value) next.add(name);
    else next.delete(name);
    if (name !== permission) besides.push(name);
    if (value) {
      for (const need of needs) if (need.permission === name) turn(need.needs, true);
    } else {
      for (const need of needs) if (need.needs === name) turn(need.permission, false);
    }
  };
  turn(permission, on);
  return { granted: next, besides };
}

/** The grants a save sends: each group whose permissions changed, from what the editor saw, in the server's order. */
export function changedGrants(before: Grants, now: Grants, order: readonly string[]): GrantEdit[] {
  const edits: GrantEdit[] = [];
  for (const [group, granted] of now) {
    const expected = namesIn(before.get(group) ?? new Set(), order);
    const permissions = namesIn(granted, order);
    if (expected.join() !== permissions.join()) edits.push({ group, expected, permissions });
  }
  return edits;
}

/**
 * The grants an answer read again brings, with each group the user changed and has not saved kept as the user left it:
 * the screen reads again after a folder's or form's sidebar saved, which changes none of these.
 */
export function withPending(before: Grants, mine: Grants, fresh: Grants): Grants {
  const next = new Map(fresh);
  for (const [group, granted] of mine) {
    const was = before.get(group) ?? new Set<string>();
    const same = was.size === granted.size && [...granted].every((name) => was.has(name));
    if (!same && fresh.has(group)) next.set(group, granted);
  }
  return next;
}

/** The grants of the groups an answer lists, as sets. */
export const grantsOf = (
  groups: readonly { readonly key: string; readonly permissions: readonly string[] }[],
): Grants => new Map(groups.map((group) => [group.key, new Set(group.permissions)]));

/** What a folder or form with access of its own says: who alone may see it, or who may also. */
export type ItemSentence =
  | { readonly kind: 'only'; readonly groups: readonly string[] }
  | { readonly kind: 'onlyAdministrators' }
  | { readonly kind: 'also'; readonly groups: readonly string[] };

/** The sentence of a folder or form: restricted, the groups with grants on it alone; otherwise those with more there. */
export function sentenceOf(
  item: { readonly isRestricted: boolean; readonly grants: readonly { readonly group: string }[] },
  nameOf: (group: string) => string,
): ItemSentence {
  const groups = item.grants.map((grant) => nameOf(grant.group));
  if (item.isRestricted) return groups.length === 0 ? { kind: 'onlyAdministrators' } : { kind: 'only', groups };
  return { kind: 'also', groups };
}
