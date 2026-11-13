// How the forms tree, the Recycle Bin's tree and the header's search read the server's items (ADR-0165, ADR-0166).
import { render } from '@umbraco-cms/backoffice/external/lit';
import { describe, expect, it } from 'vitest';
import type { FieldMatchResponseModel, LibraryItemResponseModel } from '../api';
import {
  FOLDER_ENTITY_TYPE,
  FORM_ENTITY_TYPE,
  formPath,
  LIBRARY_ROOT_ENTITY_TYPE,
  RECYCLE_BIN_ENTITY_TYPE,
  TRASHED_FOLDER_ENTITY_TYPE,
  TRASHED_FORM_ENTITY_TYPE,
} from '../section/entity-types';
import { RESTRICTED_FLAG, UNSEEN_FLAG } from './aliases';
import { fieldResultOf, resultOf, type InWords } from './form-search.provider';
import { LibraryTreeItemElement, nameOf } from './library-tree-item.element';
import { pageOf, treeItemOf } from './library-tree.repository';

const item = (change: Partial<LibraryItemResponseModel>): LibraryItemResponseModel => ({
  key: 'k',
  kind: 'form',
  name: 'Contact us',
  isRestricted: false,
  isPublished: true,
  isTrashed: false,
  hasChildren: false,
  permissions: ['design'],
  hasUnseenEntries: false,
  ...change,
});

const inWords: InWords = (name, place) => `${name}, in ${place}`;

describe("a tree item's name", () => {
  const localize = (text: string) => (text.startsWith('#') ? `[${text.slice(1)}]` : text);

  it("is a root's in the user's language, and an editor's as it stands", () => {
    expect(nameOf({ entityType: RECYCLE_BIN_ENTITY_TYPE, name: '#jfbLibrary_recycleBin' }, localize)).toBe(
      '[jfbLibrary_recycleBin]',
    );
    expect(nameOf({ entityType: LIBRARY_ROOT_ENTITY_TYPE, name: '#jfbSection_forms' }, localize)).toBe(
      '[jfbSection_forms]',
    );
    expect(nameOf({ entityType: FORM_ENTITY_TYPE, name: '#actions_create' }, localize)).toBe('#actions_create');
    expect(nameOf({ entityType: FOLDER_ENTITY_TYPE, name: '#general_name' }, localize)).toBe('#general_name');
    expect(nameOf(undefined, localize)).toBe('');
  });
});

describe("a folders-only picker's page", () => {
  const folders = (items: LibraryItemResponseModel[]) => items.filter((entry) => entry.kind === 'folder');
  const page = (kinds: ('folder' | 'form')[], total: number) => ({
    items: kinds.map((kind, index) => item({ key: `${kind}${index}`, kind })),
    total,
  });

  it('ends where the forms start, which come after every folder', () => {
    expect(pageOf(page(['folder', 'folder', 'form'], 140), folders, 0)).toMatchObject({ total: 2 });
    expect(pageOf(page(['folder', 'form', 'form'], 140), folders, 100)).toMatchObject({ total: 101 });
  });

  it('keeps the total of a page of folders only, so that the next page comes', () => {
    expect(pageOf(page(['folder', 'folder'], 140), folders, 0)).toEqual({
      items: [item({ key: 'folder0', kind: 'folder' }), item({ key: 'folder1', kind: 'folder' })],
      total: 140,
      totalBefore: 0,
      totalAfter: 138,
    });
  });

  it('is the whole page for the tree itself', () => {
    expect(pageOf(page(['folder', 'form'], 7), (items) => items, 5)).toMatchObject({ total: 7 });
  });
});

