// A save someone else's save refused (ROADMAP 10.3c, IA 5.7, ADR-0200): the dialog says who saved and when, after the
// user opened the form, or that someone changed it; Cancel leaves everything as it is. Review both versions compares
// the user's draft with theirs and both with the draft they started from, so each difference starts on the side that
// made it; its Save takes the merged draft, which keeps work from both sides, and saves it. One review at a time; a
// form left meanwhile is left alone, and a draft changed while the versions were read is compared again.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { DefinitionChangeResponseModel, FormDraftResponseModel, FormResponseModel } from '../../api';
import { registerEnglish, testHost } from '../../testing';
import { REVIEW_MODAL_ALIAS } from '../constants';
import type { DesignDefinition } from '../definition';
import { builtInTypes } from '../preview/fixtures';
import type { ReviewModalData } from './review.modal.token';

/** What the API answers and what the dialogs answer. */
const fake = vi.hoisted(() => ({
  newest: undefined as unknown,
  /** A version newer than the draft, which isn't the draft: a publish. */
  published: undefined as unknown,
  theirs: undefined as unknown,
  compared: {} as Record<number, unknown>,
  /** The definitions compared with a version, in turn. */
  sent: [] as unknown[],
  between: undefined as unknown,
  confirmed: true,
  confirms: [] as { headline: string; content: unknown; confirmLabel?: string }[],
  reviews: [] as unknown[],
  chosen: undefined as unknown,
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  versionList: async () => ({
    data: { total: 2, items: [...(fake.published ? [fake.published] : []), ...(fake.newest ? [fake.newest] : [])] },
  }),
  formDraft: async () => ({ data: fake.theirs }),
  versionCompare: async () => ({ data: fake.between }),
  versionCompareWith: async (options: { path: { number: number }; body: { definition: unknown } }) => {
    fake.sent.push(options.body.definition);
    return { data: fake.compared[options.path.number] };
  },
  actionTypeList: async () => ({ data: { types: [] } }),
}));

vi.mock('../preview/look', async (original) => ({
  ...(await original<typeof import('../preview/look')>()),
  readLook: async () => undefined,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbConfirmModal: async (_host: unknown, data: { headline: string; content: { values: unknown[] } }) => {
    fake.confirms.push({ ...data, content: data.content.values.join('') });
    if (!fake.confirmed) throw new Error('cancelled');
  },
  umbOpenModal: async (_host: unknown, token: { toString(): string }, args: { data: unknown }) => {
    if (token.toString() !== REVIEW_MODAL_ALIAS) throw new Error('unexpected');
    fake.reviews.push(args.data);
    if (!fake.chosen) throw new Error('cancelled');
    return fake.chosen;
  },
}));

const { sayConflict } = await import('./conflict-flow');

const FORM: FormResponseModel = {
  key: 'f',
  alias: 'contact',
  name: 'Contact',
  folderKey: null,
  concurrencyVersion: 7,
  isPublished: false,
  publicRevision: 0,
  isRestricted: false,
  trashedUtc: null,
  createdUtc: '2026-11-03T08:00:00Z',
  updatedUtc: '2026-11-06T14:02:00Z',
  permissions: ['design'],
};

const field = (key: string, label: string) => ({ key, type: 'text', alias: key, label: { '': label } });
const formOf = (...fields: ReturnType<typeof field>[]): DesignDefinition => ({
  pages: [{ key: 'p1', groups: [{ key: 'g1', fields }] }],
});

/** Theirs added Company; yours renamed Name to Full name; both started from version 4. */
const THEIRS = formOf(field('f1', 'Name'), field('f2', 'Email'), field('f8', 'Company'));
const YOURS = formOf(field('f1', 'Full name'), field('f2', 'Email'));
const change = (more: Partial<DefinitionChangeResponseModel>): DefinitionChangeResponseModel => ({
  kind: 'field',
  change: 'changed',
  moved: false,
  properties: [],
  ...more,
});

/** The workspace as far as the flow uses it, with the save the review's Save asks for. */
function workspaceOf() {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const data = { name: 'Contact', definition: YOURS };
  const workspace = Object.assign(document.createElement('jfb-test-host'), {
    getUnique: (): string => 'f',
    getBase: () => ({ draftVersion: 4, name: 'Contact' }),
    // The same draft until it changes, as the workspace holds it.
    getData: () => data,
    getFieldTypes: () => builtInTypes,
    takeReview: vi.fn(),
    requestSubmit: vi.fn(async () => undefined),
  });
  host.append(workspace);
  return { workspace, peek };
}

