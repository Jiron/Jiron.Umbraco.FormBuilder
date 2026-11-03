// The form workspace's pure parts (ROADMAP 9.3a): the state chip, the field a link selects, what a save sends, the
// field types' traits, and the manifests that put the views and the footer's actions behind the open form's permissions.
import { describe, expect, it } from 'vitest';
import type { FieldTypeResponseModel, FormDraftResponseModel, FormResponseModel } from '../api';
import { FORM_PERMISSION_CONDITION, FORM_WORKSPACE_ALIAS } from './constants';
import { changesOf, detailOf, detailOfForm } from './form-detail.data-source';
import { holdsAny } from './form-permission.condition';
import { fieldOfQuery, formState } from './form-state';
import { traitsOf } from './form-workspace.context';
import { manifests } from './manifests';

const FORM: FormResponseModel = {
  key: 'f',
  alias: 'contact',
  name: 'Contact us',
  folderKey: null,
  concurrencyVersion: 3,
  isPublished: false,
  publicRevision: 0,
  isRestricted: false,
  trashedUtc: null,
  createdUtc: '2026-11-03T08:00:00Z',
  updatedUtc: '2026-11-03T08:00:00Z',
  permissions: ['design'],
};

const DEFINITION = { schemaVersion: 1, pages: [{ key: 'p', groups: [] }] };

const DRAFT: FormDraftResponseModel = {
  form: FORM,
  definition: DEFINITION,
  problems: [],
  draftVersion: 2,
  publishedVersion: null,
  hasUnpublishedChanges: true,
};

describe("the form's state", () => {
  it('is a draft until the first publish, then published, with pending changes, or unpublished', () => {
    expect(formState(FORM, true)).toBe('draft');
    expect(formState({ ...FORM, isPublished: true, publicRevision: 1 }, false)).toBe('published');
    expect(formState({ ...FORM, isPublished: true, publicRevision: 1 }, true)).toBe('pending');
    expect(formState({ ...FORM, isPublished: false, publicRevision: 4 }, false)).toBe('unpublished');
  });

  it('says what visitors get to who may not design, who cannot act on pending changes', () => {
    expect(formState({ ...FORM, isPublished: true, publicRevision: 1, permissions: ['viewEntries'] }, true)).toBe(
      'published',
    );
  });
});

describe("a link's field", () => {
  it('is the key `?field=` names, in lowercase, and nothing that is no key', () => {
    expect(fieldOfQuery('?field=3F2504E0-4F89-11D3-9A0C-0305E82C3301')).toBe('3f2504e0-4f89-11d3-9a0c-0305e82c3301');
    expect(fieldOfQuery('?action=3f2504e0-4f89-11d3-9a0c-0305e82c3301')).toBeNull();
    expect(fieldOfQuery('?field=email')).toBeNull();
    expect(fieldOfQuery('')).toBeNull();
  });
});

describe('a save', () => {
  const detail = detailOf(DRAFT);

  it("holds the draft endpoint's answer as the workspace reads it", () => {
    expect(detail).toEqual({
      entityType: 'jfb-form',
      unique: 'f',
      name: 'Contact us',
      form: FORM,
      definition: DEFINITION,
      problems: [],
      publishedVersion: null,
      hasUnpublishedChanges: true,
    });
  });

  it('holds the form alone for who may not read its draft, with no definition and no pending changes', () => {
    const viewer = { ...FORM, isPublished: true, publicRevision: 2, permissions: ['viewEntries'] };
    expect(detailOfForm(viewer)).toEqual({
      entityType: 'jfb-form',
      unique: 'f',
      name: 'Contact us',
      form: viewer,
      definition: { pages: [] },
      problems: [],
      publishedVersion: null,
      hasUnpublishedChanges: false,
    });
  });

  it('sends a new name and a changed definition, and nothing that did not change', () => {
    expect(changesOf(DRAFT, detail)).toEqual({});
    expect(changesOf(DRAFT, { ...detail, name: '  Contact us  ' })).toEqual({});
    expect(changesOf(DRAFT, { ...detail, name: 'Write to us ' })).toEqual({ name: 'Write to us' });
    const definition = { ...DEFINITION, pages: [{ key: 'p', groups: [{ key: 'g', fields: [] }] }] };
    expect(changesOf(DRAFT, { ...detail, definition })).toEqual({ definition });
  });

  it("reads the server's canonical draft with its empty lists, and sends nothing when only that differs", () => {
    const canonical: FormDraftResponseModel = { ...DRAFT, definition: { schemaVersion: 1, pages: [{ key: 'p' }] } };
    const read = detailOf(canonical);
    expect(read.definition).toEqual({ schemaVersion: 1, pages: [{ key: 'p', groups: [] }] });
    expect(changesOf(canonical, read)).toEqual({});
  });

  it('never sends an empty name', () => {
    expect(changesOf(DRAFT, { ...detail, name: '   ' })).toEqual({});
  });
});

describe("a field type's traits", () => {
  it('come from the field types endpoint: the kind rules compare, once per form, and child fields', () => {
    const type = { ruleKind: 'text', onePerForm: false, features: ['label', 'childFields'] } as FieldTypeResponseModel;
    expect(traitsOf(type)).toEqual({ kind: 'text', onePerForm: false, childFields: true });
    expect(traitsOf({ ...type, ruleKind: null, onePerForm: true, features: [] })).toEqual({
      kind: null,
      onePerForm: true,
      childFields: false,
    });
  });
});

describe('the form permission condition', () => {
  it('permits who holds one of the permissions on the form, and nobody before the form is read', () => {
    expect(holdsAny(['viewEntries', 'design'], ['design'])).toBe(true);
    expect(holdsAny(['viewEntries'], ['design', 'manageActions'])).toBe(false);
    expect(holdsAny(undefined, ['design'])).toBe(false);
    expect(holdsAny(['design'], [])).toBe(false);
  });
});

describe("the form workspace's manifests", () => {
  interface Conditioned {
    type: string;
    alias: string;
    conditions?: { alias: string; match?: string; anyOf?: string[] }[];
  }
  const all = manifests as unknown as Conditioned[];
  const holding = (manifest: Conditioned) =>
    manifest.conditions?.find((condition) => condition.alias === FORM_PERMISSION_CONDITION)?.anyOf;

  it('show the six views in their order, each in the form workspace for who may use it', () => {
    const views = all.filter((manifest) => manifest.type === 'workspaceView');
    expect(views.map((view) => view.alias.split('.').at(-1))).toEqual([
      'Design',
      'Settings',
      'Actions',
      'Entries',
      'Insights',
      'History',
    ]);
    expect(views.map(holding)).toEqual([
      ['design'],
      ['design'],
      ['design', 'manageActions'],
      ['viewEntries'],
      ['viewEntries'],
      ['design'],
    ]);
    for (const view of views)
      expect(view.conditions).toContainEqual({ alias: 'Umb.Condition.WorkspaceAlias', match: FORM_WORKSPACE_ALIAS });
  });

  it('put Save draft and Publish... in the footer for who may design the form', () => {
    const actions = all.filter((manifest) => manifest.type === 'workspaceAction');
    expect(actions.map((action) => action.alias.split('.').at(-1))).toEqual(['SaveDraft', 'Publish']);
    expect(actions.map(holding)).toEqual([['design'], ['design']]);
  });
});
