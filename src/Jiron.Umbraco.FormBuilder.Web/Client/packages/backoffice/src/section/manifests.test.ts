// The section's manifests (ADR-0021, ADR-0067, ADR-0165, ADR-0257): its pathname and entity type as the server's links name
// them, the sidebar of docs/ux/information-architecture.md (the places with their tabs, each with its permission, then
// the tree), and a workspace behind everything that opens one.
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { manifests } from '../bundle.manifests';
import de from '../lang/de';
import en from '../lang/en';
import {
  ENTRY_ENTITY_TYPE,
  FOLDER_ENTITY_TYPE,
  FORM_ENTITY_TYPE,
  LIBRARY_ROOT_ENTITY_TYPE,
  SECTION_ALIAS,
  SECTION_PATHNAME,
} from './entity-types';
import { PERMISSION_CONDITION } from './my-permissions';
import {
  FORMS_NEEDS,
  meets,
  PLACES,
  PLACES_NEEDS,
  placesFor,
  tabsFor,
  type MyPermissions,
  type Needs,
} from './sidebar';

interface Manifest {
  type: string;
  kind?: string;
  alias: string;
  weight?: number;
  meta?: Record<string, unknown>;
  conditions?: { alias: string; needs?: Needs[]; none?: boolean; match?: string }[];
  element?: () => Promise<Record<string, unknown>>;
  api?: () => Promise<Record<string, unknown>>;
}

const all = manifests as unknown as Manifest[];
const byType = (type: string) => all.filter((manifest) => manifest.type === type);

/** A source file of the server. */
function serverSource(...path: string[]): string {
  let folder = dirname(fileURLToPath(import.meta.url));
  while (!existsSync(join(folder, 'Jiron.Umbraco.FormBuilder.slnx'))) folder = dirname(folder);
  return readFileSync(join(folder, 'src', ...path), 'utf8');
}

const links = serverSource('Jiron.Umbraco.FormBuilder.Core', 'MergeTags', 'BackofficeLinks.cs');
const serverConstant = (source: string, name: string) =>
  new RegExp(`const string ${name} = "([^"]+)"`).exec(source)?.[1];

