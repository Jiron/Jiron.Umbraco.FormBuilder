// The email layout workspace's repository (ROADMAP 12.1, ADR-0215): Umbraco's detail repository over the endpoints.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailRepositoryBase } from '@umbraco-cms/backoffice/repository';
import { EMAIL_LAYOUT_DETAIL_STORE_CONTEXT } from './constants';
import { EmailLayoutDetailDataSource } from './email-layout-detail.data-source';
import type { EmailLayoutDetailModel } from './email-layout-model';

export class EmailLayoutDetailRepository extends UmbDetailRepositoryBase<EmailLayoutDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, EmailLayoutDetailDataSource, EMAIL_LAYOUT_DETAIL_STORE_CONTEXT);
  }
}

export { EmailLayoutDetailRepository as api };
