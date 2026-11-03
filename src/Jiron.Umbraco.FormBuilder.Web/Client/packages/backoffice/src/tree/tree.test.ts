// How the forms tree and the header's search read the server's items (ADR-0165).
import { describe, expect, it } from 'vitest';
import type { LibraryItemResponseModel } from '../api';
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE } from '../section/entity-types';
import { resultOf } from './form-search.provider';
import { treeItemOf } from './library-tree.repository';

const item = (change: Partial<LibraryItemResponseModel>): LibraryItemResponseModel => ({
  key: 'k',
  kind: 'form',
  name: 'Contact us',
  isRestricted: false,
  isPublished: true,
  isTrashed: false,
  hasChildren: false,
  permissions: [],
  ...change,
});

describe('a tree item', () => {
  it('is a form at the root', () => {
    expect(treeItemOf(item({}))).toEqual({
      unique: 'k',
      entityType: FORM_ENTITY_TYPE,
      parent: { unique: null, entityType: LIBRARY_ROOT_ENTITY_TYPE },
      name: 'Contact us',
      hasChildren: false,
      isFolder: false,
      icon: 'icon-document',
    });
  });

  it('is a folder with children inside a folder', () => {
    expect(treeItemOf(item({ kind: 'folder', parentKey: 'p', hasChildren: true }))).toMatchObject({
      entityType: FOLDER_ENTITY_TYPE,
      parent: { unique: 'p', entityType: FOLDER_ENTITY_TYPE },
      hasChildren: true,
      isFolder: true,
      icon: 'icon-folder',
    });
  });
});

describe('a search result', () => {
  it("links to the form's workspace and names its alias", () => {
    expect(resultOf(item({ alias: 'contact' }))).toEqual({
      unique: 'k',
      entityType: FORM_ENTITY_TYPE,
      name: 'Contact us (contact)',
      icon: 'icon-document',
      href: 'section/jfb-forms/workspace/jfb-form/edit/k',
    });
  });
});
