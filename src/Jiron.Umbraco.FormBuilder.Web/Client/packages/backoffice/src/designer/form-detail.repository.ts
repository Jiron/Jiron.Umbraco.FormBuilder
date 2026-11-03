// The form workspace's repository (ROADMAP 9.3a): Umbraco's detail repository over the draft endpoint.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailRepositoryBase } from '@umbraco-cms/backoffice/repository';
import { FORM_DETAIL_STORE_CONTEXT } from './constants';
import { FormDetailDataSource, type FormDetailModel } from './form-detail.data-source';

export class FormDetailRepository extends UmbDetailRepositoryBase<FormDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, FormDetailDataSource, FORM_DETAIL_STORE_CONTEXT);
  }
}

export { FormDetailRepository as api };
