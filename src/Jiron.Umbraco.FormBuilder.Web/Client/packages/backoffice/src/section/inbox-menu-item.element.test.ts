// The Inbox's sidebar item (ROADMAP 11.1, IA 1, ADR-0209): the unseen count as its badge, up to "99+" and nothing at none,
// the item named for screen readers with the count, its address, and the open item marked as Umbraco marks its own.
import { UmbBasicState } from '@umbraco-cms/backoffice/observable-api';
import { UMB_SECTION_CONTEXT } from '@umbraco-cms/backoffice/section';
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { must, registerEnglish, settle, shadow, testHost, text } from '../testing';

const server = vi.hoisted(() => ({ count: 12, more: false, reads: 0 }));

vi.mock('../entries/entries-api', () => ({
  readUnseen: async () => {
    server.reads++;
    return { count: server.count, more: server.more };
  },
}));

const { InboxMenuItemElement } = await import('./inbox-menu-item.element');

const MANIFEST = {
  type: 'menuItem',
  alias: 'Jiron.FormBuilder.MenuItem.Inbox',
  name: 'Jiron Form Builder inbox menu item',
  meta: {
    label: '#jfbSection_inbox',
    icon: 'icon-inbox',
    entityType: 'jfb-inbox',
    menus: ['Jiron.FormBuilder.Menu.Entries'],
  },
};

async function itemOf() {
  const host = testHost();
  host.provideContext(UMB_SECTION_CONTEXT, {
    pathname: new UmbBasicState<string | undefined>('jfb-forms').asObservable(),
    getHostElement: () => host,
  } as never);
  const item = new InboxMenuItemElement();
  item.manifest = MANIFEST as never;
  host.append(item);
  await settle(item);
  const menuItem = () => must(shadow(item).querySelector('uui-menu-item'), 'the menu item');
  return { item, menuItem };
}

beforeAll(registerEnglish);

beforeEach(() => {
  Object.assign(server, { count: 12, more: false, reads: 0 });
  window.history.replaceState(null, '', '/umbraco/section/jfb-forms');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe("the Inbox's item", () => {
  it('shows the unseen count as its badge, and says it with the name to screen readers', async () => {
    const { menuItem } = await itemOf();
    expect(server.reads).toBe(1);
    const badge = must(menuItem().querySelector('uui-badge'), 'the badge');
    expect([text(badge), badge.getAttribute('slot'), badge.getAttribute('aria-hidden')]).toEqual([
      '12',
      'badge',
      'true',
    ]);
    expect(menuItem().getAttribute('label')).toBe('Inbox, 12 unseen');
    expect(text(menuItem().querySelector('[slot="label"]'))).toBe('Inbox');
    expect(menuItem().getAttribute('href')).toBe('section/jfb-forms/workspace/jfb-inbox');
  });

  it('says 99+ beyond the count the server gives, and nothing at none', async () => {
    server.count = 99;
    server.more = true;
    const many = await itemOf();
    expect(text(many.menuItem().querySelector('uui-badge'))).toBe('99+');
    expect(many.menuItem().getAttribute('label')).toBe('Inbox, 99+ unseen');
    document.body.replaceChildren();

    server.count = 0;
    server.more = false;
    const none = await itemOf();
    expect(none.menuItem().querySelector('uui-badge, [slot="label"]')).toBeNull();
    expect(none.menuItem().getAttribute('label')).toBe('Inbox');
  });

  it('is the open item while its screen is, as Umbraco marks its own items', async () => {
    const { menuItem, item } = await itemOf();
    expect(menuItem().hasAttribute('active')).toBe(false);
    window.history.pushState(null, '', '/umbraco/section/jfb-forms/workspace/jfb-inbox');
    window.dispatchEvent(new Event('navigationend'));
    await settle(item);
    expect(menuItem().hasAttribute('active')).toBe(true);
  });
});
