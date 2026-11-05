// "Publish..." in the form workspace's footer (ROADMAP 9.3a, 9.5f, 10.1a): a Fix it chosen in the dialog is handed on
// through the workspace to the view that makes it, which is then shown; a comment publishes, trimmed, none as null.
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import { testHost } from '../testing';
import type { FixTarget } from './check/form-check';
import { UNCHECKED } from './check/form-checker';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import { PublishFormAction } from './publish.action';

const answering = vi.hoisted(() => ({ value: undefined as unknown }));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: { closed?: () => void } }) => {
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

afterEach(() => {
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
});
