// What the library's items share (ROADMAP 12.1, IA 7, ADR-0214, ADR-0215): an option list, a theme and an email layout
// each have a list screen, a workspace with its name and alias in the header and the forms that use it, and Duplicate and
// Delete... in its "...". A kind says its words' prefix, its addresses and how its endpoints answer; the screens, the
// workspace's header and the actions here do the rest the same way for each.
import { html } from '@umbraco-cms/backoffice/external/lit';
import type { UmbClassInterface, UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbInfoModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import type { LibraryUsersResponseModel } from '../../api';
import { screenPath } from '../../section/entity-types';
import { confirmText } from '../../ui/confirm';
import { codeOf, leaveGone, open, sayRefusal } from '../library-changes';

/** What every item's workspace model holds besides its own values. */
export interface LibraryItemModel {
  entityType: string;
  unique: string;
  name: string;
  alias: string;
  concurrencyVersion: number;
}

/** Why a save was refused, at its path. */
export interface LibraryProblem {
  readonly code: string;
  readonly path: string;
  readonly message: string;
}

/** How a save ended: saved, refused with problems at their paths, saved by someone else meanwhile, or refused otherwise. */
export type SaveAnswer<T> =
  | { readonly kind: 'saved'; readonly item: T }
  | { readonly kind: 'invalid'; readonly problems: readonly LibraryProblem[] }
  | { readonly kind: 'conflict' | 'failed'; readonly error: unknown };

/** How a delete ended. */
export type DeleteAnswer = 'deleted' | 'inUse' | 'isDefault' | 'conflict' | 'gone' | 'failed';

/** What a read answers: the item, or the refusal. */
export interface ReadAnswer<T> {
  readonly data?: T;
  readonly error?: unknown;
}

/** A kind of library item, as the shared screens, header and actions use it. */
export interface LibraryKind<T extends LibraryItemModel = LibraryItemModel> {
  /** The prefix of the kind's words, such as `jfbOptionLists`. */
  readonly words: string;
  /** Loads the kind's words for a language, once. */
  readonly load: (lang: string) => Promise<unknown>;
  /** The entity types of one item and of the list. */
  readonly entityType: string;
  readonly listEntityType: string;
  /** The rule an alias is held to, as Umbraco's alias box checks it, and an alias made of a name. */
  readonly aliasPattern: string;
  readonly isAlias: (alias: string) => boolean;
  readonly aliasOf: (name: string) => string;
  /** Whether a saved item keeps its alias, as forms name it by its alias (a theme). */
  readonly aliasFixed?: boolean;
  /** Whether the header counts the forms that use the item, or the elements of them that do (a layout's actions). */
  readonly counts: 'forms' | 'uses';
  readonly read: (host: UmbClassInterface, key: string) => Promise<ReadAnswer<T>>;
  readonly duplicate: (host: UmbClassInterface, key: string, name: string) => Promise<T | undefined>;
  readonly users: (host: UmbClassInterface, key: string) => Promise<LibraryUsersResponseModel | undefined>;
  /** The workspace's repository, so a delete lets the open item go. */
  readonly repository: () => Promise<new (host: UmbControllerHost) => ItemRepository>;
}

/** What a delete needs of the kind's detail repository. */
export interface ItemRepository {
  delete(unique: string): Promise<{ readonly error?: Error | undefined }>;
  destroy(): void;
}

/** An item's workspace address, relative to the backoffice. */
export const itemPath = (kind: LibraryKind, key: string): string => screenPath(kind.entityType, key);

/** The problems a refusal holds, each at its path. */
export function problemsOf(error: unknown): LibraryProblem[] {
  const problem = UmbApiError.isUmbApiError(error)
    ? (error.problemDetails as unknown as { errors?: readonly Partial<LibraryProblem>[] } | null | undefined)
    : undefined;
  return (problem?.errors ?? []).flatMap((one) =>
    one.path ? [{ code: one.code ?? '', path: one.path, message: one.message ?? '' }] : [],
  );
}

/** A save's answer, from the item the API gave or its refusal. */
export function saveAnswerOf<T>(item: T | undefined, error: unknown): SaveAnswer<T> {
  if (item) return { kind: 'saved', item };
  const problems = problemsOf(error);
  if (problems.length > 0) return { kind: 'invalid', problems };
  if (codeOf(error) === 'concurrency_conflict') return { kind: 'conflict', error };
  return { kind: 'failed', error };
}

/** A delete's answer from its refusal, if any; `gone` is the code of the kind's missing item. */
export function deleteAnswerOf(error: unknown, gone: string): DeleteAnswer {
  if (!error) return 'deleted';
  switch (codeOf(error)) {
    case 'in_use':
      return 'inUse';
    case 'is_default':
      return 'isDefault';
    case 'concurrency_conflict':
      return 'conflict';
    case gone:
      return 'gone';
    default:
      return 'failed';
  }
}

/** How many the header and the dialogs count: the forms, or the elements of them that use the item. */
export function countOf(kind: LibraryKind, users: LibraryUsersResponseModel): number {
  return kind.counts === 'uses'
    ? users.forms.reduce((total, form) => total + form.uses, 0) + users.otherUses
    : users.forms.length + users.others;
}

/** A word of the kind. */
export const term = (localize: UmbLocalizationController, kind: LibraryKind, key: string, ...args: unknown[]) =>
  localize.term(`${kind.words}_${key}`, ...args);

export async function say(host: UmbClassInterface, color: 'positive' | 'danger', message: string): Promise<void> {
  (await host.getContext(UMB_NOTIFICATION_CONTEXT))?.peek(color, { data: { message } });
}

/** The kind's words, which a "..." opened from elsewhere than the item's workspace may not have loaded yet. */
export async function wordsFor(host: UmbClassInterface, kind: LibraryKind): Promise<UmbLocalizationController> {
  const localize = new UmbLocalizationController(host);
  await kind.load(localize.lang()).catch(() => undefined);
  return localize;
}

/**
 * Says that forms use an item, naming those the user may open and counting the rest; false when none does, so the
 * delete may go on.
 */
export async function sayInUse(
  host: UmbClassInterface,
  kind: LibraryKind,
  key: string,
  name: string,
): Promise<boolean> {
  const users = await kind.users(host, key);
  if (!users || users.forms.length + users.others === 0) return false;
  const localize = new UmbLocalizationController(host);
  await umbInfoModal(host, {
    headline: term(localize, kind, 'inUseHeadline', name),
    content: html`<p style="margin: 0">${term(localize, kind, 'inUseText')}</p>
      <ul
        style="display: grid; gap: var(--uui-size-space-2); margin: var(--uui-size-space-4) 0 0; padding-inline-start: 1.25em"
      >
        ${users.forms.map((form) => html`<li>${form.name}</li>`)}
        ${users.others > 0 ? html`<li>${term(localize, kind, 'usersOthers', users.others)}</li>` : ''}
      </ul>`,
  }).catch(() => undefined);
  return true;
}

/** Duplicate in an item's "...": a copy named after the item, which then opens. */
export async function duplicateItem(host: UmbControllerBase, kind: LibraryKind, key: string): Promise<void> {
  const localize = await wordsFor(host, kind);
  const { data: item, error } = await kind.read(host, key);
  if (!item) {
    await sayRefusal(host, error);
    return;
  }
  const copy = await kind.duplicate(host, key, term(localize, kind, 'copyName', item.name));
  if (!copy) {
    await say(host, 'danger', term(localize, kind, 'duplicateFailed'));
    return;
  }
  await say(host, 'positive', term(localize, kind, 'duplicated', copy.name));
  open(itemPath(kind, copy.unique));
}

/**
 * Delete... in an item's "...": asked first, as an item goes for good. One that forms use is not deleted: a dialog names
 * the forms. The site's default is not deleted either, and the toast says where it is chosen. The item's workspace goes
 * back to the list once the item is gone.
 */
export async function deleteItem(host: UmbControllerBase, kind: LibraryKind, key: string): Promise<void> {
  const localize = await wordsFor(host, kind);
  const { data: item, error } = await kind.read(host, key);
  if (!item) {
    await sayRefusal(host, error);
    return;
  }
  if (await sayInUse(host, kind, key, item.name)) return;
  await confirmText(host, {
    headline: term(localize, kind, 'deleteHeadline', item.name),
    content: term(localize, kind, 'deleteText'),
    confirmLabel: term(localize, kind, 'deleteConfirm'),
    color: 'danger',
  });
  // Through the workspace's repository, so its store lets the open item go and leaving it asks nothing.
  const Repository = await kind.repository();
  const repository = new Repository(host);
  const { error: refused } = await repository.delete(key).finally(() => repository.destroy());
  if (refused) {
    const answer = refused.message as DeleteAnswer;
    // A form took the item up since the dialog was read: the forms are named now.
    if (answer === 'inUse' && (await sayInUse(host, kind, key, item.name))) return;
    const said =
      answer === 'conflict'
        ? term(localize, kind, 'changedMeanwhile')
        : answer === 'isDefault'
          ? term(localize, kind, 'isDefault')
          : term(localize, kind, 'deleteFailed');
    await say(host, 'danger', said);
    return;
  }
  await say(host, 'positive', term(localize, kind, 'deleted', item.name));
  leaveGone({ entityType: kind.entityType, unique: key }, { entityType: kind.listEntityType, unique: null });
}
