// The designer's moves (ROADMAP 9.3c, ADR-0169): where an element is after a move, as the live region says it, the
// places Move to... offers, each with the edit it would make, so the dialog shows the ones the form refuses with why
// before anything is chosen, and whether "Move what follows to a new page" has anything to move (ADR-0256). Pure over
// the definition, as the editor model is.
import { locate, pageOf, type DesignDefinition, type DesignField } from '../definition';
import { move, type Edit, type EditorState, type ModelContext, type Place } from '../editor-model';

/**
 * The fields and groups that something follows on their page, which "Move what follows to a new page" moves there: a
 * field after one in its group, or a group after its group; a group after a group. A repeatable group's field splits
 * the page after its repeatable group, so what follows that counts. A page has nothing of the kind. One pass over the
 * form, so the canvas asks it once for all its fields (section 4.12).
 */
export function followedKeys(definition: DesignDefinition): ReadonlySet<string> {
  const keys = new Set<string>();
  const addAll = (field: DesignField) => {
    keys.add(field.key);
    for (const child of field.fields ?? []) addAll(child);
  };
  for (const page of definition.pages)
    page.groups.forEach((group, at) => {
      const later = at < page.groups.length - 1;
      if (later) keys.add(group.key);
      group.fields.forEach((field, index) => {
        if (later || index < group.fields.length - 1) addAll(field);
      });
    });
  return keys;
}

/** Whether something follows an element on its page (`followedKeys`). */
export const followsOnPage = (definition: DesignDefinition, key: string): boolean => followedKeys(definition).has(key);

/** Where an element is: its place from 1 among its container's elements, how many there are, its container and page. */
export interface Position {
  readonly kind: 'page' | 'group' | 'field';
  readonly position: number;
  readonly count: number;
  /** The key of its group or repeatable group for a field, of its page for a group; null for a page. */
  readonly container: string | null;
  /** The index of its page. */
  readonly page: number;
}

/** Where an element is, as a move's announcement says it. */
export function positionOf(definition: DesignDefinition, key: string): Position | undefined {
  const found = locate(definition, key);
  if (!found) return undefined;
  if (found.kind === 'page')
    return {
      kind: 'page',
      position: found.index + 1,
      count: definition.pages.length,
      container: null,
      page: found.index,
    };
  const pageKey = pageOf(definition, key)?.key;
  const page = definition.pages.findIndex((each) => each.key === pageKey);
  const count = found.kind === 'group' ? found.container.groups.length : (found.container.fields ?? []).length;
  return { kind: found.kind, position: found.index + 1, count, container: found.container.key, page };
}

/** The place at the end of a page (for a group) or of a group or repeatable group (for a field). */
export function endOf(definition: DesignDefinition, container: string): Place | null {
  const found = locate(definition, container);
  if (!found) return null;
  if (found.kind === 'page') return { container, index: found.element.groups.length };
  return { container, index: (found.element.fields ?? []).length };
}

/** A place Move to... offers: the end of a page, a group or a repeatable group, with the edit a move there makes. */
export interface MovePlace {
  readonly key: string;
  readonly kind: 'page' | 'group' | 'repeater';
  /** The group a repeatable group lies in; null for a page or a group. */
  readonly within: string | null;
  /** Whether the element is in it now. */
  readonly current: boolean;
  readonly edit: Edit;
}

/** The places of one page, or for a group the pages themselves (page -1). */
export interface MoveSection {
  readonly page: number;
  readonly places: readonly MovePlace[];
}

const contains = (field: DesignField, key: string): boolean =>
  (field.fields ?? []).some((child) => child.key === key || contains(child, key));

// The repeatable groups among fields, in reading order, a repeatable group inside another after it.
const repeatersIn = (fields: readonly DesignField[]): DesignField[] =>
  fields.flatMap((field) => (field.fields ? [field, ...repeatersIn(field.fields)] : []));

/**
 * The places Move to... offers an element. A field goes to the end of any group or repeatable group, by page, except
 * itself and what it holds; a group goes to the end of any page. A page is moved among the steps and has none.
 */
export function moveToPlaces(state: EditorState, key: string, context: ModelContext): MoveSection[] {
  const found = locate(state.definition, key);
  if (!found || found.kind === 'page') return [];
  const { pages } = state.definition;
  const to = (container: string, index: number) => move(state, key, { container, index }, context);
  if (found.kind === 'group')
    return [
      {
        page: -1,
        places: pages.map((page) => ({
          key: page.key,
          kind: 'page',
          within: null,
          current: page.key === found.container.key,
          edit: to(page.key, page.groups.length),
        })),
      },
    ];
  const moved = found.element;
  return pages.map((page, index) => ({
    page: index,
    places: page.groups.flatMap((group): MovePlace[] => [
      {
        key: group.key,
        kind: 'group',
        within: null,
        current: found.container.key === group.key,
        edit: to(group.key, group.fields.length),
      },
      ...repeatersIn(group.fields)
        .filter((repeater) => repeater.key !== key && !contains(moved, repeater.key))
        .map((repeater): MovePlace => ({
          key: repeater.key,
          kind: 'repeater',
          within: group.key,
          current: found.container.key === repeater.key,
          edit: to(repeater.key, (repeater.fields ?? []).length),
        })),
    ]),
  }));
}
