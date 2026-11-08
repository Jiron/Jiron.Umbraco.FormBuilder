// An option list as its screens hold it (ROADMAP 12.1, ADR-0214): its kinds in the create dialog's order, the field the
// inspector's option editor edits it as, the times a list keeps what it fetches, the preview's first options, and the
// problems a save answered with, by where each is. Pure, so it is tested without the DOM.
import type { DesignField } from '../designer/definition';
import type { DesignOption, OptionSourceKind } from '../designer/inspector/option-values';
import { aliasOf } from '../designer/new-elements';

/** A list's kind, as the management API names it. */
export type ListKind = 'manual' | 'content' | 'dataType' | 'url' | 'file' | 'countries' | 'code';

/** The kinds in the create dialog's order: options typed in first, then those a source gives (ADR-0214). */
export const KINDS: readonly ListKind[] = ['manual', 'content', 'dataType', 'url', 'file', 'countries', 'code'];

/** Whether a text names a kind. */
export const isKind = (kind: string | null | undefined): kind is ListKind => KINDS.includes(kind as ListKind);

/** A list as its workspace holds it. */
export interface OptionListDetailModel {
  readonly entityType: string;
  /** Its key; a new list's is the one it is created with. */
  readonly unique: string;
  readonly name: string;
  readonly alias: string;
  /** Its kind; null for a kind this version lacks, which its editor shows and does not change. */
  readonly kind: ListKind | null;
  readonly config: Readonly<Record<string, unknown>>;
  readonly items: readonly DesignOption[];
  readonly cacheSeconds: number;
  /** The version a save is based on; 0 for a list not saved yet. */
  readonly concurrencyVersion: number;
}

/** The key of the field the option editor edits a list as: none of a form's, so nothing a form holds is touched. */
export const LIST_FIELD_KEY = 'jfb-option-list';

/** Where the inspector's option editor takes a kind's options from (ADR-0214): its own items for a list that holds them. */
export const sourceOf = (kind: ListKind): OptionSourceKind => (kind === 'manual' || kind === 'file' ? 'items' : kind);

/** The field the option editor edits a list's contents as: a dropdown whose options come from the list's kind. */
export function fieldOf(list: Pick<OptionListDetailModel, 'kind' | 'config' | 'items'>): DesignField {
  const source = sourceOf(list.kind ?? 'manual');
  return {
    key: LIST_FIELD_KEY,
    type: 'dropdown',
    options: source === 'items' ? { source, items: list.items } : { source, config: list.config },
  };
}

/** A list's contents back from the field the option editor changed. */
export function contentsOf(field: DesignField): Pick<OptionListDetailModel, 'config' | 'items'> {
  const options = (field['options'] as { items?: readonly DesignOption[]; config?: Record<string, unknown> }) ?? {};
  return { items: options.items ?? [], config: options.config ?? {} };
}

/** What a list's options are for in the option editor: any choice field, so every option's details are offered. */
export const LIST_TYPE = { features: ['options', 'needsOptions', 'places'] } as const;

/** The times a list may keep what it fetches, in seconds: not at all, then a minute to 30 days. */
export const KEEP_TIMES: readonly number[] = [0, 60, 300, 900, 3600, 6 * 3600, 86400, 7 * 86400, 30 * 86400];

/** How long a new list of a kind keeps what it fetches: an hour for a JSON address, nothing for any other. */
export const keepOfNew = (kind: ListKind): number => (kind === 'url' ? 3600 : 0);

/**
 * The keep times a list of a kind offers: those from its shortest on, with the list's own where it is none of them, so
 * a value set elsewhere is shown and kept.
 */
export function keepTimes(minSeconds: number, current: number): number[] {
  const offered = KEEP_TIMES.filter((seconds) => seconds >= minSeconds);
  return offered.includes(current) || current < minSeconds ? offered : [...offered, current].sort((a, b) => a - b);
}

/** A keep time in its largest whole unit, for its words: `{ unit: 'hours', count: 6 }`; none for 0. */
export function keepUnit(seconds: number): {
  readonly unit: 'none' | 'seconds' | 'minutes' | 'hours' | 'days';
  readonly count: number;
} {
  if (seconds <= 0) return { unit: 'none', count: 0 };
  if (seconds % 86400 === 0) return { unit: 'days', count: seconds / 86400 };
  if (seconds % 3600 === 0) return { unit: 'hours', count: seconds / 3600 };
  if (seconds % 60 === 0) return { unit: 'minutes', count: seconds / 60 };
  return { unit: 'seconds', count: seconds };
}

/** A list's alias from its name, as a field's is made (section 4.3.1); `list` when the name gives none. */
export const aliasOfName = (name: string): string => aliasOf(name, 'list');

/** Whether an alias keeps the rule a list's alias is held to: a lowercase letter, then up to 63 letters and digits. */
export const isAlias = (alias: string): boolean => /^[a-z][A-Za-z0-9]{0,63}$/.test(alias);

/** The preview's first options, and how many more there are. */
export function firstOf<T>(
  options: readonly T[],
  shown: number,
): { readonly first: readonly T[]; readonly more: number } {
  return { first: options.slice(0, shown), more: Math.max(0, options.length - shown) };
}

/** A problem a save or the preview answered with. */
export interface ListProblem {
  readonly code: string;
  readonly path: string;
  readonly message: string;
}

/** Where a problem shows in the editor. */
export type ProblemPlace = 'name' | 'alias' | 'contents' | 'keep' | 'file' | 'other';

/** Where a problem's path puts it: the header's name or alias, the kind's box, the keep time, or a line of its own. */
export function placeOf(path: string): ProblemPlace {
  if (path === '$.name') return 'name';
  if (path === '$.alias') return 'alias';
  if (path === '$.cacheSeconds') return 'keep';
  if (path === '$.file') return 'file';
  if (path.startsWith('$.config') || path.startsWith('$.items')) return 'contents';
  return 'other';
}

/** The problems at a place, in the order the server gave them. */
export const problemsAt = (problems: readonly ListProblem[], place: ProblemPlace): ListProblem[] =>
  problems.filter((problem) => placeOf(problem.path) === place);