describe('the section', () => {
  it("has the pathname and alias the server's links and user groups name", () => {
    const [section] = byType('section');
    expect(section?.alias).toBe(SECTION_ALIAS);
    expect(section?.alias).toBe(
      serverConstant(serverSource('Jiron.Umbraco.FormBuilder.Core', 'FormBuilderConstants.cs'), 'SectionAlias'),
    );
    expect(section?.meta?.['pathname']).toBe(SECTION_PATHNAME);
    expect(section?.meta?.['pathname']).toBe(serverConstant(links, 'SectionPathname'));
    // Named "Form Builder" in Umbraco's top bar, in every language, while its forms tree stays "Forms" (ADR-0171).
    expect(section?.meta?.['label']).toBe('#jfbSection_section');
    expect([en.jfbSection.section, de.jfbSection.section, en.jfbSection.forms, de.jfbSection.forms]).toEqual([
      'Form Builder',
      'Form Builder',
      'Forms',
      'Formulare',
    ]);
    expect(section?.conditions).toEqual([{ alias: 'Umb.Condition.SectionUserPermission', match: SECTION_ALIAS }]);
  });

  it("has the Settings menu a site's menu item names, as the public declarations promise (ADR-0018)", () => {
    const promised: import('../../public/types/jiron-form-builder').JfbSettingsMenuAlias =
      'Jiron.FormBuilder.Menu.Settings';
    expect(byType('menu').map((menu) => menu.alias)).toContain(promised);
  });

  it('has one dashboard, the Overview, only in this section', () => {
    const dashboards = byType('dashboard');
    expect(dashboards.map((dashboard) => dashboard.alias)).toEqual(['Jiron.FormBuilder.Dashboard.Overview']);
    expect(dashboards[0]?.conditions).toEqual([{ alias: 'Umb.Condition.SectionAlias', match: SECTION_ALIAS }]);
  });

  it("gives an entry's link a workspace of the entity type the server names", () => {
    expect(ENTRY_ENTITY_TYPE).toBe(serverConstant(links, 'EntryEntityType'));
    expect(byType('workspace').map((workspace) => workspace.meta?.['entityType'])).toContain(ENTRY_ENTITY_TYPE);
  });

  it('puts a workspace behind every tab, every editor a tab opens, and what the tree opens', () => {
    const workspaces = byType('workspace').map((workspace) => workspace.meta?.['entityType']);
    const opened = PLACES.flatMap((place) => [...place.tabs.map((tab) => tab.entityType), ...(place.editors ?? [])]);
    expect(opened.filter((entityType) => !workspaces.includes(entityType))).toEqual([]);
    // No menu item but the tree's: the places and their tabs are the sidebar's and the screens' own (ADR-0257).
    expect(byType('menuItem').filter((item) => item.kind !== 'tree')).toEqual([]);
    expect(workspaces).toEqual(
      expect.arrayContaining([FORM_ENTITY_TYPE, FOLDER_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE]),
    );
    expect(new Set(workspaces).size).toBe(workspaces.length);
  });

  it('loads every workspace element, each a defined custom element that Umbraco can construct', async () => {
    const plain = byType('workspace').filter((workspace) => workspace.kind !== 'routable');
    const loaded = await Promise.all(plain.map((workspace) => workspace.element?.()));
    // A routable workspace has a context, whose route makes its editor element.
    loaded.push(await import('../designer/form-workspace-editor.element'));
    expect(loaded.every((module) => module?.['element'])).toBe(true);
    // Umbraco makes a workspace's element with "new"; a class no tag is defined for throws "Illegal constructor" there.
    for (const module of loaded) {
      const Element = module?.['element'] as CustomElementConstructor;
      expect(() => new Element(), Element.name).not.toThrow();
    }
  });

  it('shows no workspace or view the stand-in of 9.1: each has its own element (ADR-0165)', async () => {
    const views = byType('workspaceView').filter((view) =>
      view.alias.startsWith('Jiron.FormBuilder.WorkspaceView.Form.'),
    );
    const loaded = await Promise.all([...byType('workspace'), ...views].map((manifest) => manifest.element?.()));
    expect(views.map((view) => view.alias.split('.').at(-1))).toEqual([
      'Design',
      'Settings',
      'Actions',
      'Entries',
      'Insights',
      'History',
    ]);
    const tags = loaded
      .map((module) => module?.['element'] as CustomElementConstructor | undefined)
      .filter((element) => element !== undefined)
      .map((element) => customElements.getName(element));
    expect(tags.filter((tag) => tag === null || tag === 'jfb-coming-screen')).toEqual([]);
    expect(customElements.get('jfb-coming-screen')).toBeUndefined();
  });

  it("gives a form and each kind of the library its routable workspace, whose context the workspace's api is", async () => {
    const routable = byType('workspace').filter((workspace) => workspace.kind === 'routable');
    expect(routable.map((workspace) => [workspace.alias, workspace.meta?.['entityType']])).toEqual([
      ['Jiron.FormBuilder.Workspace.Form', FORM_ENTITY_TYPE],
      ['Jiron.FormBuilder.Workspace.OptionList', 'jfb-option-list'],
      ['Jiron.FormBuilder.Workspace.Theme', 'jfb-theme'],
      ['Jiron.FormBuilder.Workspace.EmailLayout', 'jfb-email-layout'],
    ]);
    for (const workspace of routable) {
      const context = await workspace.api?.();
      expect(typeof context?.['api'], workspace.alias).toBe('function');
    }
  });
});

