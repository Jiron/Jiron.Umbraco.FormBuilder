// "Save as template..." on a form (docs/ux/information-architecture.md, section 3; ADR-0217): the dialog asks the
// template's name, the form's own to start with, and a description; the form's saved draft becomes a template of the
// gallery. In the form's workspace with changes not saved yet, the dialog says those stay out; from the tree there is
// nothing unsaved to say.
import { UmbEntityActionBase } from '@umbraco-cms/backoffice/entity-action';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { formGet, templateSaveForm } from '../../api';
import { FORM_WORKSPACE_CONTEXT } from '../../designer/constants';
import { say } from '../items/library-items';
import { sayRefusal } from '../library-changes';
import { TEMPLATE_NAME_MODAL } from '../modals/tokens';

/** The form's workspace, as far as the dialog asks it. */
interface Workspace {
  getUnique(): string | null | undefined;
  getHasUnpersistedChanges(): boolean;
}

/** Whether the dialog says that changes not saved yet stay out: in this form's own workspace, while it has some. */
export function holdsUnsaved(workspace: Workspace | undefined, key: string): boolean {
  return workspace?.getUnique() === key && workspace.getHasUnpersistedChanges();
}

export class SaveAsTemplateAction extends UmbEntityActionBase<never> {
  override async execute() {
    const key = this.args.unique;
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }));
    if (!form) return;
    // The tree has no workspace around it, which the request learns within a frame.
    const unsaved = holdsUnsaved(await this.getContext(FORM_WORKSPACE_CONTEXT).catch(() => undefined), key);
    const localize = new UmbLocalizationController(this);
    const value = await umbOpenModal(this, TEMPLATE_NAME_MODAL, {
      data: {
        headline: localize.term('jfbLibrary_saveAsTemplateHeadline', form.name),
        confirmLabel: localize.term('jfbLibrary_saveTemplate'),
        name: form.name,
        ...(unsaved ? { note: localize.term('jfbLibrary_saveAsTemplateNote') } : {}),
      },
    });
    const { data, error } = await tryExecute(
      this,
      templateSaveForm({ path: { formKey: key }, body: { name: value.name, description: value.description } }),
      { disableNotifications: true },
    );
    if (!data) {
      await sayRefusal(this, error);
      return;
    }

    await say(this, 'positive', localize.term('jfbLibrary_savedAsTemplate', data.name));
  }
}

export { SaveAsTemplateAction as api };
