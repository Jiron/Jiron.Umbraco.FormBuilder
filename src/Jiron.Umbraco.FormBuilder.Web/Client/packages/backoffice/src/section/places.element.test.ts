// The five places at the top of the sidebar (ROADMAP 22.2, ADR-0257, ADR-0209): the places a user may open, in order, as one
// named navigation list; each a link to its first tab the user may open; the open place on a tab's screen and on an
// editor a tab opens; and the Inbox's unseen count as its badge, up to "99+" and nothing at none.
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import type { MyPermissions } from './sidebar';

const server = vi.hoisted(() => ({
  count: 12,
  more: false,
  reads: 0,
  permissions: { global: [], anywhere: [] } as MyPermissions,
}));

vi.mock('../entries/entries-api', () => ({
  readUnseen: async () => {
    server.reads++;
    return { count: server.count, more: server.more };
  },
}));

vi.mock('./my-permissions', () => ({
  myPermissions: async () => server.permissions,
  myPermissionsNow: () => server.permissions,
}));

const { PlacesElement } = await import('./places.element');

const person = (global: string[], anywhere = global): MyPermissions => ({ global, anywhere });
const ADMIN = person(['viewEntries', 'manageLibrary', 'manageSettings', 'managePermissions', 'privacyTools']);

async function placesOf(permissions: MyPermissions) {
  server.permissions = permissions;
  const host = testHost();
  const element = new PlacesElement();
  host.append(element);
  await settle(element);
  const root = shadow(element);
  const items = () => all(element, 'uui-menu-item');
  const item = (label: string) =>
    must(
      items().find((one) => text(one).includes(label) || one.getAttribute('label') === label),
      label,
    );
  return { element, root, items, item };
}

beforeAll(registerEnglish);

beforeEach(() => {
  Object.assign(server, { count: 12, more: false, reads: 0 });
  window.history.replaceState(null, '', '/umbraco/section/jfb-forms');
});

afterEach(() => {
  document.body.replaceChildren();
});

describe('the places', () => {
  it('lists the five places in order, as one navigation list named for screen readers, each with its icon', async () => {
    const { root, items } = await placesOf(ADMIN);
    expect(must(root.querySelector('nav'), 'nav').getAttribute('aria-label')).toBe('Form Builder');
    expect(items().map((one) => one.getAttribute('label'))).toEqual([
      'Inbox, 12 unseen',
      'Library',
      'Settings',
      'Privacy',
      'Activity',
    ]);
    expect(items().map((one) => one.querySelector('umb-icon')?.getAttribute('name'))).toEqual([
      'icon-inbox',
      'icon-books',
      'icon-settings',
      'icon-key',
      'icon-activity',
    ]);
  });

  it('shows each persona only the places it may open', async () => {
    const labels = async (permissions: MyPermissions) => {
      const { items } = await placesOf(permissions);
      const seen = items().map((one) => text(one.querySelector('[slot="label"]')) || one.getAttribute('label'));
      document.body.replaceChildren();
      return seen;
    };
    expect(await labels(person(['viewEntries', 'editEntries']))).toEqual(['Inbox']);
    expect(await labels(person(['design', 'manageLibrary']))).toEqual(['Library']);
    expect(await labels(person(['viewEntries', 'privacyTools']))).toEqual(['Inbox', 'Privacy']);
    expect(await labels(person([]))).toEqual([]);
  });

  it('leads each place to the first of its tabs the user may open', async () => {
    const { items } = await placesOf(person(['viewEntries', 'manageLibrary', 'managePermissions', 'privacyTools']));
    expect(items().map((one) => one.getAttribute('href'))).toEqual([
      'section/jfb-forms/workspace/jfb-inbox',
      'section/jfb-forms/workspace/jfb-option-lists',
      'section/jfb-forms/workspace/jfb-settings-access',
      'section/jfb-forms/workspace/jfb-privacy-people',
    ]);
  });

  it("marks the open place on each of its tabs' screens and on an editor a tab opens, and none elsewhere", async () => {
    const { element, items } = await placesOf(ADMIN);
    const open = () =>
      items()
        .filter((one) => one.hasAttribute('active'))
        .map((one) => one.getAttribute('label'));
    expect(open()).toEqual([]);
    for (const [path, place] of [
      ['workspace/jfb-settings-email', 'Settings'],
      ['workspace/jfb-spam', 'Inbox, 12 unseen'],
      ['workspace/jfb-theme/edit/4f1c', 'Library'],
      ['workspace/jfb-privacy-collection', 'Privacy'],
    ]) {
      window.history.pushState(null, '', `/umbraco/section/jfb-forms/${path}`);
      window.dispatchEvent(new Event('navigationend'));
      await settle(element);
      expect(open(), path).toEqual([place]);
    }
    window.history.pushState(null, '', '/umbraco/section/jfb-forms/workspace/jfb-form/edit/9a2b');
    window.dispatchEvent(new Event('navigationend'));
    await settle(element);
    expect(open(), 'a form').toEqual([]);
  });
});

describe("the Inbox's count", () => {
  it('shows the unseen count as its badge, and says it with the name to screen readers', async () => {
    const { item } = await placesOf(ADMIN);
    expect(server.reads).toBe(1);
    const inbox = item('Inbox, 12 unseen');
    const badge = must(inbox.querySelector('uui-badge'), 'the badge');
    expect([text(badge), badge.getAttribute('slot'), badge.getAttribute('aria-hidden')]).toEqual([
      '12',
      'badge',
      'true',
    ]);
    expect(text(inbox.querySelector('[slot="label"]'))).toBe('Inbox');
  });

  it('says 99+ beyond the count the server gives, and nothing at none', async () => {
    server.count = 99;
    server.more = true;
    const many = await placesOf(ADMIN);
    expect(text(many.item('Inbox, 99+ unseen').querySelector('uui-badge'))).toBe('99+');
    document.body.replaceChildren();

    server.count = 0;
    server.more = false;
    const none = await placesOf(ADMIN);
    const inbox = none.items()[0];
    expect(inbox?.getAttribute('label')).toBe('Inbox');
    expect(inbox?.querySelector('uui-badge, [slot="label"]')).toBeNull();
  });

  it('never asks for the count of a user who may not open the Inbox', async () => {
    await placesOf(person(['manageLibrary']));
    expect(server.reads).toBe(0);
  });
});
