// "Publish..." in the form workspace's footer (ROADMAP 9.3a, 9.5f, 10.1a, 10.3d): a Fix it chosen in the dialog is
// handed on through the workspace to the view that makes it, which is then shown; a comment publishes, trimmed, none
// as null; the dialog is told what changed, with what it says meanwhile, or nothing of it without History's words; and
// a second Publish... while one is open opens no second dialog.
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import { testHost } from '../testing';
import type { FixTarget } from './check/form-check';
import { UNCHECKED } from './check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { PublishFormAction } from './publish.action';

const answering = vi.hoisted(() => ({
  value: undefined as unknown,
  data: [] as { changes?: { said: Promise<unknown>; comparing: string } }[],
  wait: undefined as Promise<void> | undefined,
  wordsFail: false,
  changesFail: false,
}));

vi.mock('./history/publish-changes', async (original) => {
  const real = await original<typeof import('./history/publish-changes')>();
  return {
    ...real,
    changesSinceLive: (...args: Parameters<typeof real.changesSinceLive>) =>
      answering.changesFail ? Promise.reject(new Error('not loaded')) : real.changesSinceLive(...args),
  };
});

vi.mock('./history/history-words', async (original) => {
  const real = await original<typeof import('./history/history-words')>();
  return {
    loadHistoryWords: (lang: string) =>
      answering.wordsFail ? Promise.reject(new Error('offline')) : real.loadHistoryWords(lang),
  };
});

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: { closed?: () => void } }) => {
    answering.data.push(args?.data as never);
    await answering.wait;
    // The dialog answers, then is gone.
    const value = answering.value;
    args?.data?.closed?.();
    return value;
  },
}));

const FORM = '/umbraco/section/jfb-forms/workspace/jfb-form/edit/3f2504e0-4f89-41d3-9a0c-0305e82c3301';

/** The workspace as the action reads it, its methods on its prototype as the real one's are, so each needs its `this`. */
class Workspace {
  readonly fixes: FixTarget[] = [];
  readonly published: (string | null)[] = [];
  constructor(readonly host: HTMLElement) {}
  getHostElement() {
    return this.host;
  }
  readonly check = new UmbBasicState(UNCHECKED).asObservable();
  getEntityType() {
    return FORM_ENTITY_TYPE;
  }
  getData() {
    return { form: { name: 'Contact' } };
  }
  getName() {
    return 'Contact';
  }
  getFieldTypes() {
    return {};
  }
  checked = 0;
  async checkNow() {
    this.checked++;
  }
  requestFix(target: FixTarget) {
    this.fixes.push(target);
  }
  async publish(comment: string | null) {
    this.published.push(comment);
    return true;
  }
}

async function publishing(answer: unknown) {
  answering.value = answer;
  const host = testHost();
  const workspace = new Workspace(host);
  host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
  const action = new PublishFormAction(host, { meta: {} } as never);
  await action.execute();
  return workspace;
}

// The comparison's code and History's words load with the dialog; loaded once here, a test waits for neither.
beforeAll(async () => {
  await import('./history/publish-changes');
  await (await import('./history/history-words')).loadHistoryWords('en');
}, 60_000);

afterEach(() => {
  answering.data = [];
  answering.wait = undefined;
  answering.wordsFail = false;
  answering.changesFail = false;
  history.replaceState(null, '', '/');
  document.body.replaceChildren();
  vi.restoreAllMocks();
});

describe('Publish...', () => {
  it('hands a Fix it on through the workspace and shows the view that makes it', async () => {
    vi.spyOn(window, 'dispatchEvent').mockImplementation(() => true);
    history.replaceState(null, '', `${FORM}/view/design`);
    const settings: FixTarget = { kind: 'view', view: 'settings', path: '$.settings.appearance.theme' };
    expect((await publishing({ fix: settings })).fixes).toEqual([settings]);
    expect(window.location.pathname).toBe(`${FORM}/view/settings`);

    const element: FixTarget = { kind: 'palette', page: null };
    const workspace = await publishing({ fix: element });
    expect([workspace.fixes, workspace.published]).toEqual([[element], []]);
    expect(window.location.pathname).toBe(`${FORM}/view/design`);
  });

  it('publishes with the comment trimmed, an empty one as none', async () => {
    const workspace = await publishing({ comment: '  The new address  ' });
    // The draft is checked as the dialog opens, which shows the answer once it comes.
    expect([workspace.checked, workspace.published]).toEqual([1, ['The new address']]);
    expect((await publishing({ comment: '   ' })).published).toEqual([null]);
    expect((await publishing(undefined)).published).toEqual([]);
  });

  it('tells the dialog what changed, and what to say meanwhile, or nothing of it without the words', async () => {
    await publishing(undefined);
    const told = answering.data[0]?.changes;
    expect(told?.comparing).toBe('Comparing with the published version\u2026');
    // This workspace can't be compared: the dialog is told so, never left waiting.
    await expect(told?.said).resolves.toMatchObject({ parts: [] });

    // What fails on its way, the comparison's code among it, is said, never waited for.
    answering.changesFail = true;
    await publishing(undefined);
    await expect(answering.data[1]?.changes?.said).resolves.toEqual({
      headline: null,
      parts: [],
      said: "What changed couldn't be read. Publishing works all the same.",
    });

    answering.data = [];
    answering.wordsFail = true;
    await publishing(undefined);
    expect(answering.data[0]).toBeDefined();
    expect(answering.data[0]?.changes).toBeUndefined();
  });

  it('opens one dialog at a time for a workspace', async () => {
    let close: () => void = () => undefined;
    answering.wait = new Promise((done) => (close = done));
    answering.value = undefined;
    const host = testHost();
    const workspace = new Workspace(host);
    host.provideContext(FORM_WORKSPACE_CONTEXT, workspace as never);
    const action = new PublishFormAction(host, { meta: {} } as never);
    const first = action.execute();
    await vi.waitFor(() => expect(answering.data).toHaveLength(1));
    await action.execute();
    expect(answering.data).toHaveLength(1);
    close();
    await first;
    answering.wait = undefined;
    await action.execute();
    expect(answering.data).toHaveLength(2);
  });
});
