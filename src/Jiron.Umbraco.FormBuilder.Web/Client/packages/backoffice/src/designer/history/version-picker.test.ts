// Restore an earlier version... (ROADMAP 10.3b, IA 5.6, ADR-0198): its sidebar lists the earlier versions newest first,
// each a button with its name, the live one's chip and when and by whom, never the current draft; Show draft saves adds
// the saves; choosing one asks for its comparison and closes the sidebar once it was restored there; a read that fails
// offers Try again, and a form without an earlier version says so. Its action opens it from the form's "..." once the
// History's words are loaded, and says so in the start words where they can't be; after Restore and publish..., the
// publish dialog opens once the sidebar is closed.
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import type { FormVersionEntryResponseModel, FormVersionResponseModel } from '../../api';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../../testing';
import { FORM_WORKSPACE_CONTEXT, VERSION_COMPARISON_MODAL_ALIAS, VERSION_PICKER_MODAL_ALIAS } from '../constants';
import { PUBLISH_MODAL_ALIAS } from '../publish.modal.token';
import type { VersionPage } from './versions';
import type { VersionPickerModalData } from './version-picker.modal.token';

/**
 * The dialogs opened by the action, and whether the History's words fail to load; `answer` plays a dialog, which
 * otherwise closes at once.
 */
const opened = vi.hoisted(() => ({
  modals: [] as { token: string; data: unknown }[],
  wordsFail: false,
  answer: undefined as ((token: string, data: unknown) => Promise<unknown>) | undefined,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, token: { toString(): string }, args?: { data?: unknown }) => {
    opened.modals.push({ token: token.toString(), data: args?.data });
    if (opened.answer) return opened.answer(token.toString(), args?.data);
    throw new Error('closed');
  },
}));

vi.mock('./history-words', async (original) => {
  const real = await original<typeof import('./history-words')>();
  return {
    ...real,
    loadHistoryWords: async (lang: string) => {
      if (opened.wordsFail) throw new Error('No words');
      return real.loadHistoryWords(lang);
    },
  };
});

const { VersionPickerModalElement } = await import('./version-picker.modal.element');
const { RestoreEarlierAction } = await import('./restore-earlier.action');
const { loadHistoryWords } = await import('./history-words');

const minutesAgo = (minutes: number) => new Date(Date.now() - minutes * 60_000).toISOString();

const entry = (number: number, more: Partial<FormVersionResponseModel> = {}): FormVersionEntryResponseModel => ({
  version: {
    number,
    kind: 'published',
    isPinned: false,
    createdUtc: minutesAgo(100 - number),
    publishedUtc: minutesAgo(100 - number),
    isDraft: false,
    isLive: false,
    ...more,
  },
  changes: [],
  by: 'Ana Novak',
});

const PUBLISHED = [entry(14, { isLive: true }), entry(12), entry(10)];
const EVERYTHING = [
  entry(16, { kind: 'draft', isDraft: true, publishedUtc: null }),
  entry(15, { kind: 'draft', publishedUtc: null }),
  ...PUBLISHED,
];

/** Umbraco's modal context as far as the sidebar uses it. */
function modalContext() {
  const state = new UmbObjectState<object>({});
  return {
    value: state.asObservable(),
    getValue: () => state.getValue(),
    setValue: (next: object) => state.setValue(next),
    updateValue: (partial: object) => state.update(partial as never),
    submit: vi.fn(),
    reject: vi.fn(),
  };
}

async function pickerOf(prepare: Partial<{ fails: number; restores: boolean; page: number }> = {}) {
  const reads: [boolean, number][] = [];
  let fails = prepare.fails ?? 0;
  const read = async (includeDrafts: boolean, skip: number): Promise<VersionPage | undefined> => {
    reads.push([includeDrafts, skip]);
    if (fails-- > 0) return undefined;
    const rows = includeDrafts ? EVERYTHING : PUBLISHED;
    const page = prepare.page ?? 20;
    return { total: rows.length, items: rows.slice(skip, skip + page) };
  };
  const choose = vi.fn(async () => prepare.restores ?? false);
  const element = new VersionPickerModalElement();
  const context = modalContext();
  element.modalContext = context as never;
  element.data = { read, choose } satisfies VersionPickerModalData;
  document.body.append(element);
  await vi.waitFor(() => expect(shadow(element).querySelector('ul, [role="alert"], [role="status"]')).not.toBeNull());
  await settle(element);
  const versions = () => all<HTMLElement>(element, '.version');
  return { element, root: shadow(element), context, reads, choose, versions };
}

beforeAll(async () => {
  registerEnglish();
  await loadHistoryWords('en');
});

