// The form workspace (ROADMAP 9.3a, docs/ux/information-architecture.md section 5): Umbraco's named detail workspace
// over the draft endpoint, so its header edits the name, its footer saves, and leaving with unsaved changes asks first.
// It holds the editor model's state with its undo history: each edit changes the draft the workspace saves, and a load
// starts the history over, selecting the field a link names (`?field=`, ADR-0166).
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import {
  UmbEntityNamedDetailWorkspaceContextBase,
  type UmbRoutableWorkspaceContext,
} from '@umbraco-cms/backoffice/workspace';
import { FORMULAS } from '@jfb/logic/calc';
import { fieldTypeList, formGet, formPublish, type FieldTypeResponseModel } from '../api';
import { sayRefusal } from '../library/library-changes';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import { FORM_DETAIL_REPOSITORY_ALIAS, FORM_WORKSPACE_ALIAS } from './constants';
import { locate } from './definition';
import { select, type Edit, type EditorState, type FieldTraits, type ModelContext } from './editor-model';
import type { FormDetailModel } from './form-detail.data-source';
import { fieldOfQuery, formState, type FormState } from './form-state';
import { EditorHistory } from './history';

/** What the views draw: the editor's state and whether undo and redo can do anything. */
export interface EditorView {
  readonly state: EditorState;
  readonly canUndo: boolean;
  readonly canRedo: boolean;
}

const EMPTY: EditorState = { definition: { pages: [] }, selection: null };

/** The name guard's rule for who may not design the form. */
const NO_DESIGN = 'Jiron.FormBuilder.NameGuard.NoDesign';

/** What the editor model needs to know of a field type, from the field types endpoint. */
export function traitsOf(type: FieldTypeResponseModel): FieldTraits {
  return {
    kind: type.ruleKind ?? null,
    onePerForm: type.onePerForm,
    childFields: type.features.includes('childFields'),
  };
}

