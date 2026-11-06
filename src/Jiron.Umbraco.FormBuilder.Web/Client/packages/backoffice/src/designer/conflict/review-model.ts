// Review both versions (ROADMAP 10.3c, IA 5.7, ADR-0200): the differences between the draft someone saved meanwhile
// ("theirs") and the draft not saved yet ("yours"), each taken from either side, and the draft that makes. A difference
// is an element's own properties, or its place, or the element added or removed with everything it holds, so a choice
// never leaves a part of it behind; a rule is one whole, and the form's name is one too. Compared with the draft both
// started from, a difference only one side made starts on that side, and one both made, or one neither comparison
// names, waits for a choice. The merge starts from theirs and takes yours where chosen, key by key. Pure.
import type { DefinitionChangeResponseModel } from '../../api';
import type { DesignDefinition } from '../definition';

/** A side of the review: the draft saved meanwhile, or the draft not saved yet. */
export type Side = 'theirs' | 'yours';

/** A difference to take from one side. */
export interface Difference {
  /**
   * Its id: `name`; `settings:<group>`; `<kind>:<key>` for an element's own properties, or the element added or removed;
   * or `place:<key>` for where it is.
   */
  readonly id: string;
  /** The changes it holds from theirs to yours, its own first; none for the name. */
  readonly changes: readonly DefinitionChangeResponseModel[];
  /**
   * Who made it since the draft both started from: one side, both, or unknown where that draft can't be read or
   * neither side's comparison names it.
   */
  readonly by: Side | 'both' | 'unknown';
}

/** The names of the form on each side, and in the draft both started from, where it is known. */
export interface Names {
  readonly theirs: string;
  readonly yours: string;
  readonly base: string | null;
}

type Json = Record<string, unknown>;

const idOf = (change: DefinitionChangeResponseModel): string =>
  change.kind === 'settings' ? `settings:${change.settingsGroup ?? ''}` : `${change.kind}:${change.key ?? ''}`;

/** Kinds whose own change and place are one choice: a rule is shown, and taken, whole. */
const WHOLE: ReadonlySet<string> = new Set(['rule', 'validationRule']);

const placeChanged = (change: DefinitionChangeResponseModel): boolean =>
  change.moved || Boolean(change.previousParentKey);

/** What a change touches: an element's own properties (its id), its place (`place:<key>`), or both. */
function touches(change: DefinitionChangeResponseModel): string[] {
  const id = idOf(change);
  if (change.kind === 'settings' || WHOLE.has(change.kind)) return [id];
  const place = `place:${change.key ?? ''}`;
  if (change.change !== 'changed') return [id, place];
  return [...(change.properties.length > 0 ? [id] : []), ...(placeChanged(change) ? [place] : [])];
}

/**
 * The differences from theirs to yours, in the comparison's order; `fromBase` are the changes each side made since the
 * draft both started from, null where it can't be read.
 */
