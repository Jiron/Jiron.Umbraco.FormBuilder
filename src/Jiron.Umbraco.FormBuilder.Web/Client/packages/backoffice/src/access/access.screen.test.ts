// Settings > Access and the Permissions sidebar (ROADMAP 12.4a, IA 8, ADR-0218): the matrix lists every group with the
// administrators fixed and ticked; a box ticked brings what it needs, said to screen readers; Save sends the changed
// groups only; a change made meanwhile is said; the host's lock reads only; a group without the section is named with a
// link; each folder or form says who may see it; and the sidebar adds and takes off groups, asks before restricting
// with no group, and saves its grants and restriction together.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbApiError } from '@umbraco-cms/backoffice/resources';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { AccessResponseModel, AccessTargetResponseModel } from '../api';
import { must, registerEnglish, settle, shadow, testHost } from '../testing';

const api = vi.hoisted(() => ({
  /** What a sidebar the screen opens answers. */
  modal: undefined as { saved: boolean } | undefined,
  reads: [] as unknown[],
  saves: [] as unknown[],
  sent: [] as unknown[],
  confirm: true,
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  accessGet: async () => api.reads.shift(),
  accessSave: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.saves.shift();
  },
  accessGetFolder: async () => api.reads.shift(),
  accessSaveFolder: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.saves.shift();
  },
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async () => api.modal ?? Promise.reject(new Error('closed')),
}));

vi.mock('../ui/confirm', () => ({
  confirmText: async () => (api.confirm ? undefined : Promise.reject(new Error('cancelled'))),
}));

/** The router's event before the address changes; whether the screen stopped it to ask first. */
const stopsLeaving = (): boolean => {
  const event = new CustomEvent('willchangestate', { detail: { url: '/umbraco/section/content' }, cancelable: true });
  window.dispatchEvent(event);
  return event.defaultPrevented;
};

const { AccessScreenElement } = await import('./access.screen');
const { PermissionsModalElement } = await import('./permissions.modal.element');

const all = <E extends Element = HTMLElement>(parent: ParentNode, selector: string): E[] => [
  ...parent.querySelectorAll<E>(selector),
];
const said = (root: ParentNode): string => (root.textContent ?? '').replace(/\s+/g, ' ').trim();

const ADMINS = '00000000-0000-0000-0000-00000000000a';

function access(more: Partial<AccessResponseModel> = {}): AccessResponseModel {
  return {
    groups: [
      {
        key: ADMINS,
        name: 'Administrators',
        icon: 'icon-medal',
        isAdministrators: true,
        hasSection: true,
        permissions: ['design', 'viewEntries', 'editEntries'],
      },
      {
        key: 'editors',
        name: 'Editors',
        icon: null,
        isAdministrators: false,
        hasSection: true,
        permissions: ['design'],
      },
      {
        key: 'translators',
        name: 'Translators',
        icon: null,
        isAdministrators: false,
        hasSection: false,
        permissions: [],
      },
    ],
    parts: [
      { alias: 'forms', permissions: ['design'] },
      { alias: 'entries', permissions: ['viewEntries', 'editEntries'] },
    ],
    needs: [{ permission: 'editEntries', needs: 'viewEntries' }],
    lock: null,
    items: [
      {
        type: 'folder',
        key: 'hr',
        name: 'HR',
        isRestricted: true,
        grants: [{ group: 'editors', permissions: ['viewEntries'] }],
      },
      {
        type: 'form',
        key: 'quote',
        name: 'Quote request',
        isRestricted: false,
        grants: [{ group: 'translators', permissions: ['viewEntries'] }],
      },
    ],
    ...more,
  };
}

function folder(more: Partial<AccessTargetResponseModel> = {}): AccessTargetResponseModel {
  return {
    type: 'folder',
    key: 'events',
    name: 'Events',
    isRestricted: false,
    version: null,
    groups: [
      {
        key: 'editors',
        name: 'Editors',
        icon: null,
        isAdministrators: false,
        hasSection: true,
        permissions: ['viewEntries'],
      },
      {
        key: 'translators',
        name: 'Translators',
        icon: null,
        isAdministrators: false,
        hasSection: false,
        permissions: [],
      },
    ],
    permissions: ['design', 'viewEntries', 'editEntries'],
    needs: [{ permission: 'editEntries', needs: 'viewEntries' }],
    lock: null,
    ...more,
  };
}

