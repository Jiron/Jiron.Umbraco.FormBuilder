// The audit log in words (ROADMAP 12.4b, ADR-0219): an action's sentence from the facts its details hold, the place it
// happened, and who did it. The details hold keys, counts and identifiers, never content (ADR-0041); the server answers
// the names of the keys a page names, and an item that is gone has none, so it reads as deleted.
import type { AuditEntryResponseModel, AuditNameResponseModel } from '../api';

/** What an action's sentence may say, from its details; what it doesn't hold is empty, or 0. */
export interface AuditFacts {
  /** How many entries, files, actions, rows, settings or versions it was about. */
  readonly count: number;
  /** The version it saved, published or named. */
  readonly version: string;
  /** Where it came from: the version a restore took, or the state an entry left, in words. */
  readonly from: string;
  /** The state an entry went to, in words. */
  readonly to: string;
  /** The entry's number. */
  readonly entry: string;
  /** The field's label. */
  readonly field: string;
  /** The user group's name. */
  readonly group: string;
  /** The one setting a save changed, by its name. */
  readonly changed: string;
}

type Term = (key: string, ...args: unknown[]) => string;

/** The names the server answered for a page, by key. */
export type AuditNames = Readonly<Record<string, AuditNameResponseModel>>;

/** Where each settings section sits in the sidebar: its group and its item (IA 1). */
const SECTION_ITEMS: Readonly<Record<string, readonly [string, string]>> = {
  general: ['settings', 'general'],
  email: ['settings', 'email'],
  protection: ['settings', 'spamProtection'],
  files: ['settings', 'files'],
  integrations: ['settings', 'integrations'],
  privacy: ['privacy', 'dataCollection'],
};

/** The items whose name an action is on, each with the words for one that is gone. */
const NAMED = new Set(['form', 'folder', 'optionList', 'theme', 'emailLayout', 'template', 'userGroup']);

const detail = (entry: AuditEntryResponseModel, name: string): unknown =>
  (entry.details as Record<string, unknown> | null | undefined)?.[name];

const numberOf = (value: unknown): number => (typeof value === 'number' && Number.isFinite(value) ? value : 0);

const textOf = (value: unknown): string =>
  typeof value === 'string' || typeof value === 'number' ? String(value) : '';

/** The number of the entry an action is on, or names in its details. */
function entryNumberOf(entry: AuditEntryResponseModel, names: AuditNames): string {
  const key = entry.entityType === 'entry' ? entry.entityKey : textOf(detail(entry, 'entry'));
  const number = key ? names[key]?.number : undefined;
  return number === undefined || number === null ? '' : String(number);
}

/** How many things an action was about, by the first count its details hold. */
function countOf(entry: AuditEntryResponseModel): number {
  for (const name of ['entries', 'files', 'actions', 'rows', 'fieldCount', 'grants']) {
    const value = numberOf(detail(entry, name));
    if (value > 0) return value;
  }
  const versions = detail(entry, 'versions');
  return Array.isArray(versions) ? versions.length : 0;
}

/**
 * The facts of an action. `setting` names a settings field by its section and path, or answers '' for one it has no words
 * for; `term` is the activity area's.
 */
export function factsOf(
  entry: AuditEntryResponseModel,
  names: AuditNames,
  term: Term,
  setting: (section: string, field: string) => string,
): AuditFacts {
  const state = (value: unknown) => {
    const name = textOf(value);
    return name ? term(`entryState_${name}`) : '';
  };
  const isMove = entry.action === 'entry.stateChanged' || entry.action === 'entry.stateChangeUndone';
  const fields = detail(entry, 'fields');
  const section = textOf(detail(entry, 'section'));
  const oneField =
    entry.action === 'settings.saved' && Array.isArray(fields) && fields.length === 1 && typeof fields[0] === 'string'
      ? setting(section, fields[0].split('.')[0] ?? '')
      : '';
  const fieldKey = textOf(detail(entry, 'field'));
  const groupKey = textOf(detail(entry, 'group'));
  return {
    count: countOf(entry),
    version: textOf(detail(entry, 'version')),
    from: isMove ? state(detail(entry, 'from')) : textOf(detail(entry, 'from')),
    to: isMove ? state(detail(entry, 'to')) : '',
    entry: entryNumberOf(entry, names),
    field: fieldKey ? (names[fieldKey]?.name ?? '') : '',
    group: groupKey ? (names[groupKey]?.name ?? '') : '',
    changed: oneField,
  };
}