export function differencesOf(
  theirsToYours: readonly DefinitionChangeResponseModel[],
  fromBase: {
    readonly theirs: readonly DefinitionChangeResponseModel[];
    readonly yours: readonly DefinitionChangeResponseModel[];
  } | null,
  names: Names,
): Difference[] {
  // An element added or removed with what holds it belongs to the topmost one added or removed.
  const lonely = new Map<string, DefinitionChangeResponseModel>();
  for (const change of theirsToYours)
    if (change.key && (change.change === 'added' || change.change === 'removed')) lonely.set(change.key, change);
  const topOf = (change: DefinitionChangeResponseModel): DefinitionChangeResponseModel => {
    let top = change;
    for (let parent = lonely.get(top.parentKey ?? ''); parent && parent.change === change.change;) {
      top = parent;
      parent = lonely.get(top.parentKey ?? '');
    }
    return top;
  };

  const units = new Map<string, DefinitionChangeResponseModel[]>();
  const add = (id: string, change: DefinitionChangeResponseModel, first: boolean) =>
    units.set(id, first ? [change, ...(units.get(id) ?? [])] : [...(units.get(id) ?? []), change]);
  for (const change of theirsToYours) {
    if (change.change === 'changed' && change.kind !== 'settings' && !WHOLE.has(change.kind)) {
      // Its own properties and its place are two choices, so taking one side's label keeps the other side's move.
      if (change.properties.length > 0) add(idOf(change), { ...change, moved: false, previousParentKey: null }, true);
      if (placeChanged(change)) add(`place:${change.key ?? ''}`, { ...change, properties: [] }, true);
      continue;
    }
    const own = change.change === 'changed' ? change : topOf(change);
    add(idOf(own), change, own === change);
  }

  const theirs = new Set(fromBase?.theirs.flatMap(touches));
  const yours = new Set(fromBase?.yours.flatMap(touches));
  const madeBy = (aspects: readonly string[]): Difference['by'] => {
    if (!fromBase) return 'unknown';
    const byTheirs = aspects.some((one) => theirs.has(one));
    const byYours = aspects.some((one) => yours.has(one));
    if (byTheirs && byYours) return 'both';
    return byTheirs ? 'theirs' : byYours ? 'yours' : 'unknown';
  };

  const differences: Difference[] = [];
  if (names.theirs !== names.yours) {
    const by =
      names.base === null
        ? 'unknown'
        : names.base === names.theirs
          ? 'yours'
          : names.base === names.yours
            ? 'theirs'
            : 'both';
    differences.push({ id: 'name', changes: [], by });
  }
  for (const [id, changes] of units) differences.push({ id, changes, by: madeBy(changes.flatMap(touches)) });
  return differences;
}

/** The side a difference starts on: where one side made it, that side; otherwise none, so it waits for a choice. */
export const startingSide = (difference: Difference): Side | undefined =>
  difference.by === 'theirs' || difference.by === 'yours' ? difference.by : undefined;

// ---- The merge -------------------------------------------------------------------------------------------------------

/** What each kind of element holds its children in, and the kind of those children. */
const HOLDS: Readonly<Record<string, string>> = { page: 'groups', group: 'fields', field: 'fields' };
const CHILD: Readonly<Record<string, string>> = { page: 'group', group: 'field', field: 'field' };
const ROOT: Readonly<Record<string, string>> = {
  page: 'pages',
  validationRule: 'validationRules',
  action: 'actions',
  outcome: 'outcomes',
};

/** Where an element sits: its node, the array that holds it, and the key of the element holding that array. */
interface Spot {
  readonly kind: string;
  readonly node: Json;
  readonly list: Json[];
  readonly parent: string | null;
  readonly holder: string;
}

const arrayIn = (node: Json, property: string): Json[] =>
  Array.isArray(node[property]) ? (node[property] as Json[]) : [];

/** Every keyed element of a definition by key: pages, groups, fields, checks, actions, outcomes and their rules. */
function spots(definition: Json): Map<string, Spot> {
  const found = new Map<string, Spot>();
  const visit = (kind: string, list: Json[], parent: string | null, holder: string) => {
    for (const node of list) {
      const key = String(node['key'] ?? '');
      found.set(key, { kind, node, list, parent, holder });
      visitRules(node, key);
      const holds = HOLDS[kind];
      if (holds) visit(CHILD[kind] ?? 'field', arrayIn(node, holds), key, holds);
    }
  };
  const visitRules = (node: Json, key: string) => {
    for (const rule of arrayIn(node, 'rules'))
      found.set(String(rule['key'] ?? ''), {
        kind: 'rule',
        node: rule,
        list: arrayIn(node, 'rules'),
        parent: key,
        holder: 'rules',
      });
  };
  visit('page', arrayIn(definition, 'pages'), null, 'pages');
  for (const kind of ['validationRule', 'action', 'outcome'] as const) {
    const list = arrayIn(definition, ROOT[kind] ?? '');
    for (const node of list) {
      const key = String(node['key'] ?? '');
      found.set(key, { kind, node, list, parent: null, holder: ROOT[kind] ?? '' });
      visitRules(node, key);
    }
  }
  return found;
}

