// The forms the workspace has read (ROADMAP 9.3a): Umbraco's detail repository keeps its answers in a store, and reads
// wait for one, so the workspace needs it even though one workspace shows one form.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { UmbDetailStoreBase } from '@umbraco-cms/backoffice/store';
import { FORM_DETAIL_STORE_CONTEXT } from './constants';
import type { FormDetailModel } from './form-detail.data-source';

export class FormDetailStore extends UmbDetailStoreBase<FormDetailModel> {
  constructor(host: UmbControllerHost) {
    super(host, FORM_DETAIL_STORE_CONTEXT.toString());
  }
}

export { FormDetailStore as api };
