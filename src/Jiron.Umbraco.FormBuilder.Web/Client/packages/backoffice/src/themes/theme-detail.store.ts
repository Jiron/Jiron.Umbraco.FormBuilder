// The themes the workspace has read (ROADMAP 12.1, ADR-0215): Umbraco's detail repository keeps its answers in a store.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailStoreBase } from '@umbraco-cms/backoffice/store';
import { THEME_DETAIL_STORE_CONTEXT } from './constants';
import type { ThemeDetailModel } from './theme-model';

export class ThemeDetailStore extends UmbDetailStoreBase<ThemeDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, THEME_DETAIL_STORE_CONTEXT.toString());
  }
}

export { ThemeDetailStore as api };