/** The properties an element holds of its own: all but its key, its rules and the elements it holds. */
const ownKeys = (kind: string, node: Json): string[] =>
  Object.keys(node).filter((name) => name !== 'key' && name !== 'rules' && name !== HOLDS[kind]);

/** The keys of every element inside a node, its rules among them. */
function keysWithin(kind: string, node: Json): string[] {
  const keys = arrayIn(node, 'rules').map((rule) => String(rule['key'] ?? ''));
  const holds = HOLDS[kind];
  if (holds)
    for (const child of arrayIn(node, holds))
      keys.push(String(child['key'] ?? ''), ...keysWithin(CHILD[kind] ?? 'field', child));
  return keys;
}

/** The array a parent holds its children of a kind in, made where it has none yet; undefined without that parent. */
function listOf(result: Json, found: Map<string, Spot>, parent: string | null, holder: string): Json[] | undefined {
  const node = parent === null ? result : found.get(parent)?.node;
  if (!node) return undefined;
  if (!Array.isArray(node[holder])) node[holder] = [];
  return node[holder] as Json[];
}

/** Puts a node where yours has it: after the nearest sibling before it in yours that the result holds there too. */
function place(result: Json, found: Map<string, Spot>, yoursSpot: Spot, node: Json): boolean {
  const list = listOf(result, found, yoursSpot.parent, yoursSpot.holder);
  if (!list) return false;
  const before = yoursSpot.list.slice(0, yoursSpot.list.indexOf(yoursSpot.node)).map((one) => String(one['key']));
  let at = 0;
  for (let i = before.length - 1; i >= 0; i--) {
    const index = list.findIndex((one) => String(one['key']) === before[i]);
    if (index >= 0) {
      at = index + 1;
      break;
    }
  }
  list.splice(at, 0, node);
  return true;
}

const clone = <T>(value: T): T => structuredClone(value);

/** Takes a node out of the array that holds it. */
const unhook = (spot: Spot) => spot.list.splice(spot.list.indexOf(spot.node), 1);

/**
 * The draft that takes each difference in `fromYours` from yours and every other one from theirs. An element yours
 * keeps elsewhere leaves a container yours removed for where yours has it, so nothing either side keeps is lost; where
 * that place isn't in the draft, the container stays, holding only what yours keeps.
 */