beforeAll(registerEnglish);

beforeEach(() => {
  const today = new Date();
  today.setHours(14, 2, 0, 0);
  fake.newest = {
    version: {
      number: 5,
      kind: 'draft',
      isPinned: false,
      createdUtc: today.toISOString(),
      isDraft: true,
      isLive: false,
    },
    changes: null,
    by: 'Ana Novak',
  };
  fake.theirs = {
    form: FORM,
    definition: THEIRS,
    problems: [],
    draftVersion: 5,
    publishedVersion: null,
    hasUnpublishedChanges: true,
    hasEntries: false,
  } satisfies FormDraftResponseModel;
  fake.compared = {
    // Theirs to yours: Name relabelled, Company not there.
    5: {
      fromVersion: 5,
      changes: [
        change({ key: 'f1', label: 'Full name', properties: [{ path: ['label', ''], from: 'Name', to: 'Full name' }] }),
        change({ key: 'f8', parentKey: 'g1', change: 'removed', label: 'Company' }),
      ],
      definition: YOURS,
    },
    // The draft both started from to yours: Name relabelled.
    4: {
      fromVersion: 4,
      changes: [change({ key: 'f1', properties: [{ path: ['label', ''], from: 'Name', to: 'Full name' }] })],
      definition: YOURS,
    },
  };
  fake.between = { fromVersion: 4, toVersion: 5, changes: [change({ key: 'f8', change: 'added' })] };
  fake.published = undefined;
  fake.confirmed = true;
  fake.sent = [];
  fake.confirms = [];
  fake.reviews = [];
  fake.chosen = undefined;
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('a save someone else saved before', () => {
  it('says who saved and when, after the user opened the form, and Cancel leaves everything', async () => {
    fake.confirmed = false;
    const { workspace } = workspaceOf();
    await sayConflict(workspace as never);
    expect(fake.confirms).toEqual([
      {
        headline: "Your draft wasn't saved",
        content: expect.stringMatching(
          /^Ana Novak saved this form at (14:02|2:02.PM), after you opened it\. Review both versions before you save\. Your changes are still here\.$/,
        ) as unknown,
        confirmLabel: 'Review both versions',
        color: 'positive',
      },
    ]);
    expect(fake.reviews).toEqual([]);
    expect(workspace.takeReview).not.toHaveBeenCalled();
  });

  it('names who saved the draft, not who published since', async () => {
    fake.confirmed = false;
    fake.published = {
      version: {
        number: 6,
        kind: 'published',
        isPinned: false,
        createdUtc: '2026-11-06T15:00:00Z',
        isDraft: false,
        isLive: true,
      },
      changes: null,
      by: 'Bo Berg',
    };
    const { workspace } = workspaceOf();
    await sayConflict(workspace as never);
    expect(fake.confirms[0]?.content).toMatch(/^Ana Novak saved this form at /);
  });

  it('says someone changed it, where the History names no newer version', async () => {
    fake.confirmed = false;
    fake.newest = {
      ...(fake.newest as object),
      version: { ...(fake.newest as { version: object }).version, number: 4 },
    };
    const { workspace } = workspaceOf();
    await sayConflict(workspace as never);
    expect(fake.confirms[0]?.content).toBe(
      'Someone changed this form after you opened it. Review both versions before you save. Your changes are still here.',
    );
  });

  it("reviews both: each difference on the side that made it, and Save saves a draft with both sides' work", async () => {
    fake.chosen = { fromYours: ['field:f1'] };
    const { workspace } = workspaceOf();
    await sayConflict(workspace as never);

    const review = fake.reviews[0] as ReviewModalData;
    expect(review.theirs).toBe("Ana Novak's version");
    expect(review.differences.map((one) => [one.id, one.title, one.start])).toEqual([
      ['field:f1', 'Field "Full name"', 'yours'],
      ['field:f8', 'Field "Company"', 'theirs'],
    ]);
    expect(review.differences[0]?.rows).toEqual([{ what: 'Label', older: 'Name', newer: 'Full name' }]);

    expect(workspace.takeReview).toHaveBeenCalledOnce();
    const [theirs, definition, name] = workspace.takeReview.mock.calls[0] as [
      FormDraftResponseModel,
      DesignDefinition,
      string,
    ];
    expect(theirs.form.concurrencyVersion).toBe(7);
    expect(definition.pages[0]?.groups[0]?.fields.map((one) => one.label?.[''])).toEqual([
      'Full name',
      'Email',
      'Company',
    ]);
    expect(name).toBe('Contact');
    expect(workspace.requestSubmit).toHaveBeenCalledOnce();
  });

  it('offers the form name as a difference, and every difference waits where the starting draft is gone', async () => {
    fake.chosen = { fromYours: ['name'] };
    fake.compared = { 5: fake.compared[5] };
    const { workspace } = workspaceOf();
    const renamed = { name: 'Get in touch', definition: YOURS };
    workspace.getData = () => renamed;
    await sayConflict(workspace as never);
    const review = fake.reviews[0] as ReviewModalData;
    expect(review.differences.map((one) => [one.id, one.start])).toEqual([
      ['name', 'yours'],
      ['field:f1', undefined],
      ['field:f8', undefined],
    ]);
    expect(review.differences[0]).toMatchObject({
      title: 'The form',
      rows: [{ what: 'Name', older: 'Contact', newer: 'Get in touch' }],
    });
    expect(workspace.takeReview.mock.calls[0]?.[2]).toBe('Get in touch');
  });

  it('knows who made each where only the name moved: the draft both started from is theirs', async () => {
    fake.chosen = { fromYours: [] };
    fake.theirs = { ...(fake.theirs as object), form: { ...FORM, name: 'Contact us' }, draftVersion: 4 };
    // Nothing compares a version with itself.
    fake.between = undefined;
    const { workspace } = workspaceOf();
    const renamed = { name: 'Get in touch', definition: YOURS };
    workspace.getData = () => renamed;
    await sayConflict(workspace as never);
    const review = fake.reviews[0] as ReviewModalData;
    expect(review.differences.map((one) => [one.id, one.start, one.both])).toEqual([
      ['name', undefined, true],
      ['field:f1', 'yours', undefined],
    ]);
  });

  it('says one conflict at a time, and says again a save of the review someone else refused', async () => {
    fake.chosen = { fromYours: [] };
    const { workspace } = workspaceOf();
    let refusals = 1;
    workspace.requestSubmit = vi.fn(async () => {
      if (refusals-- > 0) await sayConflict(workspace as never);
    });
    await Promise.all([sayConflict(workspace as never), sayConflict(workspace as never)]);
    // The second save's conflict waited for the first; the review's own save was refused once more, and said.
    expect(fake.confirms).toHaveLength(2);
    expect(workspace.requestSubmit).toHaveBeenCalledTimes(2);
  });

  it('leaves a form left meanwhile alone, and compares a draft changed meanwhile again', async () => {
    fake.chosen = { fromYours: ['field:f1'] };
    const left = workspaceOf().workspace;
    let asked = 0;
    left.getUnique = () => (asked++ === 0 ? 'f' : 'other');
    await sayConflict(left as never);
    expect(fake.reviews).toEqual([]);
    expect(left.takeReview).not.toHaveBeenCalled();

    const { workspace } = workspaceOf();
    const before = { name: 'Contact', definition: YOURS };
    const after = { name: 'Contact', definition: formOf(field('f1', 'Your full name'), field('f2', 'Email')) };
    let reads = 0;
    workspace.getData = () => (reads++ === 0 ? before : after);
    await sayConflict(workspace as never);
    expect(fake.reviews).toHaveLength(1);
    expect(fake.sent.at(-1)).toEqual(after.definition);
    expect(workspace.takeReview).toHaveBeenCalledOnce();
  });

  it('compares a draft that keeps changing a few times, then says so, and opens nothing', async () => {
    fake.chosen = { fromYours: [] };
    const { workspace, peek } = workspaceOf();
    workspace.getData = () => ({ name: 'Contact', definition: YOURS });
    await sayConflict(workspace as never);
    expect(fake.reviews).toEqual([]);
    // Each compare asks for theirs against yours and the start against yours.
    expect(fake.sent).toHaveLength(6);
    expect(peek).toHaveBeenCalledOnce();
    expect(workspace.takeReview).not.toHaveBeenCalled();
  });

  it("says so where the versions can't be read, and changes nothing", async () => {
    fake.theirs = undefined;
    const { workspace, peek } = workspaceOf();
    await sayConflict(workspace as never);
    expect(peek).toHaveBeenCalledWith('danger', {
      data: { message: "Both versions couldn't be read. Your changes are still here; try again in a moment." },
    });
    expect(workspace.takeReview).not.toHaveBeenCalled();
  });
});
