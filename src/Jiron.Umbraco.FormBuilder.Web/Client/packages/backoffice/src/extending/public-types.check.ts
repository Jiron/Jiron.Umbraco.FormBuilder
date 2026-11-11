// What the shipped declarations promise, held to what runs (ROADMAP 15.1, ADR-0233): the typecheck fails when the
// form workspace's context, the field view model `<jfb-form>` hands a control's writer, or the element's writer stop
// fitting the types extension authors compile against. Nothing here runs.
import type { ControlWriter, CustomField } from '@jfb/element/views';
import type { JfbControlField, JfbControlWriter, JfbFormWorkspaceContext } from '../../public/types/jiron-form-builder';
import type { FormWorkspaceContext } from '../designer/form-workspace.context';

/** The workspace context a site's view reads is the form workspace's own. */
export type WorkspaceFits = FormWorkspaceContext extends JfbFormWorkspaceContext ? true : never;
export const workspaceFits: WorkspaceFits = true;

/** Every field `<jfb-form>` hands a writer, a site's own type's, is a field the declarations describe. */
export const fieldFits = (field: CustomField): JfbControlField => field;

/** A writer a site declares is one the element calls. */
export const writerFits = (write: JfbControlWriter): ControlWriter => write;
