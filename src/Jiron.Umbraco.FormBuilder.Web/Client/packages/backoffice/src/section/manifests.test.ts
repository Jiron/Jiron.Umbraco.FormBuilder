// The section's manifests (ADR-0021, ADR-0067, ADR-0165): its pathname and entity type as the server's links name them, the sidebar of
// docs/ux/information-architecture.md with the permission of each item, and a workspace behind everything that opens one.
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
import { FORMS_NEEDS, meets, needsOfGroup, SIDEBAR, type MyPermissions, type Needs } from './sidebar';

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
    expect(section?.conditions).toEqual([{ alias: 'Umb.Condition.SectionUserPermission', match: SECTION_ALIAS }]);
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

  it('puts a workspace behind every menu item, and behind what the tree opens', () => {
    const workspaces = byType('workspace').map((workspace) => workspace.meta?.['entityType']);
    const opened = byType('menuItem')
      .filter((item) => item.kind !== 'tree')
      .map((item) => item.meta?.['entityType']);
    expect(opened.length).toBe(SIDEBAR.flatMap((group) => group.items).length);
    expect(opened.filter((entityType) => !workspaces.includes(entityType))).toEqual([]);
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

  it("gives the form its routable workspace, whose context the workspace's api is", async () => {
    const routable = byType('workspace').filter((workspace) => workspace.kind === 'routable');
    expect(routable.map((workspace) => [workspace.alias, workspace.meta?.['entityType']])).toEqual([
      ['Jiron.FormBuilder.Workspace.Form', FORM_ENTITY_TYPE],
    ]);
    const context = await routable[0]?.api?.();
    expect(typeof context?.['api']).toBe('function');
  });
});

describe('the sidebar', () => {
  /** The labels of docs/ux/information-architecture.md, section 1. */
  const expected = {
    entries: [
      'Entries',
      'Eintr\u00e4ge',
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

  it('holds the groups and items of the design, in order, in both languages', () => {
    expect(SIDEBAR.map((group) => group.id)).toEqual(Object.keys(expected));
    for (const group of SIDEBAR) {
      const [english, german, items] = expected[group.id as keyof typeof expected];
      expect(en.jfbSection[group.id as keyof typeof en.jfbSection]).toBe(english);
      expect(de.jfbSection[group.id as keyof typeof de.jfbSection]).toBe(german);
      expect(group.items.map((item) => item.id)).toEqual(items.map(([id]) => id));
      for (const [id, itemEnglish, itemGerman] of items) {
        expect(en.jfbSection[id as keyof typeof en.jfbSection]).toBe(itemEnglish);
        expect(de.jfbSection[id as keyof typeof de.jfbSection]).toBe(itemGerman);
      }
    }
  });

  it('shows the groups in order under the tree, and each item in order in its menu, by weight', () => {
    const apps = byType('sectionSidebarApp').sort((a, b) => (b.weight ?? 0) - (a.weight ?? 0));
    expect(apps.map((app) => app.alias)).toEqual([
      'Jiron.FormBuilder.SidebarApp.Forms',
      'Jiron.FormBuilder.SidebarApp.NoForms',
      ...SIDEBAR.map((group) => `Jiron.FormBuilder.SidebarApp.${group.id.charAt(0).toUpperCase()}${group.id.slice(1)}`),
    ]);
    const library = byType('menuItem').filter((item) =>
      (item.meta?.['menus'] as string[]).includes('Jiron.FormBuilder.Menu.Library'),
    );
    expect(library.sort((a, b) => (b.weight ?? 0) - (a.weight ?? 0)).map((item) => item.meta?.['label'])).toEqual([
      '#jfbSection_optionLists',
      '#jfbSection_themes',
      '#jfbSection_emailLayouts',
      '#jfbSection_templates',
    ]);
  });

  it('keeps every group inside the section, and shows its heading only with an item the user sees', () => {
    for (const group of SIDEBAR) {
      const app = byType('sectionSidebarApp').find((each) =>
        each.alias.endsWith(`.${group.id.charAt(0).toUpperCase()}${group.id.slice(1)}`),
      );
      expect(app?.kind).toBe('menu');
      expect(app?.conditions).toEqual([
        { alias: 'Umb.Condition.SectionAlias', match: SECTION_ALIAS },
        { alias: PERMISSION_CONDITION, needs: needsOfGroup(group) },
      ]);
    }
  });

  /** What each persona of ADR-0013 sees, by the items' ids. */
  const seenBy = (permissions: MyPermissions) =>
    SIDEBAR.flatMap((group) => group.items)
      .filter((item) => meets(permissions, [item]))
      .map((item) => item.id);
  const person = (global: string[], anywhere = global): MyPermissions => ({ global, anywhere });

  it("shows the privacy officer People and Retention without Data collection, and no setting's item", () => {
    expect(seenBy(person(['privacyTools', 'viewEntries', 'viewSensitive', 'deleteEntries']))).toEqual([
      'inbox',
      'spam',
      'people',
      'retention',
    ]);
  });

  it('asks manageSettings for Retention just as privacyTools, and for each other setting, but managePermissions for Access', () => {
    expect(seenBy(person(['manageSettings']))).toEqual([
      'general',
      'email',
      'spamProtection',
      'files',
      'integrations',
      'retention',
      'dataCollection',
      'deliveries',
      'auditLog',
    ]);
    expect(seenBy(person(['managePermissions']))).toEqual(['access']);
  });

  it('shows the library to manageLibrary alone', () => {
    expect(seenBy(person(['manageLibrary']))).toEqual(['optionLists', 'themes', 'emailLayouts', 'templates']);
  });

  it('shows the Inbox to someone who may view the entries of one folder, but no setting that holds only there', () => {
    expect(seenBy(person([], ['viewEntries', 'manageSettings']))).toEqual(['inbox', 'spam']);
  });

  it('shows nothing to a user without any permission, who gets the sentence in the place of the tree', () => {
    const nobody = person([]);
    expect(seenBy(nobody)).toEqual([]);
    expect(meets(nobody, FORMS_NEEDS)).toBe(false);
    const sentence = byType('sectionSidebarApp').find((app) => app.alias.endsWith('.NoForms'));
    expect(sentence?.conditions?.[1]).toEqual({ alias: PERMISSION_CONDITION, needs: FORMS_NEEDS, none: true });
  });

  it('shows the tree to whoever holds a permission on one form', () => {
    expect(meets(person([], ['viewEntries']), FORMS_NEEDS)).toBe(true);
    expect(meets(person(['manageLibrary']), FORMS_NEEDS)).toBe(false);
  });
});
