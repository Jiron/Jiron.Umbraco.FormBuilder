// The Form Builder view on Umbraco's user group screen (ROADMAP 25.1, ADR-0262): what the group may do with every form by
// the permissions' names, the folders and forms that give it more or are its own, that its users see an empty section
// while it holds nothing, that a group without the section adds it under Details first (following the screen's own
// Sections), the administrators' one sentence, the host's lock, one link to Settings > Access, a failed read with Try
// again, a refused read, and nothing asked for a group that is new.
import { BehaviorSubject } from '@umbraco-cms/backoffice/external/rxjs';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { UMB_USER_GROUP_WORKSPACE_CONTEXT } from '@umbraco-cms/backoffice/user-group';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { AccessResponseModel } from '../api';
import { screenPath } from '../section/entity-types';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

const api = vi.hoisted(() => ({ answers: [] as unknown[], reads: 0 }));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  accessGet: async () => {
    api.reads++;
    return api.answers.shift();
  },
}));

await import('./user-group-access.view');

const FORMS = 'Jiron.FormBuilder.Section';
const ADMINS = 'aaaaaaaa-0000-4000-8000-000000000001';
const HANDLERS = 'aaaaaaaa-0000-4000-8000-000000000002';
const HR = 'aaaaaaaa-0000-4000-8000-000000000003';
const NOTHING = 'aaaaaaaa-0000-4000-8000-000000000004';
const WRITERS = 'aaaaaaaa-0000-4000-8000-000000000005';

const group = (
  key: string,
  name: string,
  permissions: string[],
  more: { admin?: boolean; section?: boolean } = {},
) => ({
  key,
  name,
  icon: null,
  isAdministrators: more.admin ?? false,
  hasSection: more.section ?? true,
  permissions,
});

function access(more: Partial<AccessResponseModel> = {}): AccessResponseModel {
  return {
    groups: [
      group(ADMINS, 'Administrators', [], { admin: true }),
      group(HANDLERS, 'Entry handlers', ['viewEntries', 'editEntries', 'export']),
      group(HR, 'HR team', []),
      group(NOTHING, 'Forms without grants', []),
      group(WRITERS, 'Writers', ['viewEntries'], { section: false }),
    ],
    parts: [
      { alias: 'forms', permissions: ['design', 'manageActions'] },
      { alias: 'entries', permissions: ['viewEntries', 'viewSensitive', 'editEntries', 'export'] },
      { alias: 'library', permissions: ['manageLibrary', 'privacyTools'] },
    ],
    needs: [],
    lock: null,
    items: [
      {
        type: 'folder',
        key: 'hr',
        name: 'HR',
        isRestricted: true,
        grants: [{ group: HR, permissions: ['editEntries', 'viewEntries'] }],
      },
      {
        type: 'folder',
        key: 'events',
        name: 'Events',
        isRestricted: true,
        grants: [
          { group: HANDLERS, permissions: ['viewEntries'] },
          { group: HR, permissions: [] },
        ],
      },
      {
        type: 'form',
        key: 'quote',
        name: 'Quote request',
        isRestricted: false,
        grants: [{ group: HANDLERS, permissions: ['viewSensitive'] }],
      },
    ],
    ...more,
  };
}

/** The view on the screen of a group, with the sections its Details hold, the reads' answers given in turn. */
async function viewOf(unique: string, options: { sections?: string[]; isNew?: boolean; answers?: unknown[] } = {}) {
  api.answers = options.answers ?? [{ data: access() }];
  const host = testHost();
  const sections = new BehaviorSubject<string[]>(options.sections ?? [FORMS]);
  host.provideContext(UMB_USER_GROUP_WORKSPACE_CONTEXT, {
    unique: new BehaviorSubject<string | null>(unique).asObservable(),
    isNew: new BehaviorSubject<boolean>(options.isNew ?? false).asObservable(),
    sections: sections.asObservable(),
    getUnique: () => unique,
    getIsNew: () => options.isNew ?? false,
    getEntityType: () => 'user-group',
    getHostElement: () => host,
  } as never);
  const view = document.createElement('jfb-user-group-access-view');
  host.append(view);
  await settle(view);
  if (!options.isNew) await vi.waitFor(() => expect(shadow(view).querySelector('[aria-busy]')).toBeNull());
  await settle(view);
  return { view, root: shadow(view), sections };
}

const said = (root: ParentNode | null | undefined) => text(root as Element);

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  api.answers = [];
  api.reads = 0;
});

