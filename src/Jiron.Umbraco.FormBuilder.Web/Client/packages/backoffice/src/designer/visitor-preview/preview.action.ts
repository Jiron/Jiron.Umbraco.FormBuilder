// "Preview" in the form workspace's footer (docs/ux/information-architecture.md 5.7, ADR-0203, ADR-0204): saves what is
// not saved yet, as Publish... does, since the preview shows the draft as it was saved; then opens the preview at the
// designer's width and in its language, offering Run actions for test entries to who may manage the form's actions. A
// save refused, a conflict among them, opens nothing, and says why itself. One preview at a time for a workspace.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { UmbWorkspaceActionBase, type UmbWorkspaceActionArgs } from '@umbraco-cms/backoffice/workspace';
import { actionTypeList } from '../../api';
import { actionLine, actionName } from '../actions/action-lines';
import { loadActionWords } from '../actions/action-words';
import { actionsOf } from '../actions/action-model';
import { readWidth } from '../canvas/widths';
import { FORM_WORKSPACE_CONTEXT } from '../constants';
import type { DesignDefinition } from '../definition';
import { readLook } from '../preview/look';
import { elementNames } from '../rules/rule-names';
import { PREVIEW_MODAL, type ActionLine } from './preview.modal.token';

// The workspaces whose preview is opening or open.
const open = new WeakSet<object>();

export class PreviewFormAction extends UmbWorkspaceActionBase {
  readonly #localize = new UmbLocalizationController(this);

  constructor(host: UmbControllerHost, args: UmbWorkspaceActionArgs<never>) {
    super(host, args);
    // The button waits only while the draft is saved.
    this.setExecuting(false);
  }

  override async execute() {
    const context = await this.getContext(FORM_WORKSPACE_CONTEXT);
    if (!context || open.has(context)) return;
    open.add(context);
    try {
      if (context.getHasUnpersistedChanges()) {
        this.setExecuting(true);
        try {
          await context.requestSubmit();
        } catch {
          return;
        } finally {
          this.setExecuting(false);
        }
      }
      const form = context.getUnique();
      if (!form) return;
      const culture = context.getLanguage();
      const look = await readLook(this, culture ?? undefined);
      // The draft as it was saved, which the preview shows and whose actions Run actions... lists.
      const definition = context.getEditorState().definition;
      await umbOpenModal(this, PREVIEW_MODAL, {
        data: {
          name: context.getName() ?? '',
          form,
          languages: look?.languages ?? [],
          culture,
          width: readWidth(),
          mayRunActions: context.getData()?.form.permissions.includes('manageActions') ?? false,
          read: (shown, ask) => context.visitorPreviewOf(shown, ask),
          ticket: (version, runActions) => context.previewTicketOf(version, runActions),
          testEntry: (submissionId) => context.testEntryOf(submissionId),
          runActions: (submissionId) => context.runTestEntryActions(submissionId),
          describe: (keys) => this.#describe(definition, look?.culture ?? '', keys),
        },
      }).catch(() => undefined);
    } finally {
      open.delete(context);
    }
  }

  // The actions by their keys, as the Actions view names them and says what each does, in its words, which load with it;
  // one the draft no longer holds by its key alone.
  async #describe(
    definition: DesignDefinition,
    culture: string,
    keys: readonly string[],
  ): Promise<readonly ActionLine[]> {
    const [{ data }] = await Promise.all([
      tryExecute(this, actionTypeList(), { disableNotifications: true }),
      loadActionWords(this.#localize.lang()).catch(() => undefined),
    ]);
    const words = { types: data?.types, localize: this.#localize };
    const names = elementNames(definition, culture, (key, ...args) => this.#localize.term(key, ...args));
    const actions = actionsOf(definition);
    return keys.map((key) => {
      const action = actions.find((one) => one.key === key);
      return action
        ? { name: actionName(words, action), line: actionLine(words, action, names) }
        : { name: key, line: '' };
    });
  }
}

export { PreviewFormAction as api };