async function open<T extends HTMLElement>(element: T, ...reads: unknown[]) {
  api.reads = reads.map((data) => ({ data }));
  api.saves = [];
  api.sent = [];
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  host.append(element);
  await vi.waitFor(() => expect(shadow(element).querySelector('table, .note')).not.toBeNull());
  await settle(element as unknown as { updateComplete: Promise<unknown> });
  const root = shadow(element);
  const box = (label: string) => must(root.querySelector<HTMLInputElement>(`input[aria-label="${label}"]`), label);
  const button = (label: string) => all(root, 'uui-button').find((one) => one.getAttribute('label') === label);
  return { root, box, button, peek };
}

beforeAll(registerEnglish);

// The screen's own address, which a leaving is told from.
beforeAll(() => history.replaceState({}, '', '/umbraco/section/jfb-forms/settings/access'));

afterEach(() => {
  document.body.replaceChildren();
  api.confirm = true;
});

describe('Settings > Access', () => {
  it('lists every group, the administrators ticked and fixed, in parts, with a box named by its permission and group', async () => {
    const { root, box } = await open(new AccessScreenElement(), access());
    expect(all(root, 'thead th').map((cell) => said(cell))).toEqual([
      'Permission',
      'Administrators',
      'Editors',
      'Translators',
    ]);
    expect(all(root, '.part th').map((cell) => said(cell))).toEqual(['Forms', 'Entries']);
    expect(box('Design forms, Administrators').checked).toBe(true);
    expect(box('Design forms, Administrators').disabled).toBe(true);
    expect(box('Design forms, Editors').checked).toBe(true);
    expect(said(root)).toContain('Administrators hold every permission everywhere');
    expect(root.querySelector('.matrix')?.getAttribute('tabindex')).toBe('0');
  });

  it('names a group without the section once, with a link to its user group', async () => {
    const { root } = await open(new AccessScreenElement(), access());
    expect(said(root)).toContain("A group can't open Form Builder yet.");
    const link = must(root.querySelector<HTMLAnchorElement>('.groups-without a'), 'link');
    expect(said(link)).toBe('Translators');
    expect(link.getAttribute('href')).toContain('translators');
  });

  it('ticks what a box needs with it, says so, and saves only the groups that changed', async () => {
    const { root, box, button, peek } = await open(new AccessScreenElement(), access());
    box('Edit entries, Editors').click();
    await settle();
    expect(box('View entries, Editors').checked).toBe(true);
    expect(said(must(root.querySelector('[role="status"]'), 'status'))).toBe(
      'View entries is on too: Edit entries needs it.',
    );
    api.saves = [{ data: access() }];
    must(button('Save'), 'Save').click();
    await vi.waitFor(() => expect(api.sent).toHaveLength(1));
    expect(api.sent).toEqual([
      { grants: [{ group: 'editors', expected: ['design'], permissions: ['design', 'viewEntries', 'editEntries'] }] },
    ]);
    await vi.waitFor(() => expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'Access is saved.' } }));
  });

  it('says a change made meanwhile, with the grants now saved', async () => {
    const { root, box, button } = await open(new AccessScreenElement(), access());
    box('View entries, Translators').click();
    api.saves = [{ error: new UmbApiError('changed', 409, null, { code: 'concurrency_conflict' } as never) }];
    api.reads = [{ data: access() }];
    must(button('Save'), 'Save').click();
    await vi.waitFor(() =>
      expect(said(must(root.querySelector('.alert'), 'alert'))).toContain('Someone else changed access meanwhile'),
    );
    expect(box('View entries, Translators').checked).toBe(false);
  });

  it("reads only, with no Save, while the host's configuration sets the grants", async () => {
    const { root, box, button } = await open(
      new AccessScreenElement(),
      access({ lock: { field: 'grants', configurationKey: 'JironFormBuilder:Access:Grants' } }),
    );
    expect(box('Design forms, Editors').disabled).toBe(true);
    expect(button('Save')).toBeUndefined();
    expect(said(root)).toContain('JironFormBuilder:Access:Grants');
    expect(button('Add folder or form\u2026')).toBeDefined();
  });

  it("keeps the ticks not saved yet when a folder's sidebar saved and the screen reads again, so Save sends them", async () => {
    const { root, box, button } = await open(new AccessScreenElement(), access());
    box('Edit entries, Editors').click();
    await settle();

    api.modal = { saved: true };
    api.reads = [{ data: access() }];
    must(root.querySelector<HTMLElement>('.items .item-name'), 'a folder').click();
    await vi.waitFor(() => expect(api.reads).toHaveLength(0));
    await settle();
    expect(box('Edit entries, Editors').checked, 'the tick stays').toBe(true);

    api.saves = [{ data: access() }];
    must(button('Save'), 'Save').click();
    await vi.waitFor(() => expect(api.sent).toHaveLength(1));
    expect(api.sent).toEqual([
      { grants: [{ group: 'editors', expected: ['design'], permissions: ['design', 'viewEntries', 'editEntries'] }] },
    ]);
    api.modal = undefined;
  });

  it('asks before the backoffice leaves it with ticks not saved, and not once they are saved', async () => {
    const { box, button } = await open(new AccessScreenElement(), access());
    expect(stopsLeaving(), 'nothing changed').toBe(false);
    box('Edit entries, Editors').click();
    await settle();
    expect(stopsLeaving(), 'a tick waits to be saved').toBe(true);

    api.saves = [
      {
        data: access({
          groups: access().groups.map((group) =>
            group.key === 'editors' ? { ...group, permissions: ['design', 'viewEntries', 'editEntries'] } : group,
          ),
        }),
      },
    ];
    must(button('Save'), 'Save').click();
    await vi.waitFor(() => expect(api.sent).toHaveLength(1));
    await settle();
    expect(stopsLeaving(), 'saved').toBe(false);
  });

  it('says of each folder or form who alone sees it, or who may also', async () => {
    const { root } = await open(new AccessScreenElement(), access());
    expect(all(root, '.items li').map((item) => said(item))).toEqual([
      'HR Only these groups: Editors',
      'Quote request Also: Translators',
    ]);
  });
});