describe("the user group screen's Form Builder view", () => {
  it('lists what the group may do with every form, by part, each permission by its name in a list', async () => {
    const { root } = await viewOf(HANDLERS);
    const terms = [...root.querySelectorAll('dl > dt')].map((term) => text(term));
    expect(terms).toEqual(['Entries']);
    expect([...root.querySelectorAll('dl > dd li')].map((item) => text(item))).toEqual([
      'View entries',
      'Edit entries',
      'Export entries',
    ]);
    expect([...root.querySelectorAll('h2')].map((heading) => text(heading))).toEqual([
      'Permissions for every form',
      'Folders and forms with their own access',
    ]);
    // Said once: the tab names Form Builder, so no headline does, and no sentence says the group has no permission.
    expect(root.querySelector('uui-box')?.getAttribute('headline')).toBeNull();
    expect(said(root)).not.toContain('empty section');
    expect(said(root)).not.toContain("can't open Form Builder");
  });

  it('lists each folder and form that gives the group more, with what it may do there, and who else sees a restricted one', async () => {
    const { root } = await viewOf(HANDLERS);
    const items = [...root.querySelectorAll('.items li')].map((item) => ({
      name: text(item.querySelector('.item-name')),
      lines: [...item.querySelectorAll('.item-line')].map((line) => text(line)),
    }));
    expect(items).toEqual([
      { name: 'Events', lines: ['View entries', 'Only this group and HR team see it'] },
      { name: 'Quote request', lines: ['View sensitive data'] },
    ]);
  });

  it("shows the HR team's folder as its own, and says it holds nothing for every form without calling the section empty", async () => {
    const { root } = await viewOf(HR);
    expect(said(root.querySelector('section'))).toBe(
      'Permissions for every form None for every form, only in the folders and forms below.',
    );
    const items = [...root.querySelectorAll('.items li')].map((item) => text(item));
    expect(items).toEqual([
      'HR View entries and Edit entries Only this group sees it',
      'Events No permission here Only this group and Entry handlers see it',
    ]);
    expect(said(root)).not.toContain('empty section');
  });

  it('says a group without any permission leaves its users an empty section, with no list of folders', async () => {
    const { root } = await viewOf(NOTHING);
    expect(said(root.querySelector('section'))).toBe(
      'Permissions for every form This group has no permission for forms yet, so its users see an empty section.',
    );
    expect(root.querySelector('dl, .items')).toBeNull();
  });

  it("tells a group without the section to add it under Details first, follows the screen's Sections, and still lists its grants", async () => {
    const { view, root, sections } = await viewOf(WRITERS, { sections: ['Umb.Section.Content'] });
    const first = must(root.querySelector('.stack > p'), 'the first line');
    expect(text(first)).toBe("This group can't open Form Builder yet. Add the section to it under Details first.");
    expect([...root.querySelectorAll('dl > dd li')].map((item) => text(item))).toEqual(['View entries']);

    // Added under Details, not saved yet: the line goes, as the screen now shows it.
    sections.next(['Umb.Section.Content', FORMS]);
    await settle(view);
    expect(said(root)).not.toContain("can't open Form Builder");
    expect(api.reads).toBe(1);
  });

  it('says only that a group without the section and without permissions has none yet, not that its users see an empty section', async () => {
    const { view, root, sections } = await viewOf(NOTHING, { sections: ['Umb.Section.Content'] });
    expect([...root.querySelectorAll('.stack > p, .stack > section')].map((each) => text(each)).slice(0, 2)).toEqual([
      "This group can't open Form Builder yet. Add the section to it under Details first.",
      'Permissions for every form No permission for forms yet.',
    ]);
    expect(said(root)).not.toContain('empty section');

    // With the section added under Details, its users would see the section, empty.
    sections.next(['Umb.Section.Content', FORMS]);
    await settle(view);
    expect(said(root.querySelector('section'))).toBe(
      'Permissions for every form This group has no permission for forms yet, so its users see an empty section.',
    );
  });

  it('says the administrators hold everything, in one sentence, and lists nothing', async () => {
    const { root } = await viewOf(ADMINS);
    expect(said(root.querySelector('.stack > p'))).toBe(
      'Administrators hold every permission everywhere, because they could grant themselves any.',
    );
    expect(root.querySelector('h2, dl, ul')).toBeNull();
  });

  it('links to Settings > Access once, by a real link named for where it goes', async () => {
    for (const unique of [HANDLERS, ADMINS]) {
      const { root } = await viewOf(unique);
      const links = [...root.querySelectorAll('a')];
      expect(links).toHaveLength(1);
      expect(links[0]?.getAttribute('href')).toBe(screenPath('jfb-settings-access'));
      expect(text(links[0])).toBe('Open Form Builder \u203a Settings \u203a Access');
      document.body.replaceChildren();
    }
  });

  it("says when the host's configuration sets the grants, with its key", async () => {
    const lock = { field: 'grants', configurationKey: 'JironFormBuilder:Access:Grants' };
    const { root } = await viewOf(HANDLERS, { answers: [{ data: access({ lock }) }] });
    expect(said(root.querySelector('[role="note"]'))).toBe(
      "The site's configuration sets what each group may do with every form (JironFormBuilder:Access:Grants).",
    );
  });

  it('says a failed read, and reads again with Try again', async () => {
    const { view, root } = await viewOf(HANDLERS, { answers: [{ error: new Error('offline') }, { data: access() }] });
    expect(said(root.querySelector('[role="alert"]'))).toBe("Access couldn't be read.");
    must(
      [...root.querySelectorAll<HTMLElement>('uui-button')].find((one) => one.getAttribute('label') === 'Try again'),
      'Try again',
    ).click();
    await vi.waitFor(() => expect(root.querySelectorAll('dl > dd li')).toHaveLength(3));
    await settle(view);
    expect(root.querySelector('[role="alert"]')).toBeNull();
    expect(api.reads).toBe(2);
  });

  it('says which permission a refused read needs', async () => {
    const refused = new UmbApiError('refused', 403, null, {
      code: 'forbidden',
      permission: 'managePermissions',
    } as never);
    const { root } = await viewOf(HANDLERS, { answers: [{ error: refused }] });
    expect(said(root.querySelector('[role="alert"]'))).toBe(
      'This needs the "Manage permissions" permission. An administrator can grant it in Form Builder \u203a Settings \u203a Access.',
    );
  });

  it('asks nothing for a group that is new', async () => {
    const { root } = await viewOf('', { isNew: true });
    await settle();
    expect(api.reads).toBe(0);
    expect(root.querySelector('[aria-busy="true"]')).not.toBeNull();
  });
});
