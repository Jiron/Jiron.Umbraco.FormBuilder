// How a library item's workspace reads and saves it (ROADMAP 12.1, ADR-0214, ADR-0215): Umbraco's detail data source
// over the kind's endpoints. A save that its values refuse hands the problems to its host element first, as Umbraco's
// workspace passes no save's error on, and the editor shows each at its place; a change someone made meanwhile is said.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import type { UmbDataSourceResponse, UmbDetailDataSource } from '@umbraco-cms/backoffice/repository';
import { sayRefusal } from '../library-changes';
import type { DeleteAnswer, LibraryItemModel, LibraryProblem, ReadAnswer, SaveAnswer } from './library-items';

/** The event a refused save sends its host element first, with the problems at their paths. */
export const SAVE_PROBLEMS = 'jfb:library-item-problems';

/** A refused save's problems, as the event carries them. */
export class SaveProblemsEvent extends Event {
  constructor(readonly problems: readonly LibraryProblem[]) {
    super(SAVE_PROBLEMS);
  }
}

export abstract class LibraryItemDataSource<T extends LibraryItemModel>
  extends UmbControllerBase
  implements UmbDetailDataSource<T>
{
  /** A new item with a key of its own from the start, so a preview and a save name the same item. */
  protected abstract scaffold(key: string, preset?: Partial<T>): T;
  protected abstract readItem(key: string): Promise<ReadAnswer<T>>;
  protected abstract createItem(model: T): Promise<SaveAnswer<T>>;
  protected abstract updateItem(model: T): Promise<SaveAnswer<T>>;
  protected abstract deleteItem(key: string, concurrencyVersion: number): Promise<DeleteAnswer>;

  createScaffold(preset?: Partial<T>): Promise<UmbDataSourceResponse<T>> {
    return Promise.resolve({ data: { ...this.scaffold(crypto.randomUUID(), preset), ...preset } });
  }

  async read(unique: string): Promise<UmbDataSourceResponse<T>> {
    const { data, error } = await this.readItem(unique);
    return data ? { data } : { error: error as Error };
  }

  async create(model: T): Promise<UmbDataSourceResponse<T>> {
    return this.#answer(await this.createItem(model));
  }

  async update(model: T): Promise<UmbDataSourceResponse<T>> {
    return this.#answer(await this.updateItem(model));
  }

  async delete(unique: string): Promise<UmbDataSourceResponse<unknown>> {
    const read = await this.readItem(unique);
    if (!read.data) return { error: read.error as Error };
    const answer = await this.deleteItem(unique, read.data.concurrencyVersion);
    return answer === 'deleted' ? {} : { error: new Error(answer) };
  }

  async #answer(answer: SaveAnswer<T>): Promise<UmbDataSourceResponse<T>> {
    switch (answer.kind) {
      case 'saved':
        this.getHostElement().dispatchEvent(new SaveProblemsEvent([]));
        return { data: answer.item };
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
