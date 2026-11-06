// How the form workspace reads and saves a form (ROADMAP 9.3a): the draft endpoint's answer as Umbraco's detail
// workspace takes it, and a save that renames the form when its name changed and saves the draft when its definition
// did, each with the concurrency version the last answer gave, so that a change someone made meanwhile is refused.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbDataSourceResponse, UmbDetailDataSource } from '@umbraco-cms/backoffice/repository';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  formDraft,
  formGet,
  formRename,
  formSaveDraft,
  type DefinitionProblemResponseModel,
  type FormDraftResponseModel,
  type FormResponseModel,
} from '../api';
import { sayRefusal } from '../library/library-changes';
import { FORM_ENTITY_TYPE } from '../section/entity-types';
import { locate, normalise, type DesignDefinition } from './definition';

/** A form as its workspace holds it: the form, its draft's definition, and what the draft says beside it. */
export interface FormDetailModel {
  readonly entityType: string;
  readonly unique: string;
  readonly name: string;
  /** The form as the last answer gave it: its concurrency version, state and the user's permissions on it. */
  readonly form: FormResponseModel;
  readonly definition: DesignDefinition;
  /** What the server's check says of the draft. */
  readonly problems: readonly DefinitionProblemResponseModel[];
  /** The draft's version number, which History compares a version with (ADR-0196); null without the draft. */
  readonly draftVersion: number | null;
  readonly publishedVersion: number | null;
  readonly hasUnpublishedChanges: boolean;
  /** Whether the form has entries, so a field's alias changed asks first and keeps the one before (section 4.3.1). */
  readonly hasEntries: boolean;
}

/**
 * The alias a field is saved with when entries may name it (section 4.3.1): the form has entries and the field is in
 * the saved draft, with an alias. Changing it away from that alias asks first and keeps it for merge tags; null for a
 * field whose alias no entry knows yet.
 */
export function keptAlias(saved: FormDetailModel | undefined, key: string): string | null {
  if (!saved?.hasEntries) return null;
  const found = locate(saved.definition, key);
  return found?.kind === 'field' && found.element.alias ? found.element.alias : null;
}

/** The workspace's model of a draft endpoint's answer. */
export function detailOf(draft: FormDraftResponseModel): FormDetailModel {
  return {
    entityType: FORM_ENTITY_TYPE,
    unique: draft.form.key,
    name: draft.form.name,
    form: draft.form,
    definition: normalise(draft.definition),
    problems: draft.problems,
    draftVersion: draft.draftVersion,
    publishedVersion: draft.publishedVersion ?? null,
    hasUnpublishedChanges: draft.hasUnpublishedChanges,
    hasEntries: draft.hasEntries,
  };
}

/** The workspace's model of a form whose draft the user may not read: the form alone, and no definition. */
export function detailOfForm(form: FormResponseModel): FormDetailModel {
  return {
    entityType: FORM_ENTITY_TYPE,
    unique: form.key,
    name: form.name,
    form,
    definition: { pages: [] },
    problems: [],
    draftVersion: null,
    publishedVersion: null,
    hasUnpublishedChanges: false,
    hasEntries: false,
  };
}

/** What a save sends: a new name, a new definition, or both; nothing for neither. */
export function changesOf(
  saved: FormDraftResponseModel,
  model: FormDetailModel,
): { name?: string; definition?: DesignDefinition } {
  const name = model.name.trim();
  return {
    ...(name !== '' && name !== saved.form.name ? { name } : {}),
    ...(JSON.stringify(model.definition) !== JSON.stringify(normalise(saved.definition))
      ? { definition: model.definition }
      : {}),
  };
}

const notHere = (what: string): UmbDataSourceResponse<never> => ({
  error: new Error(`A form is ${what} in the forms tree, not in its workspace.`),
});

// A controller of its host, so that it can say a refusal through the host's notifications.
export class FormDetailDataSource extends UmbControllerBase implements UmbDetailDataSource<FormDetailModel> {
  // The draft is the designer's; who may not design gets the form alone, which is all the views they see need.
  async read(unique: string): Promise<UmbDataSourceResponse<FormDetailModel>> {
    const { data: form, error: unread } = await tryExecute(this, formGet({ path: { key: unique } }), {
      disableNotifications: true,
    });
    if (!form) return { error: unread as Error };
    if (!form.permissions.includes('design')) return { data: detailOfForm(form) };
    const { data, error } = await tryExecute(this, formDraft({ path: { key: unique } }), {
      disableNotifications: true,
    });
    return data ? { data: detailOf(data) } : { error: error as Error };
  }

  // A save by who may not design (Ctrl+S works for everyone) has nothing to send; nor has a save of what did not change.
  async update(model: FormDetailModel): Promise<UmbDataSourceResponse<FormDetailModel>> {
    if (!model.form.permissions.includes('design')) return { data: model };
    const key = model.unique;
    const { data: saved, error: unread } = await tryExecute(this, formDraft({ path: { key } }), {
      disableNotifications: true,
    });
    if (!saved) return this.#refused(unread);

    const changes = changesOf(saved, model);
    if (changes.name === undefined && changes.definition === undefined) return { data: detailOf(saved) };
    let version = model.form.concurrencyVersion;
    if (changes.name !== undefined) {
      const { data, error } = await tryExecute(
        this,
        formRename({ path: { key }, body: { concurrencyVersion: version, name: changes.name } }),
        { disableNotifications: true },
      );
      if (!data) return this.#refused(error);
      version = data.concurrencyVersion;
    }
    if (changes.definition !== undefined) {
      const { data, error } = await tryExecute(
        this,
        formSaveDraft({ path: { key }, body: { concurrencyVersion: version, definition: changes.definition } }),
        { disableNotifications: true },
      );
      if (!data) return this.#refused(error);
    }
    return this.read(key);
  }

  createScaffold(): Promise<UmbDataSourceResponse<FormDetailModel>> {
    return Promise.resolve(notHere('created'));
  }

  create(): Promise<UmbDataSourceResponse<FormDetailModel>> {
    return Promise.resolve(notHere('created'));
  }

  delete(): Promise<UmbDataSourceResponse<unknown>> {
    return Promise.resolve(notHere('trashed'));
  }

  // A refusal is said in the user's words (a change someone made meanwhile, a permission), then passed on.
  async #refused(error: unknown): Promise<UmbDataSourceResponse<FormDetailModel>> {
    await sayRefusal(this, error);
    return { error: error as Error };
  }
}