describe('a tree item', () => {
  it('is a published form at the root, without marks', () => {
    expect(treeItemOf(item({}))).toEqual({
      unique: 'k',
      entityType: FORM_ENTITY_TYPE,
      parent: { unique: null, entityType: LIBRARY_ROOT_ENTITY_TYPE },
      name: 'Contact us',
      hasChildren: false,
      isFolder: false,
      icon: 'icon-document',
      isPublished: true,
      permissions: ['design'],
      flags: [],
    });
  });

  it('is a folder with children inside a folder, never muted', () => {
    expect(treeItemOf(item({ kind: 'folder', parentKey: 'p', hasChildren: true, isPublished: false }))).toMatchObject({
      entityType: FOLDER_ENTITY_TYPE,
      parent: { unique: 'p', entityType: FOLDER_ENTITY_TYPE },
      hasChildren: true,
      isFolder: true,
      icon: 'icon-folder',
      isPublished: true,
    });
  });

  it('says that a form has no published version', () => {
    expect(treeItemOf(item({ isPublished: false })).isPublished).toBe(false);
  });

  it('carries the restricted and the unseen entries marks as flags', () => {
    expect(treeItemOf(item({ isRestricted: true })).flags).toEqual([{ alias: RESTRICTED_FLAG }]);
    expect(treeItemOf(item({ hasUnseenEntries: true })).flags).toEqual([{ alias: UNSEEN_FLAG }]);
    expect(treeItemOf(item({ isRestricted: true, hasUnseenEntries: true })).flags).toEqual([
      { alias: RESTRICTED_FLAG },
      { alias: UNSEEN_FLAG },
    ]);
  });

  it('is a trashed form under the Recycle Bin when it went there alone', () => {
    expect(treeItemOf(item({ isTrashed: true }))).toMatchObject({
      entityType: TRASHED_FORM_ENTITY_TYPE,
      parent: { unique: null, entityType: RECYCLE_BIN_ENTITY_TYPE },
    });
  });

  it('is a trashed item under its trashed folder when it went with it', () => {
    expect(treeItemOf(item({ kind: 'folder', isTrashed: true, parentKey: 'p' }))).toMatchObject({
      entityType: TRASHED_FOLDER_ENTITY_TYPE,
      parent: { unique: 'p', entityType: TRASHED_FOLDER_ENTITY_TYPE },
    });
    expect(treeItemOf(item({ isTrashed: true, parentKey: 'p' })).parent).toEqual({
      unique: 'p',
      entityType: TRASHED_FOLDER_ENTITY_TYPE,
    });
  });
});

describe('a search result', () => {
  it("links to the form's workspace by its name alone at the top", () => {
    expect(resultOf(item({ alias: 'contact' }), inWords)).toEqual({
      unique: 'k',
      entityType: FORM_ENTITY_TYPE,
      name: 'Contact us',
      icon: 'icon-document',
      href: 'section/jfb-forms/workspace/jfb-form/edit/k',
    });
  });

  it('names the folder a form is in', () => {
    expect(resultOf(item({ folderName: 'Events' }), inWords).name).toBe('Contact us, in Events');
  });

  const field = (change: Partial<FieldMatchResponseModel>): FieldMatchResponseModel => ({
    formKey: 'f',
    formName: 'Contact us',
    fieldKey: 'e',
    alias: 'email',
    label: 'Email',
    fieldType: 'email',
    ...change,
  });

  it('names a field in its form and opens the form with the field selected', () => {
    expect(fieldResultOf(field({}), inWords)).toEqual({
      unique: 'e',
      entityType: FORM_ENTITY_TYPE,
      name: 'Email, in Contact us',
      icon: 'icon-autofill',
      href: 'section/jfb-forms/workspace/jfb-form/edit/f?field=e',
    });
  });

  it('names a field without a label by its alias', () => {
    expect(fieldResultOf(field({ label: '' }), inWords).name).toBe('email, in Contact us');
  });
});

describe("a form's path", () => {
  it('selects a field or an action, or nothing', () => {
    expect(formPath('f')).toBe('section/jfb-forms/workspace/jfb-form/edit/f');
    expect(formPath('f', { field: 'e' })).toBe('section/jfb-forms/workspace/jfb-form/edit/f?field=e');
    expect(formPath('f', { action: 'a' })).toBe('section/jfb-forms/workspace/jfb-form/edit/f?action=a');
  });
});

describe("a tree item's label", () => {
  it('is Umbraco\'s own label, which ends a long name with an ellipsis, so the row\'s "..." stays in the sidebar', () => {
    const element = new LibraryTreeItemElement();
    (element as unknown as { _item: unknown })._item = treeItemOf(
      item({ name: 'A form whose name runs far past the width of the sidebar', isPublished: false }),
    );
    const where = document.createElement('div');
    render(element.renderLabel(), where);
    const label = where.querySelector('span');
    expect([label?.id, label?.slot, label?.className]).toEqual(['label', 'label', 'unpublished']);
    expect(label?.textContent).toContain('A form whose name runs far past the width of the sidebar');
  });
});
