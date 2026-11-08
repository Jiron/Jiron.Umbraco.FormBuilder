// The option list workspace's repository (ROADMAP 12.1, ADR-0214): Umbraco's detail repository over the list endpoints.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailRepositoryBase } from '@umbraco-cms/backoffice/repository';
import { OPTION_LIST_DETAIL_STORE_CONTEXT } from './constants';
import { OptionListDetailDataSource } from './option-list-detail.data-source';
import type { OptionListDetailModel } from './option-list-model';

export class OptionListDetailRepository extends UmbDetailRepositoryBase<OptionListDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, OptionListDetailDataSource, OPTION_LIST_DETAIL_STORE_CONTEXT);
  }
}

export { OptionListDetailRepository as api };