/** An action's sentence; an action with no words says its code, which a unit test keeps from happening. */
export function sentenceOf(entry: AuditEntryResponseModel, facts: AuditFacts, term: Term): string {
  const key = `audit_${entry.action.replace('.', '_')}`;
  const said = term(key, facts);
  return said === key ? entry.action : said;
}

/** Who did it: their name, Jiron Form Builder for its own work, or a user who is gone. */
export function whoOf(entry: AuditEntryResponseModel, term: Term): string {
  if (entry.userName) return entry.userName;
  return entry.userKey ? term('userGone') : term('system');
}

/**
 * Where it happened: the item by its name now, an entry by its number and form, a settings section by its place in the
 * sidebar (with the site of a site's save), or the screen it was done on. `section` is the sidebar's words.
 */
export function whereOf(entry: AuditEntryResponseModel, names: AuditNames, term: Term, section: Term): string {
  const key = entry.entityKey ?? '';
  const type = entry.entityType;
  const at = (group: string, item: string) => term('where_section', section(group), section(item));
  if (NAMED.has(type)) return names[key]?.name ?? term(`gone_${type}`);
  switch (type) {
    case 'entry': {
      const named = names[key];
      return named?.number !== undefined && named.number !== null
        ? term('where_entry', named.number, named.name)
        : term('gone_entry');
    }
    case 'entryFile': {
      const form = textOf(detail(entry, 'form'));
      return (form && names[form]?.name) || term('gone_form');
    }
    case 'settings': {
      const [group, item] = SECTION_ITEMS[textOf(detail(entry, 'section'))] ?? ['settings', 'general'];
      if (!key) return at(group, item);
      const site = names[key]?.name;
      return site ? term('where_sectionSite', section(group), section(item), site) : term('gone_site');
    }
    case 'access':
      return at('settings', 'access');
    case 'email':
      return at('activity', 'deliveries');
    case 'audit':
      return at('activity', 'auditLog');
    default:
      return term('gone_other');
  }
}

/** A grant's permissions before and after, each permission once, in the order the after list and then the before list name them. */
export function grantChangeOf(
  entry: AuditEntryResponseModel,
): { readonly name: string; readonly before: boolean; readonly after: boolean }[] {
  const listOf = (name: string) => {
    const value = detail(entry, name);
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
  };
  const before = listOf('before');
  const after = listOf('after');
  return [...new Set([...after, ...before])].map((name) => ({
    name,
    before: before.includes(name),
    after: after.includes(name),
  }));
}

/** The details each action's sentence says already, which its sidebar leaves out, so it says them once. */
const SAID: Readonly<Record<string, readonly string[]>> = {
  'form.saved': ['version'],
  'form.published': ['version'],
  'form.versionCommented': ['version'],
  'form.versionPinned': ['version'],
  'form.versionUnpinned': ['version'],
  'form.versionRestored': ['version', 'from'],
  'entries.exported': ['entries'],
  'entry.deleted': ['entries'],
  'entry.actionsRerun': ['entries'],
  'form.deleted': ['entries'],
  'entryFile.archived': ['files'],
  'form.actionsCopied': ['actions'],
  'audit.exported': ['rows'],
  'entry.sensitiveRevealed': ['field'],
  'entryFile.sensitiveDownloaded': ['field'],
};

/** Whether an action's sentence says a detail already. */
export const saysAlready = (action: string, detail: string): boolean => SAID[action]?.includes(detail) ?? false;

/** The details a sidebar lists besides the sentence, each with its words, in this order; others are the sentence's. */
export const DETAIL_ROWS = [
  'version',
  'revision',
  'entries',
  'numbers',
  'field',
  'fields',
  'format',
  'sensitive',
  'files',
  'leftOut',
  'actions',
  'folders',
  'forms',
  'rows',
  'alias',
  'previousAlias',
  'uncertain',
] as const;

/** A detail's value in words: a list joined, a key by its name, a flag as yes or no; '' for none. */
export function detailText(value: unknown, names: AuditNames, term: Term): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'boolean') return term(value ? 'yes' : 'no');
  if (Array.isArray(value))
    return value
      .map((item) => detailText(item, names, term))
      .filter(Boolean)
      .join(', ');
  const text = textOf(value);
  return names[text]?.name ?? text;
}
