// Library > Templates (ROADMAP 12.1, IA 7, ADR-0217): the built-in templates, the saved forms and the saved groups, each
// part under its heading, as one radio group; the chosen one's preview with Use template, for who may design forms at the
// top of the forms, which creates the form there, lists it in the tree and opens it; a group's preview says where it is
// used and has no Use template; a saved template's "..." renames it, the focus back on the "...", and deletes it after
// asking, the next card of its part chosen and focused; a built-in one has none.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type { TemplateResponseModel } from '../api';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';

const api = vi.hoisted(() => ({
  created: [] as unknown[],
  renamed: [] as unknown[],
  deleted: [] as string[],
  modal: undefined as unknown,
  confirmed: true,
  saved: [] as unknown[],
  global: ['design', 'manageLibrary'] as string[],
  reloaded: [] as unknown[],
}));

vi.mock('../section/my-permissions', () => ({
  myPermissions: async () => ({ global: api.global }),
}));

vi.mock('../library/library-changes', async (original) => ({
  ...(await original<typeof import('../library/library-changes')>()),
  changed: async (_host: unknown, ...places: unknown[]) => {
    api.reloaded.push(...places);
  },
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async (_host: unknown, _token: unknown, options: { data: unknown }) => {
    api.modal = options.data;
    return { name: 'Enquiries', description: null };
  },
  umbConfirmModal: async () => (api.confirmed ? undefined : Promise.reject(new Error('cancelled'))),
}));

const template = (key: string, more: Partial<TemplateResponseModel> = {}): TemplateResponseModel => ({
  key,
  builtIn: false,
  alias: null,
  name: key,
  description: null,
  holds: { pages: 1, fields: 2, rules: 0, outcomes: 1, actions: 0 },
  definition: { schemaVersion: 1, key, pages: [] } as unknown as TemplateResponseModel['definition'],
  kind: 'form',
  ...more,
});

const CONTACT = template('66c2bf3e-fe43-41cc-8470-f446c2f01a89', { builtIn: true, alias: 'contact', name: 'Contact' });
const SAVED = template('00000000-0000-4000-8000-000000000001', { name: 'Enquiry', description: 'For enquiries' });
const QUOTE = template('00000000-0000-4000-8000-000000000003', { name: 'Quote' });
const GROUP = template('00000000-0000-4000-8000-000000000002', { name: 'Address block', kind: 'group' });

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  // The preview's look: none, so the preview draws without the site's styles.
  canvasGet: async () => ({ data: undefined }),
  fieldTypeList: async () => ({ data: { types: [], paletteGroups: [] } }),
  templateList: async (options: { query: { kind?: string } }) => {
    const items = (options.query.kind === 'group' ? [GROUP] : [CONTACT, SAVED, QUOTE]).filter(
      (one) => !api.deleted.includes(one.key),
    );
    return { data: { total: items.length, items } };
  },
  formExchangeFromTemplate: async (options: { body: unknown }) => {
    api.created.push(options.body);
    return { data: { key: '00000000-0000-4000-8000-0000000000f1' } };
  },
  templateRename: async (options: { path: { key: string }; body: unknown }) => {
    api.renamed.push({ key: options.path.key, ...(options.body as object) });
    return { data: { ...SAVED, name: 'Enquiries' } };
  },
  templateDelete: async (options: { path: { key: string } }) => {
    api.deleted.push(options.path.key);
    return { data: undefined };
  },
}));

const { TemplatesScreenElement } = await import('./templates.screen');

async function screenOf() {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  const screen = new TemplatesScreenElement();
  host.append(screen);
  await vi.waitFor(() => expect(shadow(screen).querySelector('.page')).not.toBeNull());
  await settle(screen);
  return { screen, root: shadow(screen), peek };
}

const choose = async (root: ShadowRoot, screen: { updateComplete: Promise<unknown> }, name: string) => {
  const card = must(
    all<HTMLLabelElement>(root.host, '.card').find((one) => text(one.querySelector('.name')) === name),
    name,
  );
  const radio = must(card.querySelector('input'), `${name}'s radio`);
  radio.checked = true;
  radio.dispatchEvent(new Event('change'));
  await settle(screen);
};

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  api.created = [];
  api.renamed = [];
  api.deleted = [];
  api.confirmed = true;
  api.global = ['design', 'manageLibrary'];
  api.reloaded = [];
  vi.restoreAllMocks();
});