describe('the sidebar', () => {
  /** The labels of docs/ux/information-architecture.md, section 1: each place, and each of its tabs. */
  const expected = {
    inbox: [
      'Inbox',
      'Posteingang',
      [
        ['inbox', 'Inbox', 'Posteingang'],
        ['spam', 'Spam', 'Spam'],
      ],
    ],
    library: [
      'Library',
      'Bibliothek',
      [
        ['optionLists', 'Option lists', 'Optionslisten'],
        ['themes', 'Themes', 'Themes'],
        ['emailLayouts', 'Email layouts', 'E-Mail-Layouts'],
        ['templates', 'Templates', 'Vorlagen'],
      ],
    ],
    settings: [
      'Settings',
      'Einstellungen',
      [
        ['general', 'General', 'Allgemein'],
        ['email', 'Email', 'E-Mail'],
        ['spamProtection', 'Spam protection', 'Spamschutz'],
        ['files', 'Files', 'Dateien'],
        ['access', 'Access', 'Zugriff'],
        ['integrations', 'Integrations', 'Integrationen'],
      ],
    ],
    privacy: [
      'Privacy',
      'Datenschutz',
      [
        ['people', 'People', 'Personen'],
        ['retention', 'Retention', 'Aufbewahrung'],
        ['dataCollection', 'Data collection', 'Datenerfassung'],
      ],
    ],
    activity: [
      'Activity',
      'Aktivit\u00e4t',
      [
        ['deliveries', 'Deliveries', 'Zustellungen'],
        ['auditLog', 'Audit log', 'Protokoll'],
      ],
    ],
  } as const;

  it('holds the places and their tabs of the design, in order, in both languages', () => {
    expect(PLACES.map((place) => place.id)).toEqual(Object.keys(expected));
    for (const place of PLACES) {
      const [english, german, tabs] = expected[place.id as keyof typeof expected];
      expect(en.jfbSection[place.id as keyof typeof en.jfbSection]).toBe(english);
      expect(de.jfbSection[place.id as keyof typeof de.jfbSection]).toBe(german);
      expect(place.tabs.map((tab) => tab.id)).toEqual(tabs.map(([id]) => id));
      for (const [id, tabEnglish, tabGerman] of tabs) {
        expect(en.jfbSection[id as keyof typeof en.jfbSection]).toBe(tabEnglish);
        expect(de.jfbSection[id as keyof typeof de.jfbSection]).toBe(tabGerman);
      }
    }
  });

  it('gives every tab its screen, a workspace of its entity type in the table order', () => {
    const screens = byType('workspace')
      .filter(
        (workspace) => workspace.alias.startsWith('Jiron.FormBuilder.Workspace.') && workspace.kind !== 'routable',
      )
      .map((workspace) => workspace.meta?.['entityType']);
    const tabs = PLACES.flatMap((place) => place.tabs.map((tab) => tab.entityType));
    expect(screens.filter((entityType) => tabs.includes(entityType as string))).toEqual(tabs);
  });

  it('shows the places first, then the tree or its sentence, all inside the section', () => {
    const apps = byType('sectionSidebarApp').sort((a, b) => (b.weight ?? 0) - (a.weight ?? 0));
    expect(apps.map((app) => app.alias)).toEqual([
      'Jiron.FormBuilder.SidebarApp.Places',
      'Jiron.FormBuilder.SidebarApp.Forms',
      'Jiron.FormBuilder.SidebarApp.NoForms',
    ]);
    expect(apps[0]?.conditions).toEqual([
      { alias: 'Umb.Condition.SectionAlias', match: SECTION_ALIAS },
      { alias: PERMISSION_CONDITION, needs: PLACES_NEEDS },
    ]);
    expect(apps.every((app) => app.conditions?.[0]?.match === SECTION_ALIAS)).toBe(true);
  });

  const person = (global: string[], anywhere = global): MyPermissions => ({ global, anywhere });
  /** What a persona of ADR-0013 sees: each place with the tabs it may open. */
  const seenBy = (permissions: MyPermissions) =>
    Object.fromEntries(
      placesFor(permissions).map(({ place }) => [place.id, tabsFor(place, permissions).map((tab) => tab.id)]),
    );

  it('shows the administrator every place with every tab', () => {
    const admin = person([
      'design',
      'manageActions',
      'viewEntries',
      'viewSensitive',
      'editEntries',
      'deleteEntries',
      'export',
      'manageLibrary',
      'manageSettings',
      'managePermissions',
      'privacyTools',
    ]);
    expect(seenBy(admin)).toEqual(
      Object.fromEntries(PLACES.map((place) => [place.id, place.tabs.map((tab) => tab.id)])),
    );
  });

  it('shows the entry handler and the viewer the Inbox alone, with Spam', () => {
    expect(seenBy(person(['viewEntries', 'editEntries', 'export']))).toEqual({ inbox: ['inbox', 'spam'] });
    expect(seenBy(person(['viewEntries']))).toEqual({ inbox: ['inbox', 'spam'] });
  });

  it('shows the privacy officer People and Retention without Data collection, and no setting', () => {
    expect(seenBy(person(['privacyTools', 'viewEntries', 'viewSensitive', 'deleteEntries']))).toEqual({
      inbox: ['inbox', 'spam'],
      privacy: ['people', 'retention'],
    });
  });

  it('shows the designer the library alone', () => {
    expect(seenBy(person(['design', 'manageActions', 'manageLibrary']))).toEqual({
      library: ['optionLists', 'themes', 'emailLayouts', 'templates'],
    });
  });

  it('asks manageSettings for Retention just as privacyTools, and for each other setting, but managePermissions for Access', () => {
    expect(seenBy(person(['manageSettings']))).toEqual({
      settings: ['general', 'email', 'spamProtection', 'files', 'integrations'],
      privacy: ['retention', 'dataCollection'],
      activity: ['deliveries', 'auditLog'],
    });
    expect(seenBy(person(['managePermissions']))).toEqual({ settings: ['access'] });
  });

  it('leads each place to the first of its tabs the user may open', () => {
    const first = (permissions: MyPermissions) =>
      Object.fromEntries(placesFor(permissions).map(({ place, first: tab }) => [place.id, tab.id]));
    expect(first(person(['manageSettings', 'managePermissions', 'privacyTools']))).toEqual({
      settings: 'general',
      privacy: 'people',
      activity: 'deliveries',
    });
    expect(first(person(['managePermissions', 'manageSettings'], []))).toEqual({
      settings: 'general',
      privacy: 'retention',
      activity: 'deliveries',
    });
    expect(first(person(['managePermissions']))).toEqual({ settings: 'access' });
  });

  it('shows the Inbox to someone who may view the entries of one folder, but no setting that holds only there', () => {
    expect(seenBy(person([], ['viewEntries', 'manageSettings']))).toEqual({ inbox: ['inbox', 'spam'] });
  });

  it('shows nothing to a user without any permission, who gets the sentence in the place of the tree', () => {
    const nobody = person([]);
    expect(seenBy(nobody)).toEqual({});
    expect(meets(nobody, PLACES_NEEDS)).toBe(false);
    expect(meets(nobody, FORMS_NEEDS)).toBe(false);
    const sentence = byType('sectionSidebarApp').find((app) => app.alias.endsWith('.NoForms'));
    expect(sentence?.conditions?.[1]).toEqual({ alias: PERMISSION_CONDITION, needs: FORMS_NEEDS, none: true });
  });

  it('shows the tree to whoever holds a permission on one form', () => {
    expect(meets(person([], ['viewEntries']), FORMS_NEEDS)).toBe(true);
    expect(meets(person(['manageLibrary']), FORMS_NEEDS)).toBe(false);
  });
});

