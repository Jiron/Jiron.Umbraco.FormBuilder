// The option lists the workspace has read (ROADMAP 12.1, ADR-0214): Umbraco's detail repository keeps its answers in a
// store, as the form workspace's does.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailStoreBase } from '@umbraco-cms/backoffice/store';
import { OPTION_LIST_DETAIL_STORE_CONTEXT } from './constants';
import type { OptionListDetailModel } from './option-list-model';

export class OptionListDetailStore extends UmbDetailStoreBase<OptionListDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, OPTION_LIST_DETAIL_STORE_CONTEXT.toString());
  }
}

export { OptionListDetailStore as api };
