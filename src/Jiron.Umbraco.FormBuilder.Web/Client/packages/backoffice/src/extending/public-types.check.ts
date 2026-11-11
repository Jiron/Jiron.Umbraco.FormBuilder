// What the shipped declarations promise, held to what runs (ROADMAP 15.1, ADR-0233): the typecheck fails when the
// form workspace's context, the field view model `<jfb-form>` hands a control's writer, or the element's writer stop
// fitting the types extension authors compile against. Nothing here runs.
import type { ControlWriter, CustomField } from '@jfb/element/views';
import type { JfbControlField, JfbControlWriter, JfbFormWorkspaceContext } from '../../public/types/jiron-form-builder';
import type { FormWorkspaceContext } from '../designer/form-workspace.context';
import type { DesignerContext } from '../designer/designer-host';
import type {
  JfbLibraryPlaceEntityType,
  JfbPermission,
  JfbSectionPathname,
  JfbSettingsMenuAlias,
} from '../../public/types/jiron-form-builder';
import { FOLDER_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, SECTION_PATHNAME } from '../section/entity-types';
import type { Permission } from '../section/sidebar';

/** The workspace context a site's view reads is the form workspace's own. */
export type WorkspaceFits = FormWorkspaceContext extends JfbFormWorkspaceContext ? true : never;
export const workspaceFits: WorkspaceFits = true;

/** Every field `<jfb-form>` hands a writer, a site's own type's, is a field the declarations describe. */
export const fieldFits = (field: CustomField): JfbControlField => field;

/** A writer a site declares is one the element calls. */
export const writerFits = (write: JfbControlWriter): ControlWriter => write;

/** The designer host reads the form workspace's own context. */
export type DesignerFits = FormWorkspaceContext extends DesignerContext ? true : never;
export const designerFits: DesignerFits = true;

/** The places, the section's address, the settings menu and the permissions the declarations name are the backoffice's own (ADR-0018). */
export const placesFit: readonly JfbLibraryPlaceEntityType[] = [LIBRARY_ROOT_ENTITY_TYPE, FOLDER_ENTITY_TYPE];
export const pathnameFits: JfbSectionPathname = SECTION_PATHNAME;
// The menu's registration is held to this name by section/manifests.test.ts.
export const settingsMenuFits: JfbSettingsMenuAlias = 'Jiron.FormBuilder.Menu.Settings';
export const permissionsFit = (permission: Permission): JfbPermission => permission;
export const permissionsFitBack = (permission: JfbPermission): Permission => permission;
