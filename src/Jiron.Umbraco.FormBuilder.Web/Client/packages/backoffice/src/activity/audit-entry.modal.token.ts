// An action's details in the audit log (ROADMAP 12.4b, IA 9, ADR-0219): its token, what it is opened with and what it
// answers.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { AuditEntryResponseModel } from '../api';
import type { AuditNames } from './audit-words';

export const AUDIT_ENTRY_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.AuditEntry';

/** The action, with the names its page holds, so the sidebar reads nothing again. */
export interface AuditEntryModalData {
  readonly entry: AuditEntryResponseModel;
  readonly names: AuditNames;
}

/** The person whose actions alone the log should show, when the sidebar asked for that. */
export interface AuditEntryModalValue {
  readonly user?: { readonly key: string; readonly name: string };
}

export const AUDIT_ENTRY_MODAL = new UmbModalToken<AuditEntryModalData, AuditEntryModalValue>(AUDIT_ENTRY_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});
