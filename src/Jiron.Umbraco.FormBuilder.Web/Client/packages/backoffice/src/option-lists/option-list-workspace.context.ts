// The option list workspace (ROADMAP 12.1, IA 7, ADR-0214): the library's workspace over the list endpoints. A new list
// is made by its create route with the kind the create dialog chose; its editor shows the inspector's option editor, so
// the form workspace's words load with the list's.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { loadWorkspaceWords } from '../designer/workspace-words';
import { LibraryItemWorkspaceContext } from '../library/items/library-item-workspace';
import { OPTION_LIST_DETAIL_REPOSITORY_ALIAS, OPTION_LIST_WORKSPACE_ALIAS } from './constants';
import type { OptionListDetailModel } from './option-list-model';
import { OPTION_LIST_KIND } from './option-list-kind';

export class OptionListWorkspaceContext extends LibraryItemWorkspaceContext<OptionListDetailModel> {
  readonly kind = this._data.createObservablePartOfCurrent((data) => data?.kind ?? null);
  readonly contents = this._data.createObservablePartOfCurrent((data) =>
    data ? { kind: data.kind, config: data.config, items: data.items } : undefined,
  );
  readonly cacheSeconds = this._data.createObservablePartOfCurrent((data) => data?.cacheSeconds ?? 0);

  constructor(host: UmbControllerHost) {
    super(host, {
      workspaceAlias: OPTION_LIST_WORKSPACE_ALIAS,
      detailRepositoryAlias: OPTION_LIST_DETAIL_REPOSITORY_ALIAS,
      kind: OPTION_LIST_KIND,
      createParam: 'kind',
      prepare: loadWorkspaceWords,
    });
  }

  /** The kind's configuration and the items, as the option editor changed them. */
  setContents(contents: Pick<OptionListDetailModel, 'config' | 'items'>) {
    this.change(contents);
  }

  setCacheSeconds(cacheSeconds: number) {
    this.change({ cacheSeconds });
  }
}

export { OptionListWorkspaceContext as api };
