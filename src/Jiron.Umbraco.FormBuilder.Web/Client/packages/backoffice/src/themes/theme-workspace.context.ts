// The theme workspace (ROADMAP 12.1, IA 7, ADR-0215): the library's workspace over the theme endpoints. A new theme is
// made by its create route on the base theme the create dialog chose; its view edits the tokens and the custom CSS.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { LibraryItemWorkspaceContext } from '../library/items/library-item-workspace';
import { THEME_DETAIL_REPOSITORY_ALIAS, THEME_WORKSPACE_ALIAS } from './constants';
import { THEME_KIND } from './theme-kind';
import type { ThemeDetailModel, ThemeTokens } from './theme-model';

export class ThemeWorkspaceContext extends LibraryItemWorkspaceContext<ThemeDetailModel> {
  /** What the view edits: the base theme, the tokens and the custom CSS. */
  readonly theme = this._data.createObservablePartOfCurrent((data) =>
    data ? { baseTheme: data.baseTheme, tokens: data.tokens, customCss: data.customCss } : undefined,
  );

  constructor(host: UmbControllerHost) {
    super(host, {
      workspaceAlias: THEME_WORKSPACE_ALIAS,
      detailRepositoryAlias: THEME_DETAIL_REPOSITORY_ALIAS,
      kind: THEME_KIND,
      createParam: 'baseTheme',
    });
  }

  setTokens(tokens: ThemeTokens) {
    this.change({ tokens });
  }

  setCustomCss(customCss: string) {
    this.change({ customCss });
  }
}

export { ThemeWorkspaceContext as api };
