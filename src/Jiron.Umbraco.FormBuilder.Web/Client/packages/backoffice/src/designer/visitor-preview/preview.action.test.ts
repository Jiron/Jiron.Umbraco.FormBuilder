// "Preview" in the form workspace's footer (ROADMAP 10.3e and 10.3f, ADR-0203, ADR-0204): what is not saved yet is saved
// first, and a save refused opens nothing; the preview opens at the designer's width and in its language, with the
// site's languages, offering Run actions for test entries to who may manage the form's actions, and reads its document,
// its tickets and its test entries through the workspace; it names the actions Run actions... lists as the Actions view
// does; one preview at a time for a workspace.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { ActionTypeResponseModel } from '../../api';
import { FORM_ENTITY_TYPE } from '../../section/entity-types';
import { must, registerEnglish, testHost } from '../../testing';
import { keepWidth } from '../canvas/widths';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import { PreviewFormAction } from './preview.action';
import type { PreviewModalData } from './preview.modal.token';

/** The built-in action types, as the snapshot holds what the server serves. */
const ACTION_TYPES = JSON.parse(
  readFileSync(
    join(__dirname, '..', '..', '..', '..', '..', '..', '..', '..', 'docs', 'api', 'action-types.json'),
    'utf8',
  ),
) as ActionTypeResponseModel[];

const opened = vi.hoisted(() => ({
  data: [] as PreviewModalData[],
  wait: undefined as Promise<void> | undefined,
  cultures: [] as (string | undefined)[],
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, args?: { data?: PreviewModalData }) => {
    if (args?.data) opened.data.push(args.data);
    await opened.wait;
    return undefined;
  },
}));

vi.mock('@umbraco-cms/backoffice/resources', () => ({
  tryExecute: async (_host: unknown, request: Promise<unknown>) => ({ data: await request }),
}));

vi.mock('../../api', async (original) => ({
  ...(await original<typeof import('../../api')>()),
  actionTypeList: async () => ({ types: ACTION_TYPES }),
}));

vi.mock('../preview/look', () => ({
  readLook: (_host: unknown, culture?: string) => {
    opened.cultures.push(culture);
    return Promise.resolve({
      culture: culture ?? 'en-US',
      languages: [
        { culture: 'en-US', name: 'English', isDefault: true },
        { culture: 'de-DE', name: 'German', isDefault: false },
      ],
    });
  },
}));

const FORM = '3f2504e0-4f89-41d3-9a0c-0305e82c3301';

/** The draft as saved: a page, and two actions, one named, one not. */
const DEFINITION = {
  pages: [{ key: 'p1', groups: [] }],
  actions: [
    { key: 'a1', type: 'email', name: 'Tell the team', settings: { to: ['team@example.com'] } },
    { key: 'a2', type: 'email', settings: {} },
  ],
};

/** The workspace as the action reads it, with what it was asked. */
class Workspace {
  saved = 0;
  readonly asked: unknown[][] = [];
  constructor(
    readonly host: HTMLElement,
    readonly unsaved: boolean,
    readonly refuse = false,
    readonly language: string | null = null,
    readonly permissions: readonly string[] = ['design', 'manageActions'],
  ) {}
  getData() {
    return { form: { permissions: this.permissions } };
  }
  getEditorState() {
    return { definition: DEFINITION };
  }
  getHostElement() {
    return this.host;
  }
  getEntityType() {
    return FORM_ENTITY_TYPE;
  }
  getUnique() {
    return FORM;
  }
  getName() {
    return 'Contact us';
  }
  getLanguage() {
    return this.language;
  }
  getHasUnpersistedChanges() {
    return this.unsaved && this.saved === 0;
  }
  async requestSubmit() {
    this.saved++;
    if (this.refuse) throw new Error('Someone saved meanwhile.');
  }
  async visitorPreviewOf(culture: string | null, ask: unknown) {
    this.asked.push(['read', culture, ask]);
    return { kind: 'failed' as const };
  }
  async previewTicketOf(version: number, runActions: boolean) {
    this.asked.push(['ticket', version, runActions]);
    return undefined;
  }
  async testEntryOf(submissionId: string) {
    this.asked.push(['entry', submissionId]);
    return { kind: 'missing' as const };
  }
  async runTestEntryActions(submissionId: string) {
    this.asked.push(['run', submissionId]);
    return 'ran' as const;
  }
}

