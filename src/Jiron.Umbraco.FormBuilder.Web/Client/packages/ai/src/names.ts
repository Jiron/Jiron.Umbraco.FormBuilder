// The names the add-on's manifests share with Jiron Form Builder's backoffice (ADR-0018), each typed by the public
// declarations, so the typecheck fails when one drifts. The add-on loads no module of the core at run time: it is an
// extension as a site's own would be.
import type { UmbWorkspaceContext } from '@umbraco-cms/backoffice/workspace';
import { UmbContextToken } from '@umbraco-cms/backoffice/context-api';
import type {
  JfbFormEntityType,
  JfbFormWorkspaceAlias,
  JfbFormWorkspaceContext,
  JfbLibraryPlaceEntityType,
  JfbPermissionConditionConfig,
  JfbSectionPathname,
  JfbSettingsMenuAlias,
} from '../../backoffice/public/types/jiron-form-builder';

export const FORM_ENTITY_TYPE: JfbFormEntityType = 'jfb-form';
export const FORM_WORKSPACE: JfbFormWorkspaceAlias = 'Jiron.FormBuilder.Workspace.Form';
export const LIBRARY_ROOT: JfbLibraryPlaceEntityType = 'jfb-library-root';
export const FOLDER: JfbLibraryPlaceEntityType = 'jfb-folder';
export const SECTION_PATHNAME: JfbSectionPathname = 'jfb-forms';
export const SETTINGS_MENU: JfbSettingsMenuAlias = 'Jiron.FormBuilder.Menu.Settings';

/** Settings > AI's tab, a menu item that names the Settings menu, by its alias (ADR-0257). */
export const SETTINGS_TAB = 'Jiron.FormBuilder.AI.MenuItem.Settings';

/** The entity type of Settings > AI, the add-on's own screen. */
export const SETTINGS_ENTITY_TYPE = 'jfb-settings-ai';

/** Shown to who may manage settings. */
export const MANAGE_SETTINGS: JfbPermissionConditionConfig = {
  alias: 'Jiron.FormBuilder.Condition.Permission',
  needs: [{ anyOf: ['manageSettings'] }],
};

/** Shown to who may design a form somewhere. */
export const DESIGN_ANYWHERE: JfbPermissionConditionConfig = {
  alias: 'Jiron.FormBuilder.Condition.Permission',
  needs: [{ anyOf: ['design'], anywhere: true }],
};

/** The open form's workspace context, as the public declarations describe it. */
export const FORM_WORKSPACE_CONTEXT = new UmbContextToken<UmbWorkspaceContext, JfbFormWorkspaceContext>(
  'UmbWorkspaceContext',
  undefined,
  (context): context is JfbFormWorkspaceContext => context.getEntityType() === FORM_ENTITY_TYPE,
);

/** A form's screen, below the backoffice's root. */
export const formPath = (key: string): string =>
  `section/${SECTION_PATHNAME}/workspace/${FORM_ENTITY_TYPE}/edit/${key}`;
