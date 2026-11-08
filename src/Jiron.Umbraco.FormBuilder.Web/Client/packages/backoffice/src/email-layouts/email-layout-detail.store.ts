// The email layouts the workspace has read (ROADMAP 12.1, ADR-0215): Umbraco's detail repository keeps its answers here.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailStoreBase } from '@umbraco-cms/backoffice/store';
import { EMAIL_LAYOUT_DETAIL_STORE_CONTEXT } from './constants';
import type { EmailLayoutDetailModel } from './email-layout-model';

export class EmailLayoutDetailStore extends UmbDetailStoreBase<EmailLayoutDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, EMAIL_LAYOUT_DETAIL_STORE_CONTEXT.toString());
  }
}

export { EmailLayoutDetailStore as api };
