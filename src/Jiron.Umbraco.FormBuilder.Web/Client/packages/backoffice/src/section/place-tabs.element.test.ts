// A place's tabs along the top of its screens (ROADMAP 22.2, ADR-0257): Umbraco's tab group with a link to each tab the
// user may open, the open one active, in the body layout's header; no row and no header for one tab; a heading for
// screen readers naming the place and the tab; drawn at once from what the user holds; and Settings' added tabs.
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { all, must, registerEnglish, settle, shadow, testHost, text } from '../testing';
import { jfbVisuallyHidden } from '../styles/tokens';
import type { MyPermissions } from './sidebar';

const server = vi.hoisted(() => ({ permissions: { global: [], anywhere: [] } as MyPermissions | undefined }));

vi.mock('./my-permissions', () => ({
  myPermissions: async () => server.permissions,
  myPermissionsNow: () => server.permissions,
}));

const { PlaceTabsElement } = await import('./place-tabs.element');

const person = (global: string[], anywhere = global): MyPermissions => ({ global, anywhere });
const ADMIN = person(['viewEntries', 'manageLibrary', 'manageSettings', 'managePermissions', 'privacyTools']);

async function tabsOf(tab: string, permissions: MyPermissions | undefined = ADMIN) {
  server.permissions = permissions;
  const host = testHost();
  const element = new PlaceTabsElement();
  element.tab = tab;
  host.append(element);
  await settle(element);
  const tabs = () => all(element, 'uui-tab');
  return { element, tabs, heading: () => text(shadow(element).querySelector('h2')) };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
});

describe("a place's tabs", () => {
  it("links each tab of the place to its screen's address, with the open one active, in the header", async () => {
    const { element, tabs } = await tabsOf('email');
    expect(
      tabs().map((tab) => [tab.getAttribute('label'), tab.getAttribute('href'), tab.hasAttribute('active')]),
    ).toEqual([
      ['General', 'section/jfb-forms/workspace/jfb-settings-general', false],
      ['Email', 'section/jfb-forms/workspace/jfb-settings-email', true],
      ['Spam protection', 'section/jfb-forms/workspace/jfb-settings-spam', false],
      ['Files', 'section/jfb-forms/workspace/jfb-settings-files', false],
      ['Access', 'section/jfb-forms/workspace/jfb-settings-access', false],
      ['Integrations', 'section/jfb-forms/workspace/jfb-settings-integrations', false],
    ]);
    expect(tabs()[1]?.querySelector('umb-icon')?.getAttribute('name')).toBe('icon-message');
    expect(must(shadow(element).querySelector('uui-tab-group'), 'group').getAttribute('aria-label')).toBe('Settings');
    expect(element.slot).toBe('header');
  });

  it('keeps the open tab marked after a click the router may stop, as a screen with changes asks first', async () => {
    const { element, tabs } = await tabsOf('email');
    // The router is not there to go anywhere: the click is all that happens, as when Stay keeps the screen.
    tabs()[0]?.addEventListener('click', (event) => event.preventDefault());
    tabs()[0]?.click();
    await settle(element);
    expect(
      tabs()
        .filter((tab) => tab.hasAttribute('active'))
        .map((tab) => tab.getAttribute('label')),
    ).toEqual(['Email']);
  });

  it('names its place and tab in a heading for screen readers, hidden from sight', async () => {
    expect(PlaceTabsElement.styles).toContain(jfbVisuallyHidden);
    expect((await tabsOf('general')).element.shadowRoot?.querySelector('h2')?.className).toBe('visually-hidden');
    document.body.replaceChildren();
    expect((await tabsOf('email')).heading()).toBe('Settings: Email');
    document.body.replaceChildren();
    expect((await tabsOf('spam')).heading()).toBe('Inbox: Spam');
    document.body.replaceChildren();
    // The Inbox's first tab has the place's name: said once.
    expect((await tabsOf('inbox')).heading()).toBe('Inbox');
    document.body.replaceChildren();
    expect((await tabsOf('dataCollection')).heading()).toBe('Privacy: Data collection');
  });

  it('shows only the tabs the user may open', async () => {
    const { tabs } = await tabsOf('retention', person(['privacyTools', 'viewEntries']));
    expect(tabs().map((tab) => tab.getAttribute('label'))).toEqual(['People', 'Retention']);
  });

  it('shows no row, and stands outside the header, where the user may open one tab of the place', async () => {
    const { element, tabs, heading } = await tabsOf('access', person(['managePermissions']));
    expect(tabs()).toEqual([]);
    expect(shadow(element).querySelector('uui-tab-group')).toBeNull();
    expect(element.slot).toBe('');
    expect(heading()).toBe('Settings: Access');
  });

  it('draws at once from what the user holds, before any wait', async () => {
    server.permissions = ADMIN;
    const element = new PlaceTabsElement();
    element.tab = 'deliveries';
    testHost().append(element);
    await element.updateComplete;
    expect(all(element, 'uui-tab').map((tab) => tab.getAttribute('label'))).toEqual(['Deliveries', 'Audit log']);
  });

  it("ends Settings' tabs with a tab a site or an add-on adds, whose own screen it marks open", async () => {
    umbExtensionsRegistry.register({
      type: 'menuItem',
      alias: 'Test.MenuItem.Settings',
      name: 'Test settings tab',
      weight: 10,
      meta: {
        label: 'Webshop',
        icon: 'icon-cart',
        entityType: 'test-settings-shop',
        menus: ['Jiron.FormBuilder.Menu.Settings'],
      },
    });
    try {
      const own = await tabsOf('general', person(['manageSettings']));
      await vi.waitFor(() => expect(own.tabs().at(-1)?.getAttribute('label')).toBe('Webshop'));
      expect(own.tabs().at(-1)?.getAttribute('href')).toBe('section/jfb-forms/workspace/test-settings-shop');
      document.body.replaceChildren();

      const added = await tabsOf('Test.MenuItem.Settings', person(['manageSettings']));
      await vi.waitFor(() => expect(added.heading()).toBe('Settings: Webshop'));
      expect(
        added
          .tabs()
          .filter((tab) => tab.hasAttribute('active'))
          .map((tab) => tab.getAttribute('label')),
      ).toEqual(['Webshop']);
    } finally {
      umbExtensionsRegistry.unregister('Test.MenuItem.Settings');
    }
  });
});