beforeEach(() => {
  opened.modals = [];
  opened.wordsFail = false;
  opened.answer = undefined;
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the earlier versions sidebar', () => {
  it('lists the earlier versions newest first as buttons: name, the live chip, when and by whom', async () => {
    const { root, versions, reads } = await pickerOf();
    expect(root.querySelector('umb-body-layout')?.getAttribute('headline')).toBe('Restore an earlier version');
    expect(versions().map((one) => text(one.querySelector('.title')))).toEqual([
      'Version 14 Published',
      'Version 12',
      'Version 10',
    ]);
    expect(text(versions()[0]?.querySelector('.made'))).toMatch(/^Today, .+ \u00b7 Ana Novak$/);
    expect(reads).toEqual([[false, 0]]);
  });

  it('adds the draft saves, never the current draft, with Show draft saves', async () => {
    const { root, versions, element } = await pickerOf();
    const toggle = must(root.querySelector<HTMLInputElement>('uui-toggle'), 'Show draft saves');
    toggle.checked = true;
    toggle.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(versions()).toHaveLength(4));
    await settle(element);
    expect(versions().map((one) => one.dataset['number'])).toEqual(['15', '14', '12', '10']);
  });

  it('asks for the chosen version, and closes once it was restored there', async () => {
    const kept = await pickerOf();
    kept.versions()[1]?.click();
    await vi.waitFor(() => expect(kept.choose).toHaveBeenCalledOnce());
    expect((kept.choose.mock.calls[0] as unknown[])[0]).toEqual(PUBLISHED[1]);
    await settle(kept.element);
    expect(kept.context.submit).not.toHaveBeenCalled();
    document.body.replaceChildren();

    const restored = await pickerOf({ restores: true });
    restored.versions()[0]?.click();
    await vi.waitFor(() => expect(restored.context.submit).toHaveBeenCalledOnce());
  });

  it('shows more a page at a time, the focus on the first new version', async () => {
    const { root, versions, element } = await pickerOf({ page: 2 });
    expect(versions()).toHaveLength(2);
    must(root.querySelector<HTMLElement>('.more'), 'Show more').click();
    await vi.waitFor(() => expect(versions()).toHaveLength(3));
    await settle(element);
    expect(root.activeElement).toBe(versions()[2]);
  });

  it("offers Try again for a read that fails, and says when there's no earlier version", async () => {
    const { root, versions } = await pickerOf({ fails: 1 });
    expect(text(root.querySelector('[role="alert"] span'))).toBe("The versions couldn't be read.");
    must(root.querySelector<HTMLElement>('[role="alert"] uui-button'), 'Try again').click();
    await vi.waitFor(() => expect(versions()).toHaveLength(3));
    document.body.replaceChildren();

    const element = new VersionPickerModalElement();
    element.modalContext = modalContext() as never;
    element.data = {
      read: async () => ({ total: 1, items: [EVERYTHING[0] as FormVersionEntryResponseModel] }),
      choose: async () => false,
    };
    document.body.append(element);
    await vi.waitFor(() =>
      expect(text(shadow(element).querySelector('[role="status"]'))).toBe('There is no earlier version yet.'),
    );
  });
});

describe('Restore an earlier version... in the form\'s "..."', () => {
  async function run(wordsFail = false) {
    opened.wordsFail = wordsFail;
    const host = testHost();
    const peek = vi.fn();
    const workspace = {
      versionsOf: vi.fn(async () => undefined),
      getData: () => ({ draftVersion: 16, form: { name: 'Contact' } }),
      getEntityType: () => 'jfb-form',
      getUnique: () => 'form1',
      getHostElement: () => host,
      getHasUnpersistedChanges: () => false,
      restoreVersion: vi.fn(async () => ({ before: 16, after: 17, shown: true })),
      comparisonOf: async () => ({ kind: 'failed' }),
      getName: () => 'Contact',
      getFieldTypes: () => ({}),
      check: new UmbObjectState({}).asObservable(),
      checkNow: vi.fn(),
    };
    host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
    host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
    // The action sits in the workspace's header, below where the contexts are provided.
    const header = document.createElement('jfb-test-host');
    host.append(header);
    const action = new RestoreEarlierAction(
      header as never,
      { unique: 'form1', entityType: 'jfb-form', meta: {} } as never,
    );
    await action.execute();
    return { peek, workspace };
  }

  it("opens the earlier versions, which read the form's", async () => {
    const { workspace } = await run();
    expect(opened.modals.map((modal) => modal.token)).toEqual([VERSION_PICKER_MODAL_ALIAS]);
    await (opened.modals[0]?.data as VersionPickerModalData).read(true, 20);
    expect(workspace.versionsOf).toHaveBeenCalledWith(true, 20);
  });

  it('opens the publish dialog after Restore and publish..., once the earlier versions are closed', async () => {
    const steps: string[] = [];
    opened.answer = async (token, data) => {
      steps.push(`open ${token}`);
      if (token === VERSION_PICKER_MODAL_ALIAS) {
        const restored = await (data as VersionPickerModalData).choose(entry(12));
        steps.push(`close ${token}`);
        if (restored) return {};
      }
      if (token === VERSION_COMPARISON_MODAL_ALIAS) return { restore: 'publish' };
      throw new Error('closed');
    };
    const { workspace } = await run();
    expect(workspace.restoreVersion).toHaveBeenCalledWith('form1', 12);
    expect(steps).toEqual([
      `open ${VERSION_PICKER_MODAL_ALIAS}`,
      `open ${VERSION_COMPARISON_MODAL_ALIAS}`,
      `close ${VERSION_PICKER_MODAL_ALIAS}`,
      `open ${PUBLISH_MODAL_ALIAS}`,
    ]);
  });

  it("says in the start words when the History's can't be loaded, and opens nothing", async () => {
    const { peek } = await run(true);
    expect(opened.modals).toEqual([]);
    expect(peek).toHaveBeenCalledWith('danger', {
      data: { message: "The History couldn't be shown. Try again in a moment." },
    });
  });
});
