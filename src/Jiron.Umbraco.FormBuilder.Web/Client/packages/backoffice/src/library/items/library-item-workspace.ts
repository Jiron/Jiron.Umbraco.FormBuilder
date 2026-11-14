// A library item's workspace (ROADMAP 12.1, IA 7, ADR-0214, ADR-0215): Umbraco's named detail workspace over the kind's
// endpoints, so its header edits the name, its footer saves, its "..." holds the item's entity actions, and leaving with
// unsaved changes asks first. A new item is made by its create route, whose parameter, if any, the scaffold takes, and
// the workspace moves to the item's own address once it is saved. It holds what a refused save said, by place, and the
// forms that use the item, read again after every load and save.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import type { UmbEntityModel } from '@umbraco-cms/backoffice/entity';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import type { UmbRouterSlotElement } from '@umbraco-cms/backoffice/router';
import type { UmbDeepPartialObject } from '@umbraco-cms/backoffice/utils';
import {
  UmbEntityNamedDetailWorkspaceContextBase,
  UmbWorkspaceIsNewRedirectController,
  type UmbRoutableWorkspaceContext,
} from '@umbraco-cms/backoffice/workspace';
import type { LibraryUsersResponseModel } from '../../api';
import { SAVE_PROBLEMS, SaveProblemsEvent } from './library-item-data-source';
import type { LibraryItemModel, LibraryKind, LibraryProblem } from './library-items';
import { commitTyping } from '../../ui/commit-typing';

/** What a kind's workspace says of itself. */
export interface LibraryWorkspaceArgs<T extends LibraryItemModel> {
  readonly workspaceAlias: string;
  readonly detailRepositoryAlias: string;
  readonly kind: LibraryKind<T>;
  /** The create route's parameter, such as `kind`, which the scaffold takes as the preset of the same name. */
  readonly createParam?: keyof T & string;
  /** Loads what the editor needs beyond the kind's words, before it draws. */
  readonly prepare?: (lang: string) => Promise<unknown>;
}

export class LibraryItemWorkspaceContext<T extends LibraryItemModel>
  extends UmbEntityNamedDetailWorkspaceContextBase<T>
  implements UmbRoutableWorkspaceContext
{
  readonly #problems = new UmbObjectState<readonly LibraryProblem[]>([]);
  readonly #users = new UmbObjectState<LibraryUsersResponseModel | undefined>(undefined);

  /** The kind, whose words, addresses and counts the header takes. */
  readonly libraryKind: LibraryKind<T>;

  /** What the last refused save said, each at its path; empty after a save that went through. */
  readonly problems = this.#problems.asObservable();

  /** The forms that use the item, as the user may see them; undefined for a new item or while they are read. */
  readonly users = this.#users.asObservable();

  readonly alias = this._data.createObservablePartOfCurrent((data) => data?.alias ?? '');

  readonly #onProblems = (event: Event) => {
    if (event instanceof SaveProblemsEvent) this.#problems.setValue(event.problems);
  };

  constructor(host: UmbControllerHost, args: LibraryWorkspaceArgs<T>) {
    super(host, {
      workspaceAlias: args.workspaceAlias,
      entityType: args.kind.entityType,
      detailRepositoryAlias: args.detailRepositoryAlias,
    });
    this.libraryKind = args.kind;
    // The editor's words, and what else it needs, before anything of it draws (ADR-0201).
    const editor = async () => {
      const lang = new UmbLocalizationController(this).lang();
      await Promise.all([args.kind.load(lang), args.prepare?.(lang)]).catch(() => undefined);
      return import('./library-item-editor.element');
    };
    const param = args.createParam;
    this.routes.setRoutes([
      {
        path: param ? `create/:${param}` : 'create',
        component: editor,
        setup: async (_component, info) => {
          this.#problems.setValue([]);
          this.#users.setValue(undefined);
          await this.createScaffold({
            parent: { entityType: args.kind.listEntityType, unique: null },
            preset: (param ? { [param]: info.match.params[param] } : {}) as UmbDeepPartialObject<T>,
          });
          // Saved, the new item moves to its own address, as Umbraco's own create routes do.
          const router = this.getHostElement().shadowRoot?.querySelector<UmbRouterSlotElement>('umb-router-slot');
          if (router) new UmbWorkspaceIsNewRedirectController(this, this, router);
        },
      },
      {
        path: 'edit/:unique',
        component: editor,
        setup: async (_component, info) => {
          this.#problems.setValue([]);
          await this.load(info.match.params['unique'] ?? '');
        },
      },
    ]);
    this.getHostElement().addEventListener(SAVE_PROBLEMS, this.#onProblems);
  }

  /** Saves the item with the text still being typed in a box that tells of it only once it is left, as at Ctrl+S. */
  override async requestSubmit(): Promise<void> {
    commitTyping();
    return super.requestSubmit();
  }

  protected override async _processIncomingData(data: T): Promise<T> {
    void this.#readUsers(data.unique);
    return data;
  }

  protected override async _create(currentData: T, parent: UmbEntityModel): Promise<void> {
    await super._create(currentData, parent);
    // The saved item's key, which a theme takes from its alias.
    const key = this.getUnique();
    if (key) void this.#readUsers(key);
  }

  protected override async _update(currentData: T): Promise<void> {
    await super._update(currentData);
    void this.#readUsers(currentData.unique);
  }

  async #readUsers(key: string) {
    // Only a saved item has users: a new one's scaffold comes in before the workspace marks it new.
    if (this.getIsNew() !== false) return;
    const users = await this.libraryKind.users(this, key);
    if (key === this.getUnique()) this.#users.setValue(users);
  }

  /** The forms that use the item, read again. */
  refreshUsers(): Promise<void> {
    const key = this.getUnique();
    return key ? this.#readUsers(key) : Promise.resolve();
  }

  setAlias(alias: string) {
    this._data.updateCurrent({ alias } as Partial<T>);
  }

  /** Changes some of the item's values, as its view edits them. */
  change(values: Partial<T>) {
    this._data.updateCurrent(values);
  }

  /** Whether the item is saved and holds what was saved. */
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
