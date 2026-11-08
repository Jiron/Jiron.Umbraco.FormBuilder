// The theme workspace's repository (ROADMAP 12.1, ADR-0215): Umbraco's detail repository over the theme endpoints.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailRepositoryBase } from '@umbraco-cms/backoffice/repository';
import { THEME_DETAIL_STORE_CONTEXT } from './constants';
import { ThemeDetailDataSource } from './theme-detail.data-source';
import type { ThemeDetailModel } from './theme-model';

export class ThemeDetailRepository extends UmbDetailRepositoryBase<ThemeDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, ThemeDetailDataSource, THEME_DETAIL_STORE_CONTEXT);
  }
}

export { ThemeDetailRepository as api };
