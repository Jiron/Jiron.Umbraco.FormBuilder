// The open form's designer as a site's extension gets it (ADR-0018): the public `JfbFormDesigner` over the form
// workspace's context. A change of texts is one of the designer's own edits, so Undo takes it back and the editor's save
// keeps it; a save is the workspace's, as Preview asks for it.
import type { JfbFormDesigner, JfbTextChange } from '../../public/types/jiron-form-builder';
import type { FormWorkspaceContext } from './form-workspace.context';
import { withTexts } from './text-changes';

/** The context's part the designer host uses. */
export type DesignerContext = Pick<
  FormWorkspaceContext,
  'getUnique' | 'getLanguage' | 'getEditorState' | 'getHasUnpersistedChanges' | 'requestSubmit' | 'edit'
>;

/** The designer of the open form. */
export function designerHost(context: DesignerContext): JfbFormDesigner {
  return {
    get formKey() {
      return context.getUnique() ?? '';
    },
    getLanguage: () => context.getLanguage() ?? '',
    getDefinition: () => structuredClone(context.getEditorState().definition),
    hasUnsavedChanges: () => context.getHasUnpersistedChanges(),
    async save() {
      if (!context.getHasUnpersistedChanges()) return true;
      try {
        await context.requestSubmit();
        return true;
      } catch {
        return false;
      }
    },
    changeTexts(changes: readonly JfbTextChange[], said: string) {
      const state = context.getEditorState();
      const definition = withTexts(state.definition, changes);
      if (!definition) return false;
      return context.edit({ ok: true, state: { definition, selection: state.selection } }, said).ok;
    },
  };
}
