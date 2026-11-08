// The option list workspace (ROADMAP 12.1, IA 7, ADR-0214): Umbraco's named detail workspace over the list endpoints, so
// its header edits the name, its footer saves, its "..." holds the list's entity actions, and leaving with unsaved
// changes asks first. A new list is made by its create route with the kind the create dialog chose, and the workspace
// moves to the list's own address once it is saved. It holds what a refused save said, by place, and the forms that use
// the list, read again after every load and save.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbEntityModel } from '@umbraco-cms/backoffice/entity';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import type { UmbRouterSlotElement } from '@umbraco-cms/backoffice/router';
import {
  UmbEntityNamedDetailWorkspaceContextBase,
  UmbWorkspaceIsNewRedirectController,
  type UmbRoutableWorkspaceContext,
} from '@umbraco-cms/backoffice/workspace';
import type { OptionListUsersResponseModel } from '../api';
import { loadWorkspaceWords } from '../designer/workspace-words';
import {
  OPTION_LIST_DETAIL_REPOSITORY_ALIAS,
  OPTION_LIST_ENTITY_TYPE,
  OPTION_LIST_WORKSPACE_ALIAS,
  OPTION_LISTS_ENTITY_TYPE,
} from './constants';
import { SAVE_PROBLEMS, SaveProblemsEvent } from './option-list-detail.data-source';
import type { ListProblem, OptionListDetailModel } from './option-list-model';
import { loadOptionListWords } from './option-list-words';
import { readUsers } from './option-lists-api';

export class OptionListWorkspaceContext
  extends UmbEntityNamedDetailWorkspaceContextBase<OptionListDetailModel>
  implements UmbRoutableWorkspaceContext
{
  readonly #problems = new UmbObjectState<readonly ListProblem[]>([]);
  readonly #users = new UmbObjectState<OptionListUsersResponseModel | undefined>(undefined);

  /** What the last refused save said, each at its path; empty after a save that went through. */
  readonly problems = this.#problems.asObservable();

  /** The forms that use the list, as the user may see them; undefined for a new list or while they are read. */
  readonly users = this.#users.asObservable();

  readonly alias = this._data.createObservablePartOfCurrent((data) => data?.alias ?? '');
  readonly kind = this._data.createObservablePartOfCurrent((data) => data?.kind ?? null);
  readonly contents = this._data.createObservablePartOfCurrent((data) =>
    data ? { kind: data.kind, config: data.config, items: data.items } : undefined,
  );
  readonly cacheSeconds = this._data.createObservablePartOfCurrent((data) => data?.cacheSeconds ?? 0);

  /** The editor's words, and the inspector's whose option editor it shows, before anything of it draws (ADR-0201). */
  readonly #editor = async () => {
    const lang = new UmbLocalizationController(this).lang();
    await Promise.all([loadOptionListWords(lang), loadWorkspaceWords(lang)]).catch(() => undefined);
    return import('./option-list-workspace-editor.element');
  };

  readonly #onProblems = (event: Event) => {
    if (event instanceof SaveProblemsEvent) this.#problems.setValue(event.problems);
  };

  constructor(host: UmbControllerHost) {
    super(host, {
      workspaceAlias: OPTION_LIST_WORKSPACE_ALIAS,
      entityType: OPTION_LIST_ENTITY_TYPE,
      detailRepositoryAlias: OPTION_LIST_DETAIL_REPOSITORY_ALIAS,
    });
    this.routes.setRoutes([
      {
        path: 'create/:kind',
        component: this.#editor,
        setup: async (_component, info) => {
          this.#problems.setValue([]);
          this.#users.setValue(undefined);
          await this.createScaffold({
            parent: { entityType: OPTION_LISTS_ENTITY_TYPE, unique: null },
            preset: { kind: info.match.params['kind'] as OptionListDetailModel['kind'] },
          });
          // Saved, the new list moves to its own address, as Umbraco's own create routes do.
          const router = this.getHostElement().shadowRoot?.querySelector<UmbRouterSlotElement>('umb-router-slot');
          if (router) new UmbWorkspaceIsNewRedirectController(this, this, router);
        },
      },
      {
        path: 'edit/:unique',
        component: this.#editor,
        setup: async (_component, info) => {
          this.#problems.setValue([]);
          await this.load(info.match.params['unique'] ?? '');
        },
      },
    ]);
    this.getHostElement().addEventListener(SAVE_PROBLEMS, this.#onProblems);
  }

  protected override async _processIncomingData(data: OptionListDetailModel): Promise<OptionListDetailModel> {
    void this.#readUsers(data.unique);
    return data;
  }

  protected override async _create(currentData: OptionListDetailModel, parent: UmbEntityModel): Promise<void> {
    await super._create(currentData, parent);
    void this.#readUsers(currentData.unique);
  }

  protected override async _update(currentData: OptionListDetailModel): Promise<void> {
    await super._update(currentData);
    void this.#readUsers(currentData.unique);
  }

  async #readUsers(key: string) {
    if (this.getIsNew()) return;
    const users = await readUsers(this, key);
    if (key === this.getUnique()) this.#users.setValue(users);
  }

  /** The forms that use the list, read again. */
  refreshUsers(): Promise<void> {
    const key = this.getUnique();
    return key ? this.#readUsers(key) : Promise.resolve();
  }

  setAlias(alias: string) {
    this._data.updateCurrent({ alias });
  }

  /** The kind's configuration and the items, as the option editor changed them. */
  setContents(contents: Pick<OptionListDetailModel, 'config' | 'items'>) {
    this._data.updateCurrent(contents);
  }

  setCacheSeconds(cacheSeconds: number) {
    this._data.updateCurrent({ cacheSeconds });
  }

  /** Whether the list is saved and holds what was saved: what a URL list's Fetch now fetches. */
  isSavedAsShown(): boolean {
    return !this.getIsNew() && !this.getHasUnpersistedChanges();
  }

  override destroy(): void {
    this.getHostElement()?.removeEventListener(SAVE_PROBLEMS, this.#onProblems);
    this.#problems.destroy();
    this.#users.destroy();
    super.destroy();
  }
}

export { OptionListWorkspaceContext as api };
