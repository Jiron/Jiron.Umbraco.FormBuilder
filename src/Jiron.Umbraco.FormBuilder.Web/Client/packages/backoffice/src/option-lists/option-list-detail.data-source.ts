// How the option list workspace reads and saves a list (ROADMAP 12.1, ADR-0214): Umbraco's detail data source over the
// list endpoints. A new list's scaffold holds the kind its create route names, its key from the start, so a preview and a
// save name the same list. A save that its values refuse hands the problems to its host element first, as Umbraco's
// workspace passes no save's error on, and the editor shows each at its place; a change someone made meanwhile is said.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbDataSourceResponse, UmbDetailDataSource } from '@umbraco-cms/backoffice/repository';
import { sayRefusal } from '../library/library-changes';
import { OPTION_LIST_ENTITY_TYPE } from './constants';
import { isKind, keepOfNew, type ListProblem, type OptionListDetailModel } from './option-list-model';
import { createList, deleteList, readList, updateList, type SaveAnswer } from './option-lists-api';

/** The event a refused save sends its host element first, with the problems at their paths. */
export const SAVE_PROBLEMS = 'jfb:option-list-problems';

/** A refused save's problems, as the event carries them. */
export class SaveProblemsEvent extends Event {
  constructor(readonly problems: readonly ListProblem[]) {
    super(SAVE_PROBLEMS);
  }
}

/** The new list a create route's scaffold holds: its kind, a key of its own, and the kind's keep time. */
export function scaffoldOf(kind: string, key: string): OptionListDetailModel {
  const known = isKind(kind) ? kind : 'manual';
  return {
    entityType: OPTION_LIST_ENTITY_TYPE,
    unique: key,
    name: '',
    alias: '',
    kind: known,
    config: {},
    items: [],
    cacheSeconds: keepOfNew(known),
    concurrencyVersion: 0,
  };
}

export class OptionListDetailDataSource
  extends UmbControllerBase
  implements UmbDetailDataSource<OptionListDetailModel>
{
  createScaffold(preset?: Partial<OptionListDetailModel>): Promise<UmbDataSourceResponse<OptionListDetailModel>> {
    return Promise.resolve({ data: { ...scaffoldOf(preset?.kind ?? 'manual', crypto.randomUUID()), ...preset } });
  }

  async read(unique: string): Promise<UmbDataSourceResponse<OptionListDetailModel>> {
    const { data, error } = await readList(this, unique);
    return data ? { data } : { error: error as Error };
  }

  async create(model: OptionListDetailModel): Promise<UmbDataSourceResponse<OptionListDetailModel>> {
    return this.#answer(await createList(this, model));
  }

  async update(model: OptionListDetailModel): Promise<UmbDataSourceResponse<OptionListDetailModel>> {
    return this.#answer(await updateList(this, model));
  }

  async delete(unique: string): Promise<UmbDataSourceResponse<unknown>> {
    const read = await readList(this, unique);
    if (!read.data) return { error: read.error as Error };
    const answer = await deleteList(this, unique, read.data.concurrencyVersion);
    return answer === 'deleted' ? {} : { error: new Error(answer) };
  }

  async #answer(answer: SaveAnswer): Promise<UmbDataSourceResponse<OptionListDetailModel>> {
    switch (answer.kind) {
      case 'saved':
        this.getHostElement().dispatchEvent(new SaveProblemsEvent([]));
        return { data: answer.list };
      case 'invalid':
        this.getHostElement().dispatchEvent(new SaveProblemsEvent(answer.problems));
        return { error: new Error(SAVE_PROBLEMS) };
      case 'conflict':
      case 'failed':
        await sayRefusal(this, answer.error);
        return { error: new Error(answer.kind) };
    }
  }
}

export { OptionListDetailDataSource as api };
