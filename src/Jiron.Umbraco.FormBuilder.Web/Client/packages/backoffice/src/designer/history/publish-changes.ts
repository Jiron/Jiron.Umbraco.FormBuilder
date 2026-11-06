// What the publish dialog says of the draft (ROADMAP 10.3d, IA 5.7): what changed since the version visitors get, in
// History's parts and words, the changes not saved yet included, as the server compares the draft as it is now with
// that version; since the last published one for a form taken offline; and on a first publish, what the first version
// holds. Each part lists its first few lines and says how many more it has; one sentence says it all for the dialog's
// status region, so a screen reader hears which parts changed, not every line.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import type { DesignDefinition } from '../definition';
import type { FormWorkspaceContext } from '../form-workspace.context';
import { publishWords } from './change-words';
import { compareWithDraft, readDefinition, readVersions } from './versions';
import { wordsContextOf } from './words-context';

/** What the dialog says: parts of the changes under a headline, or one sentence in their place. */
export interface PublishChanges {
  /** Over the parts: since which version; null where the sentence says it all. */
  readonly headline: string | null;
  readonly parts: readonly PublishPart[];
  /**
   * What the dialog's status region says: in the parts' place, what the first version holds, that nothing changed or
   * that it can't be said; over the parts, which of them changed.
   */
  readonly said: string;
}

export interface PublishPart {
  readonly name: string;
  readonly lines: readonly string[];
  /** "And 2 more.", where the part holds more lines than it shows. */
  readonly more: string | null;
}

/** How many lines of each part the dialog shows; the History lists them all once the version is published. */
export const MOST_LINES = 3;

type Term = (key: string, ...args: unknown[]) => string;
type Json = Record<string, unknown>;

const arrayIn = (node: Json, property: string): Json[] =>
  Array.isArray(node[property]) ? (node[property] as Json[]) : [];

/** What a version holds, as counts: its pages and fields, and its rules, checks and actions where it has any. */
export function holdings(definition: DesignDefinition, term: Term): string[] {
  const root = definition as unknown as Json;
  let fields = 0;
  let rules = 0;
  // A group's fields, and a field's own, as a row holds them, each with its rules.
  const visit = (node: Json) => {
    rules += arrayIn(node, 'rules').length;
    for (const field of arrayIn(node, 'fields')) {
      fields += 1;
      visit(field);
    }
  };
  const pages = arrayIn(root, 'pages');
  for (const page of pages) {
    rules += arrayIn(page, 'rules').length;
    for (const group of arrayIn(page, 'groups')) visit(group);
  }
  const actions = arrayIn(root, 'actions');
  for (const node of [...actions, ...arrayIn(root, 'outcomes')]) rules += arrayIn(node, 'rules').length;
  const checks = arrayIn(root, 'validationRules').length;
  return [
    term('jfbHistory_changes_pages', pages.length),
    term('jfbHistory_changes_fields', fields),
    ...(rules > 0 ? [term('jfbHistory_changes_rules', rules)] : []),
    ...(checks > 0 ? [term('jfbHistory_changes_checks', checks)] : []),
    ...(actions.length > 0 ? [term('jfbHistory_changes_actions', actions.length)] : []),
  ];
}

/**
 * What the publish dialog says of the workspace's draft, read through the host that opens the dialog, in the words of
 * its localization; History's words must be loaded. It never fails: what can't be read is said so.
 */
export async function changesSinceLive(
  host: UmbClassInterface,
  context: FormWorkspaceContext,
  localize: UmbLocalizationController,
): Promise<PublishChanges> {
  const term: Term = (key, ...args) => localize.term(key, ...args);
  const say = (text: string): PublishChanges => ({ headline: null, parts: [], said: text });
  const failed = say(term('jfbHistory_publish_failed'));
  try {
    const data = context.getData();
    const key = context.getUnique();
    if (!data?.definition || !key) return failed;
    let since = data.publishedVersion;
    let last = false;
    if (since === null) {
      // Taken offline, a form has no version visitors get; the last one they got is the newest published one.
      const published = await readVersions(host, key, false, 0, 1);
      if (!published) return failed;
      const newest = published.items[0]?.version.number;
      if (newest === undefined)
        return say(term('jfbHistory_publish_first', term('jfbHistory_listOf', ...holdings(data.definition, term))));
      since = newest;
      last = true;
    }
    const [older, compared] = await Promise.all([
      readDefinition(host, key, since),
      compareWithDraft(host, key, since, data.definition),
    ]);
    if (!older || !compared) return failed;
    const words = await wordsContextOf(host, localize, context.getFieldTypes(), older, compared.definition);
    const parts = publishWords(compared.changes, words).map((part) => ({
      name: term(`jfbHistory_part_${part.name}`),
      lines: part.lines.slice(0, MOST_LINES),
      more: part.lines.length > MOST_LINES ? term('jfbHistory_publish_more', part.lines.length - MOST_LINES) : null,
    }));
    if (parts.length === 0) return say(term(last ? 'jfbHistory_publish_noneLast' : 'jfbHistory_publish_none'));
    const names = term('jfbHistory_listOf', ...parts.map((part) => part.name));
    return {
      headline: term(last ? 'jfbHistory_publish_sinceLast' : 'jfbHistory_publish_since'),
      parts,
      said: term(last ? 'jfbHistory_publish_saidLast' : 'jfbHistory_publish_said', names),
    };
  } catch {
    return failed;
  }
}