export function merged(
  theirs: DesignDefinition,
  yours: DesignDefinition,
  differences: readonly Difference[],
  fromYours: ReadonlySet<string>,
): DesignDefinition {
  const result = clone(theirs) as Json;
  const mine = yours as unknown as Json;
  const yoursSpots = spots(mine);
  const taken = differences.filter((difference) => fromYours.has(difference.id) && difference.changes.length > 0);
  const own = (difference: Difference) => difference.changes[0] as DefinitionChangeResponseModel;
  const isPlace = (difference: Difference) => difference.id.startsWith('place:');
  const of = (kind: string) => taken.filter((difference) => !isPlace(difference) && own(difference).change === kind);

  // Settings, a group at a time.
  const settings = (result['settings'] ?? (result['settings'] = {})) as Json;
  for (const difference of taken) {
    const change = own(difference);
    if (change.kind !== 'settings' || !change.settingsGroup) continue;
    const group = (mine['settings'] as Json | undefined)?.[change.settingsGroup];
    if (group === undefined) Reflect.deleteProperty(settings, change.settingsGroup);
    else settings[change.settingsGroup] = clone(group);
  }

  // Elements' own properties.
  let found = spots(result);
  const changed = of('changed').filter((difference) => own(difference).kind !== 'settings');
  for (const difference of changed) {
    const change = own(difference);
    const target = found.get(change.key ?? '');
    const source = yoursSpots.get(change.key ?? '');
    if (!target || !source) continue;
    for (const name of ownKeys(target.kind, target.node)) Reflect.deleteProperty(target.node, name);
    for (const name of ownKeys(source.kind, source.node)) target.node[name] = clone(source.node[name]);
  }

  // Elements yours added, each with what it holds but what the result holds already, which keeps its own choice.
  for (const difference of of('added')) {
    const change = own(difference);
    const source = yoursSpots.get(change.key ?? '');
    if (!source) continue;
    const node = clone(source.node);
    found = spots(result);
    const held = new Set(found.keys());
    const strip = (kind: string, parent: Json) => {
      if (Array.isArray(parent['rules']))
        parent['rules'] = (parent['rules'] as Json[]).filter((rule) => !held.has(String(rule['key'])));
      const holds = HOLDS[kind];
      if (!holds || !Array.isArray(parent[holds])) return;
      parent[holds] = (parent[holds] as Json[]).filter((child) => !held.has(String(child['key'])));
      for (const child of parent[holds] as Json[]) strip(CHILD[kind] ?? 'field', child);
    };
    strip(source.kind, node);
    place(result, found, source, node);
  }

  // Places yours changed, a rule's with the rule: after the additions, so an element moved into one added there finds
  // it. One whose place in yours isn't in the draft stays where it is.
  const moves = [...taken.filter(isPlace), ...changed.filter((difference) => WHOLE.has(own(difference).kind))];
  for (const difference of moves) {
    const change = own(difference);
    if (!placeChanged(change)) continue;
    found = spots(result);
    const target = found.get(change.key ?? '');
    const source = yoursSpots.get(change.key ?? '');
    if (!target || !source || !listOf(result, found, source.parent, source.holder)) continue;
    unhook(target);
    place(result, spots(result), source, target.node);
  }

  // Elements yours removed. What yours keeps of one, elsewhere, goes where yours has it first.
  for (const difference of of('removed')) {
    const key = own(difference).key ?? '';
    for (let rescued = true; rescued;) {
      rescued = false;
      found = spots(result);
      const target = found.get(key);
      if (!target) break;
      const inside = new Set(keysWithin(target.kind, target.node));
      for (const one of inside) {
        const source = yoursSpots.get(one);
        const spot = found.get(one);
        // A rule goes with its element, and an element whose place in yours is inside goes once that place is out.
        if (!source || !spot || source.kind === 'rule' || source.parent === null) continue;
        if (source.parent === key || inside.has(source.parent) || !found.has(source.parent)) continue;
        unhook(spot);
        place(result, spots(result), source, spot.node);
        rescued = true;
        break;
      }
    }
    found = spots(result);
    const target = found.get(key);
    if (!target) continue;
    if (!keysWithin(target.kind, target.node).some((one) => yoursSpots.has(one))) {
      unhook(target);
      continue;
    }
    // What can't go where yours has it keeps the container, which holds only that.
    prune(target.kind, target.node, yoursSpots);
  }

  return result as unknown as DesignDefinition;
}

/** Leaves in a node only what yours has, or what holds something yours has. */
function prune(kind: string, node: Json, yoursSpots: ReadonlyMap<string, Spot>) {
  const keeps = (key: string) => yoursSpots.has(key);
  if (Array.isArray(node['rules']))
    node['rules'] = (node['rules'] as Json[]).filter((rule) => keeps(String(rule['key'])));
  const holds = HOLDS[kind];
  if (!holds || !Array.isArray(node[holds])) return;
  const childKind = CHILD[kind] ?? 'field';
  node[holds] = (node[holds] as Json[]).filter(
    (child) => keeps(String(child['key'])) || keysWithin(childKind, child).some(keeps),
  );
  for (const child of node[holds] as Json[]) if (!keeps(String(child['key']))) prune(childKind, child, yoursSpots);
}
