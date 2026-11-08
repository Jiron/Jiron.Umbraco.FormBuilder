// A delivery's details (ROADMAP 12.4b, IA 9, ADR-0219): its token, what it is opened with and what it answers.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { DeliveryKind } from './activity-api';

export const DELIVERY_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Delivery';

/** The mail or run whose details the sidebar shows. */
export interface DeliveryModalData {
  readonly kind: DeliveryKind;
  readonly id: number;
}

/** Whether the sidebar queued the delivery again, so the list reads it again. */
export interface DeliveryModalValue {
  readonly retried: boolean;
}

export const DELIVERY_MODAL = new UmbModalToken<DeliveryModalData, DeliveryModalValue>(DELIVERY_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});