export class FormWorkspaceContext
  extends UmbEntityNamedDetailWorkspaceContextBase<FormDetailModel>
  implements UmbRoutableWorkspaceContext
{
  readonly #history = new EditorHistory(EMPTY);
  readonly #editor = new UmbObjectState<EditorView>({ state: EMPTY, canUndo: false, canRedo: false });
  /** The field the address named when the form was opened, until the load that selects it. */
  #wanted: string | null = null;
  /** Whether the field types were read, without which the model can check nothing. */
  #typesRead = false;
  // A record, not a Map: the state compares values as JSON, which writes every Map as {}.
  readonly #types = new UmbObjectState<Readonly<Record<string, FieldTypeResponseModel>>>({});

  /** The field types by alias, as the views name and draw them; empty until they are read. */
  readonly fieldTypes = this.#types.asObservable();

  /** What the editor model asks of the workspace: the field types, fresh keys and the calculation language. */
  readonly model: ModelContext = {
    traitsOf: (type) => {
      const types = this.#types.getValue();
      const found = Object.hasOwn(types, type) ? types[type] : undefined;
      return found ? traitsOf(found) : undefined;
    },
    newKey: () => crypto.randomUUID(),
    formulas: FORMULAS,
  };

  /** The editor's state, as the views draw it. */
  readonly editor = this.#editor.asObservable();

  /** The form as the last answer gave it. */
  readonly form = this._data.createObservablePartOfCurrent((data) => data?.form);

  /** The state chip's state. */
  readonly state = this._data.createObservablePartOfCurrent((data): FormState | undefined =>
    data ? formState(data.form, data.hasUnpublishedChanges) : undefined,
  );

  constructor(host: UmbControllerHost) {
    super(host, {
      workspaceAlias: FORM_WORKSPACE_ALIAS,
      entityType: FORM_ENTITY_TYPE,
      detailRepositoryAlias: FORM_DETAIL_REPOSITORY_ALIAS,
    });
    this.routes.setRoutes([
      {
        path: 'edit/:unique',
        component: () => import('./form-workspace-editor.element'),
        setup: async (_component, info) => {
          // Read before the router moves on to a view, which drops the query.
          const wanted = fieldOfQuery(window.location.search);
          const unique = info.match.params['unique'] ?? '';
          // The form open already is not read again: a link to one of its fields selects it here.
          if (unique === this.getUnique() && this.getData()) {
            if (wanted && locate(this.#history.state.definition, wanted)?.kind === 'field')
              this.edit(select(this.#history.state, wanted), '');
            return;
          }
          this.#wanted = wanted;
          await this.load(unique);
        },
      },
    ]);
    void this.#loadTypes();
  }

  async #loadTypes() {
    const { data } = await tryExecute(this, fieldTypeList(), { disableNotifications: true });
    if (!data) return;
    this.#types.setValue(Object.fromEntries(data.types.map((type) => [type.alias, type])));
    this.#typesRead = true;
  }

  /** Whether the user may design the open form. */
  canDesign(): boolean {
    return this.getData()?.form.permissions.includes('design') ?? false;
  }

  // Every load starts the history over, with the field a link named selected when the form has it, else what was
  // selected before when it is still there, as after a publish.
  protected override async _processIncomingData(data: FormDetailModel): Promise<FormDetailModel> {
    const kept = this.#wanted ?? this.#history.state.selection;
    this.#wanted = null;
    const selection = kept && locate(data.definition, kept) ? kept : null;
    this.#history.reset({ definition: data.definition, selection });
    this.#guardName(data);
    this.#publish();
    return data;
  }

  // Who may not design reads the name and can't change it; Umbraco's name field honours the guard.
  #guardName(data: FormDetailModel) {
    if (data.form.permissions.includes('design')) this.nameWriteGuard.removeRule(NO_DESIGN);
    else this.nameWriteGuard.addRule({ unique: NO_DESIGN, permitted: false });
  }

  /**
   * Saves, as Umbraco's base does, then puts the saved definition in the history's present place. When a save fails
   * after its rename went through, the form is read again, so the next save sends the version the rename gave.
   */
  override async submit(): Promise<void> {
    try {
      await super.submit();
    } catch (error) {
      await this.#refreshForm();
      throw error;
    }
    const saved = this.getData()?.definition;
    if (saved) this.#history.rebase(saved);
    this.#publish();
  }

  async #refreshForm() {
    const key = this.getUnique();
    if (!key) return;
    const { data: form } = await tryExecute(this, formGet({ path: { key } }), { disableNotifications: true });
    if (!form) return;
    this._data.updatePersisted({ form, name: form.name });
    this._data.updateCurrent({ form });
  }

  /**
   * Takes an edit of the editor model: a change to the definition goes into the draft and the undo history, a selection
   * only into the view. Returns the edit, whose refusal the caller says.
   */
  edit(edit: Edit, said: string): Edit {
    const before = this.#history.state.definition;
    // A change checked without the field types was checked against nothing: it waits for them.
    if (edit.ok && edit.state.definition !== before && !this.#typesRead) {
      void this.#loadTypes();
      return { ok: false, refusal: 'notReady' };
    }
    const result = this.#history.apply(edit, said);
    if (result.ok && result.state.definition !== before)
      this._data.updateCurrent({ definition: result.state.definition });
    this.#publish();
    return result;
  }

  /** Takes the last change back; what it was, or undefined with nothing to undo. */
  undo(): string | undefined {
    return this.#travel(this.#history.undo());
  }

  /** Does the last undone change again; what it was, or undefined with nothing to redo. */
  redo(): string | undefined {
    return this.#travel(this.#history.redo());
  }

  /** The editor's state now. */
  getEditorState(): EditorState {
    return this.#history.state;
  }

  /**
   * Publishes the form, saving its draft first when it has unsaved changes. True once it is published; a refusal is
   * said, the form check's problems among them.
   */
  async publish(comment: string | null): Promise<boolean> {
    if (this.getHasUnpersistedChanges()) {
      try {
        await this.requestSubmit();
      } catch {
        return false;
      }
    }

    const form = this.getData()?.form;
    if (!form) return false;
    const { data, error } = await tryExecute(
      this,
      formPublish({ path: { key: form.key }, body: { concurrencyVersion: form.concurrencyVersion, comment } }),
      { disableNotifications: true },
    );
    if (!data) {
      await sayRefusal(this, error);
      return false;
    }

    await this.reload();
    const localize = new UmbLocalizationController(this);
    (await this.getContext(UMB_NOTIFICATION_CONTEXT))?.peek('positive', {
      data: { message: localize.term('jfbDesigner_published', data.form.name) },
    });
    return true;
  }

  #travel(said: string | undefined): string | undefined {
    if (said !== undefined) this._data.updateCurrent({ definition: this.#history.state.definition });
    this.#publish();
    return said;
  }

  #publish() {
    this.#editor.setValue({
      state: this.#history.state,
      canUndo: this.#history.canUndo,
      canRedo: this.#history.canRedo,
    });
  }

  override destroy() {
    this.#editor.destroy();
    this.#types.destroy();
    super.destroy();
  }
}

export { FormWorkspaceContext as api };
