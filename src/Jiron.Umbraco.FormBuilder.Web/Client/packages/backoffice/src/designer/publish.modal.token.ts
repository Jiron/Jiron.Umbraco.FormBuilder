// The publish dialog's token (ROADMAP 9.3a).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';

export const PUBLISH_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Publish';

/** The form to publish, by the name its header shows. */
export interface PublishModalData {
  name: string;
}

/** The comment for the version; empty for none. */
export interface PublishModalValue {
  comment: string;
}

export const PUBLISH_MODAL = new UmbModalToken<PublishModalData, PublishModalValue>(PUBLISH_MODAL_ALIAS, {
  modal: { type: 'dialog', size: 'small' },
});
