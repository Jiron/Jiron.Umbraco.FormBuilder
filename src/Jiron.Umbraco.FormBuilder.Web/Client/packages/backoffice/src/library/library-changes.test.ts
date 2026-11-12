// What the library's actions share (ADR-0166): one reload per place a change touched, and a refusal said in the user's words
// by its code, its permission, or the server's title.
import { UmbRequestReloadChildrenOfEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { describe, expect, it } from 'vitest';
import {
  FOLDER_ENTITY_TYPE,
  FORM_ENTITY_TYPE,
  LIBRARY_ROOT_ENTITY_TYPE,
  screenPath,
  TRASHED_FOLDER_ENTITY_TYPE,
} from '../section/entity-types';
import { codeOf, placeOf, RECYCLE_BIN, refusalText, reloadChildren, screenAfterGone } from './library-changes';

describe('a change', () => {
  it('reloads the children of each place it touched once', () => {
    const events = new EventTarget();
    const reloaded: string[] = [];
    events.addEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, (event) => {
      const args = (event as UmbRequestReloadChildrenOfEntityEvent).getEntityType();
      reloaded.push(`${args}/${(event as UmbRequestReloadChildrenOfEntityEvent).getUnique() ?? ''}`);
    });

    reloadChildren(events, placeOf(null), placeOf('f'), placeOf(null), RECYCLE_BIN);

    expect(reloaded).toEqual([`${LIBRARY_ROOT_ENTITY_TYPE}/`, `${FOLDER_ENTITY_TYPE}/f`, 'jfb-recycle-bin/']);
  });

  it("reloads nothing without the backoffice's action events", () => {
    expect(() => reloadChildren(undefined, RECYCLE_BIN)).not.toThrow();
  });
});

describe('an item gone for good', () => {
  const at = (entityType: string, key: string) => `/umbraco/${screenPath(entityType, key)}`;

  it('leaves its own open screen for the folder it was in, or the top level', () => {
    expect(screenAfterGone(at(FOLDER_ENTITY_TYPE, 'gone'), placeOf('gone'), placeOf('parent'))).toBe(
      screenPath(FOLDER_ENTITY_TYPE, 'parent'),
    );
    expect(screenAfterGone(at(FOLDER_ENTITY_TYPE, 'gone'), placeOf('gone'), placeOf(null))).toBe(
      screenPath(LIBRARY_ROOT_ENTITY_TYPE),
    );
  });

  it("leaves a trashed form's own screen for its folder's", () => {
    const form = { entityType: FORM_ENTITY_TYPE, unique: 'f' };
    expect(screenAfterGone(at(FORM_ENTITY_TYPE, 'f'), form, placeOf('folder'))).toBe(
      screenPath(FOLDER_ENTITY_TYPE, 'folder'),
    );
  });

  it('leaves any other screen as it is', () => {
    expect(screenAfterGone(at(FOLDER_ENTITY_TYPE, 'other'), placeOf('gone'), placeOf(null))).toBeUndefined();
    expect(screenAfterGone(at(TRASHED_FOLDER_ENTITY_TYPE, 'gone'), placeOf('gone'), placeOf(null))).toBeUndefined();
  });
});

describe('a refusal', () => {
  const localize = {
    term: (key: string, ...args: unknown[]) => (args.length ? `${key}(${args.join(',')})` : key),
  } as unknown as UmbLocalizationController;
  const problem = (details: Record<string, unknown>) =>
    new UmbApiError('refused', 409, null, {
      type: 'about:blank',
      status: 409,
      title: 'The server says no.',
      ...details,
    });

  it('says each code it knows in its own words', () => {
    expect(codeOf(problem({ code: 'folder_not_empty' }))).toBe('folder_not_empty');
    expect(
      [
        'folder_not_empty',
        'entries_held',
        'form_not_found',
        'folder_not_found',
        'template_not_found',
        'version_not_found',
        'concurrency_conflict',
        'tests_limited',
      ].map((code) => refusalText(localize, problem({ code }))),
    ).toEqual([
      'jfbLibrary_folderNotEmpty',
      'jfbLibrary_entriesHeld',
      'jfbLibrary_formGone',
      'jfbLibrary_folderGone',
      'jfbLibrary_templateGone',
      'jfbLibrary_versionGone',
      'jfbLibrary_changedMeanwhile',
      'jfbLibrary_testsLimited',
    ]);
  });

  it('names the permission a refusal misses', () => {
    expect(refusalText(localize, problem({ code: 'forbidden', permission: 'manageActions' }))).toBe(
      'jfbLibrary_missingPermission(jfbLibrary_permission_manageActions)',
    );
  });

  it('says what a content action needs in Umbraco that the user lacks, and the title for a gap it does not know', () => {
    expect(
      ['contentSection', 'content', 'languages', 'mediaSection', 'media'].map((gap) =>
        refusalText(localize, problem({ code: 'content_rights', gap })),
      ),
    ).toEqual([
      'jfbLibrary_contentRights_contentSection',
      'jfbLibrary_contentRights_content',
      'jfbLibrary_contentRights_languages',
      'jfbLibrary_contentRights_mediaSection',
      'jfbLibrary_contentRights_media',
    ]);
    expect(refusalText(localize, problem({ code: 'content_rights', gap: 'somewhere' }))).toBe('The server says no.');
  });

  it("says the server's title for a code it does not know, and that it failed for anything else", () => {
    expect(refusalText(localize, problem({ code: 'something_new' }))).toBe('The server says no.');
    expect(refusalText(localize, new Error('offline'))).toBe('jfbLibrary_failed');
    expect(codeOf(new Error('offline'))).toBeUndefined();
  });
});
