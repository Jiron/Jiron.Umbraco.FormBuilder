// The inspector's merge tags (ROADMAP 9.4c, ADR-0173): the place a text is written for, a tag put in where the caret is,
// the paths the server offers grouped by their source and found by what the editor types, and the read itself. Pure but
// for the read, so it is tested without the DOM.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { mergeTagPathList, type MergeTagPathResponseModel } from '../../api';
import type { DesignDefinition } from '../definition';

/**
 * Where a text with tags is used, as the merge tag service names its places: the entry title, an outcome's message and
 * its redirect's address are the form's own, a mail's subject and body an email action's, and the texts of the other
 * actions (a webhook's names and values, its body and a chat's message) theirs. A hidden field's value is worked out on
 * the server when the entry arrives, so it may use every source, a default value only those the same for every visitor.
 */
export type MergeTagPlace =
  | 'visitorText'
  | 'consentText'
  | 'defaultValue'
  | 'hiddenValue'
  | 'entryTitle'
  | 'outcome'
  | 'redirect'
  | 'mail'
  | 'action';

/** The place of a setting's text: a consent's wording is the consent's own, any other a visitor's text. */
export const placeOf = (type: string, setting: string): MergeTagPlace =>
  type === 'consent' && setting === 'text' ? 'consentText' : 'visitorText';

/** The tag a path is written as. */
export const tagOf = (path: string) => `{{ ${path} }}`;

/** A text with a tag put in between `start` and `end`, which it replaces, and where the caret goes after it. */
export function withTag(text: string, start: number, end: number, tag: string): { text: string; caret: number } {
  const from = Math.max(0, Math.min(start, text.length));
  const to = Math.max(from, Math.min(end, text.length));
  return { text: text.slice(0, from) + tag + text.slice(to), caret: from + tag.length };
}

/** A path as the picker shows it. */
export interface ShownPath {
  readonly path: string;
  readonly label: string;
}

/** The paths of one source, under its heading. */
export interface PathGroup {
  readonly heading: string;
  readonly paths: readonly ShownPath[];
}

/** How the picker words what the backoffice knows: a built-in source's heading, a built-in path's name. */
export interface PathWords {
  readonly source: (source: string) => string | undefined;
  readonly path: (path: string) => string | undefined;
  readonly own: string;
}

const fold = (text: string) => text.toLocaleLowerCase();

/**
 * The paths in the server's order, grouped by source under their headings: a built-in source's from the backoffice's
 * words, every source the site added under one heading of its own. A field shows its label, a built-in path its name, a
 * site's path what the site labels it, and any path without words shows as written. Only those whose words or path hold
 * what the editor typed are kept, and a source with none left goes.
 */
export function groupPaths(paths: readonly MergeTagPathResponseModel[], words: PathWords, query = ''): PathGroup[] {
  const wanted = fold(query.trim());
  const groups = new Map<string, { heading: string; paths: ShownPath[] }>();
  for (const each of paths) {
    const label = (each.source === 'field' || each.isSiteSource ? each.label : words.path(each.path)) || each.path;
    if (wanted && !fold(label).includes(wanted) && !fold(each.path).includes(wanted)) continue;
    const key = each.isSiteSource ? '' : each.source;
    let group = groups.get(key);
    if (!group) {
      group = { heading: each.isSiteSource ? words.own : (words.source(each.source) ?? each.source), paths: [] };
      groups.set(key, group);
    }
    group.paths.push({ path: each.path, label });
  }
  return [...groups.values()];
}

/**
 * The paths a place's tags may use in the draft as the designer holds it, for the text of an element (a visitor text
 * recalls only the answers before it); undefined when they can't be read.
 */
export async function readMergeTagPaths(
  host: UmbControllerHost,
  form: string,
  place: MergeTagPlace,
  culture: string,
  definition: DesignDefinition,
  owner = '',
): Promise<MergeTagPathResponseModel[] | undefined> {
  const { data } = await tryExecute(
    host,
    mergeTagPathList({ path: { key: form }, body: { place, culture, definition, ...(owner ? { owner } : {}) } }),
    { disableNotifications: true },
  );
  return data?.paths;
}