describe("the user group screen's Form Builder view (ROADMAP 25.1, ADR-0262)", () => {
  const view = all.find((manifest) => manifest.alias === 'Jiron.FormBuilder.WorkspaceView.UserGroupAccess');
  const person = (global: string[]): MyPermissions => ({ global, anywhere: global });

  it("sits on Umbraco's user group screen after Details, named as the section, on a group that exists", () => {
    expect(view?.type).toBe('workspaceView');
    // Umbraco's Details weighs 90; a lower weight comes after it.
    expect(view?.weight).toBeLessThan(90);
    expect(view?.meta).toEqual({ label: '#jfbSection_section', pathname: 'form-builder', icon: 'icon-jfb' });
    expect(view?.conditions?.slice(0, 2)).toEqual([
      { alias: 'Umb.Condition.WorkspaceAlias', match: 'Umb.Workspace.UserGroup' },
      { alias: 'Umb.Condition.WorkspaceEntityIsNew', match: false },
    ]);
  });

  it('shows only to a user with the section who may manage permissions', () => {
    expect(view?.conditions?.slice(2)).toEqual([
      { alias: 'Umb.Condition.SectionUserPermission', match: SECTION_ALIAS },
      { alias: PERMISSION_CONDITION, needs: [{ anyOf: ['managePermissions'] }] },
    ]);
    const conditions = view?.conditions ?? [];
    const permission = conditions.find((condition) => condition.alias === PERMISSION_CONDITION);
    expect(meets(person(['managePermissions']), permission?.needs ?? [])).toBe(true);
    expect(meets(person(['manageSettings', 'design']), permission?.needs ?? [])).toBe(false);
  });

  it('loads its element lazily, a defined custom element', async () => {
    expect(typeof view?.element).toBe('function');
    const loaded = await view?.element?.();
    const Element = loaded?.['default'] as CustomElementConstructor | undefined;
    expect(Element && customElements.getName(Element)).toBe('jfb-user-group-access-view');
  });
});
