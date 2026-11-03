// What the library's actions share (ADR-0166): telling the trees and screens what changed, opening a screen, reading a
// refusal's code, and saying a refusal in the user's language where a code has its own words.
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbRequestReloadChildrenOfEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { FOLDER_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, RECYCLE_BIN_ENTITY_TYPE } from '../section/entity-types';

/** An entity a change touched: a folder by its key, or the root (null). */
export interface Place {
  entityType: string;
  unique: string | null;
}

/** The folder a form or folder is in, or the root. */
export const placeOf = (folderKey: string | null | undefined): Place =>
  folderKey
    ? { entityType: FOLDER_ENTITY_TYPE, unique: folderKey }
    : { entityType: LIBRARY_ROOT_ENTITY_TYPE, unique: null };

/** The Recycle Bin, as its tree's root. */
export const RECYCLE_BIN: Place = { entityType: RECYCLE_BIN_ENTITY_TYPE, unique: null };

/** Tells the trees and the open screens that the children of these places changed. */
export async function changed(host: UmbClassInterface, ...places: Place[]): Promise<void> {
  reloadChildren(await host.getContext(UMB_ACTION_EVENT_CONTEXT), ...places);
}

/**
 * The same through the backoffice's action events, for work that runs after its host is gone: an Undo in a toast runs once
 * Umbraco has let go of the action that offered it, so the action hands it what it held.
 */
export function reloadChildren(events: EventTarget | undefined, ...places: Place[]): void {
  const seen = new Set<string>();
  for (const place of places) {
    const id = `${place.entityType}/${place.unique ?? ''}`;
    if (seen.has(id)) continue;
    seen.add(id);
    events?.dispatchEvent(new UmbRequestReloadChildrenOfEntityEvent(place));
  }
}

/** Opens a screen of the backoffice by its address below the backoffice's root. */
export function open(path: string): void {
  history.pushState(null, '', new URL(path, document.baseURI).href);
}

/** A refusal's stable code, or undefined for an answer that is none. */
export function codeOf(error: unknown): string | undefined {
  if (!error || !UmbApiError.isUmbApiError(error)) return undefined;
  return (error.problemDetails as { code?: string } | null | undefined)?.code;
}

/** The permission a refusal names, or undefined. */
export function permissionOf(error: unknown): string | undefined {
  if (!error || !UmbApiError.isUmbApiError(error)) return undefined;
  return (error.problemDetails as { permission?: string } | null | undefined)?.permission;
}

/** Says a refusal as a toast: the words its code has here, or the server's title, which Umbraco shows as it came. */
export async function sayRefusal(host: UmbClassInterface, error: unknown): Promise<void> {
  const message = refusalText(new UmbLocalizationController(host), error);
  const notifications = await host.getContext(UMB_NOTIFICATION_CONTEXT);
  notifications?.peek('danger', { data: { message } });
}

/** A refusal in words: those its code has here, the server's title, or that it failed. */
export function refusalText(localize: UmbLocalizationController, error: unknown): string {
  const code = codeOf(error);
  const permission = permissionOf(error);
  const known: Record<string, string> = {
    folder_not_empty: localize.term('jfbLibrary_folderNotEmpty'),
    form_not_found: localize.term('jfbLibrary_formGone'),
    folder_not_found: localize.term('jfbLibrary_folderGone'),
    template_not_found: localize.term('jfbLibrary_templateGone'),
    concurrency_conflict: localize.term('jfbLibrary_changedMeanwhile'),
  };
  return code === 'forbidden' && permission
    ? localize.term('jfbLibrary_missingPermission', localize.term(`jfbLibrary_permission_${permission}`))
    : (code && known[code]) ||
        (UmbApiError.isUmbApiError(error) ? (error.problemDetails?.title ?? '') : '') ||
        localize.term('jfbLibrary_failed');
}
