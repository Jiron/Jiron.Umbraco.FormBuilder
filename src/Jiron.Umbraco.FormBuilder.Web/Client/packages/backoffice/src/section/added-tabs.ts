// The tabs a site or an add-on adds to Settings (ADR-0018, ADR-0257): a `menuItem` that names the Settings menu in
// `meta.menus`, the public `JfbSettingsMenuAlias`, is a tab after Settings' own, shown by its own conditions, and its
// workspace is a screen of the Settings place. The AI add-on's Settings > AI is one.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbExtensionsManifestInitializer } from '@umbraco-cms/backoffice/extension-api';
import { umbExtensionsRegistry } from '@umbraco-cms/backoffice/extension-registry';
import type { ManifestMenuItem } from '@umbraco-cms/backoffice/menu';

/** The menu an added tab names, which the section registers so the name stays good. */
export const SETTINGS_MENU_ALIAS = 'Jiron.FormBuilder.Menu.Settings';

/** The place added tabs join. */
export const ADDED_TABS_PLACE = 'settings';

/** A tab an extension adds: its id is its manifest's alias, its label a text or a `#key`. */
export interface AddedTab {
  id: string;
  entityType: string;
  icon: string;
  label: string;
}

/** Calls back with the added tabs the user may open, in their weight's order, and again whenever they change. */
export function observeAddedTabs(host: UmbControllerHost, listen: (tabs: AddedTab[]) => void): void {
  new UmbExtensionsManifestInitializer(
    host,
    umbExtensionsRegistry,
    'menuItem',
    (manifest: ManifestMenuItem) => manifest.meta.menus.includes(SETTINGS_MENU_ALIAS),
    (permitted) =>
      listen(
        permitted.map(({ manifest }) => ({
          id: manifest.alias,
          entityType: manifest.meta.entityType ?? '',
          icon: manifest.meta.icon ?? '',
          label: manifest.meta.label ?? manifest.name,
        })),
      ),
    'jfbAddedTabs',
  );
}
