// What History reads and writes of a form's versions (ROADMAP 10.3a and 10.3b, ADR-0045, ADR-0052, ADR-0196, ADR-0198):
// a page of the list, two versions compared with their definitions, so the comparison names elements as each version
// holds them, a pin, a comment and a restore; and for Review both versions (ROADMAP 10.3c, ADR-0200) a draft not saved
// yet compared with a version. A refusal is said in the user's words where the server gives a reason; otherwise the
// caller says it.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  versionComment,
  versionCompare,
  versionGet,
  versionList,
  versionPin,
  versionCompareWith,
  versionRestore,
  type DefinitionChangeResponseModel,
  type FormVersionEntryResponseModel,
  type FormVersionResponseModel,
} from '../../api';
import { sayRefusal } from '../../library/library-changes';
import { normalise, type DesignDefinition } from '../definition';

/** A page of a form's versions, newest first. */
export interface VersionPage {
  readonly total: number;
  readonly items: readonly FormVersionEntryResponseModel[];
}

/** Two versions compared: their changes with both definitions, one that can't be read, or no answer. */
export type VersionComparison =
  | {
      readonly kind: 'compared';
      readonly changes: readonly DefinitionChangeResponseModel[];
      readonly older: DesignDefinition;
      readonly newer: DesignDefinition;
    }
  | { readonly kind: 'unreadable'; readonly number: number }
  | { readonly kind: 'failed' };

/** How many versions a page of the list holds. */
export const PAGE = 20;

/** A page of the form's versions; undefined without an answer. */
export async function readVersions(
  host: UmbClassInterface,
  key: string,
  includeDrafts: boolean,
  skip: number,
): Promise<VersionPage | undefined> {
  const { data } = await tryExecute(host, versionList({ path: { key }, query: { includeDrafts, skip, take: PAGE } }), {
    disableNotifications: true,
  });
  return data ? { total: data.total, items: data.items } : undefined;
}

/** Two versions compared, the older first, with both definitions as the server holds them. */
export async function compareVersions(
  host: UmbClassInterface,
  key: string,
  from: number,
  to: number,
): Promise<VersionComparison> {
  const read = (number: number) =>
    tryExecute(host, versionGet({ path: { key, number } }), { disableNotifications: true });
  const [older, newer, comparison] = await Promise.all([
    read(from),
    read(to),
    tryExecute(host, versionCompare({ path: { key }, query: { from, to } }), { disableNotifications: true }),
  ]);
  // A version whose stored definition can't be read is said by its number; the comparison refuses it too.
  for (const [number, version] of [
    [from, older],
    [to, newer],
  ] as const)
    if (version.data && (version.data.definition === null || version.data.definition === undefined))
      return { kind: 'unreadable', number };
  if (!older.data || !newer.data || !comparison.data) return { kind: 'failed' };
  return {
    kind: 'compared',
    changes: comparison.data.changes,
    older: normalise(older.data.definition),
    newer: normalise(newer.data.definition),
  };
}

/** The changes from one version to another, without their definitions; undefined without an answer. */
export async function changesBetween(
  host: UmbClassInterface,
  key: string,
  from: number,
  to: number,
): Promise<readonly DefinitionChangeResponseModel[] | undefined> {
  const { data } = await tryExecute(host, versionCompare({ path: { key }, query: { from, to } }), {
    disableNotifications: true,
  });
  return data?.changes;
}

/**
 * A draft not saved yet compared with a version, the version the older side, and the draft as a save would store it;
 * undefined without an answer.
 */
export async function compareWithDraft(
  host: UmbClassInterface,
  key: string,
  number: number,
  definition: DesignDefinition,
): Promise<
  { readonly changes: readonly DefinitionChangeResponseModel[]; readonly definition: DesignDefinition } | undefined
> {
  const { data } = await tryExecute(host, versionCompareWith({ path: { key, number }, body: { definition } }), {
    disableNotifications: true,
  });
  return data ? { changes: data.changes, definition: normalise(data.definition) } : undefined;
}

/** Pins a version, or unpins it; the version as it is now, or undefined after a refusal, which is said. */
export async function pinVersion(
  host: UmbClassInterface,
  key: string,
  number: number,
  pinned: boolean,
): Promise<FormVersionResponseModel | undefined> {
  const { data, error } = await tryExecute(host, versionPin({ path: { key, number }, body: { isPinned: pinned } }), {
    disableNotifications: true,
  });
  if (!data) await sayRefusal(host, error);
  return data;
}

/**
 * Copies a version into a new draft (ADR-0052, ADR-0198), based on the form as it was read; the new draft's number once it
 * is restored, or undefined after a refusal, which is said.
 */
export async function restoreVersion(
  host: UmbClassInterface,
  key: string,
  number: number,
  concurrencyVersion: number,
): Promise<number | undefined> {
  const { data, error } = await tryExecute(
    host,
    versionRestore({ path: { key, number }, body: { concurrencyVersion } }),
    { disableNotifications: true },
  );
  if (!data) await sayRefusal(host, error);
  return data?.version ?? undefined;
}

/** Sets a version's comment, empty to remove it; the version as it is now, or undefined after a refusal, which is said. */
export async function commentVersion(
  host: UmbClassInterface,
  key: string,
  number: number,
  comment: string,
): Promise<FormVersionResponseModel | undefined> {
  const { data, error } = await tryExecute(
    host,
    versionComment({ path: { key, number }, body: { comment: comment.trim() === '' ? null : comment.trim() } }),
    { disableNotifications: true },
  );
  if (!data) await sayRefusal(host, error);
  return data;
}