describe('Library > Templates', () => {
  it('shows the built-in templates, the saved forms and the saved groups, each part under its heading, the first chosen', async () => {
    const { root } = await screenOf();
    expect(all(root.host, 'fieldset h3').map((heading) => text(heading))).toEqual([
      'Built in',
      'Saved forms',
      'Saved groups',
    ]);
    expect(all(root.host, '.card .name').map((name) => text(name))).toEqual([
      'Contact',
      'Enquiry',
      'Quote',
      'Address block',
    ]);
    expect(all(root.host, '.card .line').map((line) => text(line))).toEqual([
      'Name, email address and a message, then a thank-you message.',
      'For enquiries',
      '',
      '',
    ]);
    expect(all<HTMLInputElement>(root.host, '.card input').map((radio) => radio.checked)).toEqual([
      true,
      false,
      false,
      false,
    ]);
    expect(text(root.querySelector('#preview-heading'))).toBe('Contact');
    expect(root.querySelector('.preview uui-symbol-more')).toBeNull();
  });

  it('creates a form from the chosen template at the top of the forms, lists it in the tree and opens it', async () => {
    const pushed = vi.spyOn(history, 'pushState').mockImplementation(() => undefined);
    const { root } = await screenOf();
    must(root.querySelector<HTMLElement>('.preview uui-button[label="Use template"]'), 'Use template').click();
    await vi.waitFor(() => expect(pushed).toHaveBeenCalledOnce());
    expect(api.created).toEqual([{ templateKey: CONTACT.key, name: 'Contact', folderKey: null }]);
    expect(api.reloaded, 'the top of the forms reads its children again').toEqual([
      { entityType: 'jfb-library-root', unique: null },
    ]);
    expect(String(pushed.mock.calls[0]?.[2])).toMatch(
      /\/workspace\/jfb-form\/edit\/00000000-0000-4000-8000-0000000000f1$/,
    );
  });

  it('offers Use template only to who may design forms at the top of the forms', async () => {
    api.global = ['manageLibrary'];
    const { root } = await screenOf();
    expect(text(root.querySelector('#preview-heading'))).toBe('Contact');
    expect(root.querySelector('.preview uui-button[label="Use template"]')).toBeNull();
  });

  it("says where a group is used, without Use template, and renames and deletes a saved one from its '...'", async () => {
    const { root, screen, peek } = await screenOf();
    await choose(root, screen, 'Address block');
    expect(text(root.querySelector('.preview .muted'))).toBe(
      "A group: the designer's palette inserts it into any form, under Saved groups.",
    );
    expect(root.querySelector('.preview uui-button[label="Use template"]')).toBeNull();
    await choose(root, screen, 'Enquiry');
    const menu = all<HTMLElement>(root.host, 'uui-menu-item');
    expect(menu.map((item) => item.getAttribute('label'))).toEqual(['Rename\u2026', 'Delete\u2026']);
    menu[0]?.dispatchEvent(new CustomEvent('click-label'));
    await vi.waitFor(() => expect(api.renamed).toHaveLength(1));
    expect(api.modal).toMatchObject({ headline: 'Rename Enquiry', name: 'Enquiry', description: 'For enquiries' });
    expect(api.renamed).toEqual([{ key: SAVED.key, name: 'Enquiries', description: null }]);
    await vi.waitFor(() =>
      expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'The template is now Enquiries.' } }),
    );
    await vi.waitFor(() =>
      expect(root.activeElement, 'the focus back on the "..."').toBe(
        root.querySelector('.head uui-button[popovertarget]'),
      ),
    );
    api.confirmed = false;
    must(all<HTMLElement>(root.host, 'uui-menu-item')[1], 'Delete\u2026').dispatchEvent(new CustomEvent('click-label'));
    await settle(screen);
    expect(api.deleted).toEqual([]);
    api.confirmed = true;
    must(all<HTMLElement>(root.host, 'uui-menu-item')[1], 'Delete\u2026').dispatchEvent(new CustomEvent('click-label'));
    await vi.waitFor(() => expect(api.deleted).toEqual([SAVED.key]));
    // The next card of its part takes its place, chosen and focused.
    await vi.waitFor(() => expect(text(root.querySelector('#preview-heading'))).toBe('Quote'));
    expect(all(root.host, '.card .name').map((name) => text(name))).toEqual(['Contact', 'Quote', 'Address block']);
    const quote = must(
      all<HTMLLabelElement>(root.host, '.card').find((one) => text(one.querySelector('.name')) === 'Quote'),
      'Quote',
    );
    await vi.waitFor(() => expect(root.activeElement).toBe(quote.querySelector('input')));
  });
});