describe("a folder's Permissions sidebar", () => {
  const sidebar = () => {
    const element = new PermissionsModalElement();
    element.data = { target: 'folder', key: 'events' };
    return element;
  };

  it('shows the groups with access here, and adds and takes off a group', async () => {
    const { root, box } = await open(sidebar(), folder());
    expect(all(root, 'thead th').map((cell) => said(cell))).toEqual(['Permission', 'Editors']);
    // The area's heading is heard, not seen: the switch's help says it, and Add user group's label follows (16.6's review).
    const heading = must(root.querySelector('h3'), 'the heading');
    expect([said(heading), heading.classList.contains('visually-hidden')]).toEqual(['Groups with access here', true]);
    const add = must(root.querySelector<HTMLSelectElement>('#add-group'), 'add');
    add.value = 'translators';
    add.dispatchEvent(new Event('change'));
    await settle();
    expect(box('Design forms, Translators').checked).toBe(false);
    all(root, 'thead uui-button')
      .find((one) => one.getAttribute('label') === 'Remove Editors')
      ?.click();
    await settle();
    expect(all(root, 'thead th').map((cell) => said(cell))).toEqual(['Permission', 'Translators']);
  });

  it('asks before restricting with no group, and saves the grants and the restriction together', async () => {
    const { root, button } = await open(
      sidebar(),
      folder({ groups: folder().groups.map((group) => ({ ...group, permissions: [] })) }),
    );
    expect(said(root)).toContain('None yet. Access given to every form reaches this folder.');
    must(root.querySelector<HTMLInputElement>('#restrict'), 'restrict').click();
    await settle();
    api.confirm = false;
    must(button('Save'), 'Save').click();
    await settle();
    expect(api.sent).toEqual([]);
    api.confirm = true;
    api.saves = [{ data: folder({ isRestricted: true }) }];
    must(button('Save'), 'Save').click();
    await vi.waitFor(() => expect(api.sent).toEqual([{ grants: [], isRestricted: true, version: null }]));
  });
});