async function previewing(workspace: (host: HTMLElement) => Workspace) {
  const host = testHost();
  const context = workspace(host);
  host.provideContext(FORM_WORKSPACE_CONTEXT, context as never);
  const action = new PreviewFormAction(host, { meta: {} } as never);
  return { context, action, run: () => action.execute() };
}

beforeAll(registerEnglish);

afterEach(() => {
  opened.data = [];
  opened.cultures = [];
  opened.wait = undefined;
  localStorage.clear();
  document.body.replaceChildren();
});

describe('Preview', () => {
  it("saves what is not saved yet, then opens at the designer's width and in its language", async () => {
    keepWidth('tablet');
    const { context, run } = await previewing((host) => new Workspace(host, true, false, 'de-DE'));
    await run();
    expect(context.saved).toBe(1);
    expect(opened.cultures).toEqual(['de-DE']);
    const data = must(opened.data[0], 'the preview opened');
    const calls = {
      read: undefined,
      ticket: undefined,
      testEntry: undefined,
      runActions: undefined,
      describe: undefined,
    };
    expect({ ...data, ...calls }).toEqual({
      name: 'Contact us',
      form: FORM,
      languages: [
        { culture: 'en-US', name: 'English', isDefault: true },
        { culture: 'de-DE', name: 'German', isDefault: false },
      ],
      culture: 'de-DE',
      width: 'tablet',
      mayRunActions: true,
      ...calls,
    });
    // The document, the tickets and the test entries come through the workspace.
    const ask = { dark: true, sample: true, actions: false };
    await data.read('en-US', ask);
    await data.ticket(7, true);
    expect(await data.testEntry('s1')).toEqual({ kind: 'missing' });
    expect(await data.runActions('s1')).toBe('ran');
    expect(context.asked).toEqual([
      ['read', 'en-US', ask],
      ['ticket', 7, true],
      ['entry', 's1'],
      ['run', 's1'],
    ]);
  });

  it('names the actions as the Actions view does, one the draft no longer holds by its key alone', async () => {
    const { run } = await previewing((host) => new Workspace(host, false));
    await run();
    const data = must(opened.data[0], 'the preview opened');
    expect(await data.describe(['a2', 'a1', 'gone'])).toEqual([
      { name: 'Email', line: 'No recipient yet' },
      { name: 'Tell the team', line: 'To team@example.com' },
      { name: 'gone', line: '' },
    ]);
  });

  it("offers Run actions for test entries only to who may manage the form's actions", async () => {
    const { run } = await previewing((host) => new Workspace(host, false, false, null, ['design']));
    await run();
    expect(must(opened.data[0], 'the preview opened').mayRunActions).toBe(false);
  });

  it('saves nothing when all is saved, and opens nothing when the save is refused', async () => {
    const saved = await previewing((host) => new Workspace(host, false));
    await saved.run();
    expect([saved.context.saved, opened.data.length]).toEqual([0, 1]);

    opened.data = [];
    const refused = await previewing((host) => new Workspace(host, true, true));
    await refused.run();
    expect([refused.context.saved, opened.data.length]).toEqual([1, 0]);
  });

  it('opens one preview at a time for a workspace', async () => {
    let close: () => void = () => undefined;
    opened.wait = new Promise((done) => (close = done));
    const { run } = await previewing((host) => new Workspace(host, false));
    const first = run();
    await vi.waitFor(() => expect(opened.data).toHaveLength(1));
    await run();
    expect(opened.data).toHaveLength(1);
    close();
    await first;
    opened.wait = undefined;
    await run();
    expect(opened.data).toHaveLength(2);
  });
});
