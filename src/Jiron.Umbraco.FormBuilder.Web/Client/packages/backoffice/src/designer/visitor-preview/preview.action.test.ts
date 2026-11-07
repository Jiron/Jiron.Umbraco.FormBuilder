// "Preview" in the form workspace's footer (ROADMAP 10.3e, ADR-0203): what is not saved yet is saved first, and a save
// refused opens nothing; the preview opens at the designer's width and in its language, with the site's languages, and
// reads its document and its tickets through the workspace; one preview at a time for a workspace.
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { FORM_ENTITY_TYPE } from '../../section/entity-types';
import { must, registerEnglish, testHost } from '../../testing';
import { keepWidth } from '../canvas/widths';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import { PreviewFormAction } from './preview.action';
import type { PreviewModalData } from './preview.modal.token';

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

/** The workspace as the action reads it, with what it was asked. */
class Workspace {
  saved = 0;
  readonly asked: unknown[][] = [];
  constructor(
    readonly host: HTMLElement,
    readonly unsaved: boolean,
    readonly refuse = false,
    readonly language: string | null = null,
  ) {}
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
  async visitorPreviewOf(culture: string | null, dark: boolean) {
    this.asked.push(['read', culture, dark]);
    return { kind: 'failed' as const };
  }
  async previewTicketOf(version: number) {
    this.asked.push(['ticket', version]);
    return undefined;
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
    expect({ ...data, read: undefined, ticket: undefined }).toEqual({
      name: 'Contact us',
      form: FORM,
      languages: [
        { culture: 'en-US', name: 'English', isDefault: true },
        { culture: 'de-DE', name: 'German', isDefault: false },
      ],
      culture: 'de-DE',
      width: 'tablet',
      read: undefined,
      ticket: undefined,
    });
    // The document and the tickets come through the workspace.
    await data.read('en-US', true);
    await data.ticket(7);
    expect(context.asked).toEqual([
      ['read', 'en-US', true],
      ['ticket', 7],
    ]);
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
