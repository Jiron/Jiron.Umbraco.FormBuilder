// What Activity > Deliveries and Activity > Audit log read and do (ROADMAP 12.4b, ADR-0219): a page of each list, a
// delivery's details and its retry, and the log's export; a refusal comes back as what the screen says.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  auditExport,
  auditList,
  deliveryGetEmail,
  deliveryGetRun,
  deliveryList,
  deliveryRetryEmail,
  deliveryRetryRun,
  type AuditListResponseModel,
  type DeliveryDetailResponseModel,
  type DeliveryListResponseModel,
} from '../api';
import { codeOf } from '../library/library-changes';

const quiet = { disableNotifications: true } as const;

/** A list's answer: the page, 'forbidden' without the permission, 'tooLong' past the read budget, or undefined. */
export type ListAnswer<T> = T | 'forbidden' | 'tooLong' | undefined;

function refusalOf(error: unknown): 'forbidden' | 'tooLong' | undefined {
  const code = codeOf(error);
  return code === 'forbidden' ? 'forbidden' : code === 'query_timeout' ? 'tooLong' : undefined;
}

/** The filters of Deliveries, as the screen holds them; an empty one is left out. */
export interface DeliveryFilters {
  readonly states: '' | 'problems' | 'waiting' | 'delivered' | 'skipped';
  readonly kind: '' | 'email' | 'webhook' | 'other';
  readonly days: number;
  readonly search: string;
}

/** A page of the deliveries log. */
export async function readDeliveries(
  host: UmbClassInterface,
  filters: DeliveryFilters,
  skip: number,
  take: number,
): Promise<ListAnswer<DeliveryListResponseModel>> {
  const { data, error } = await tryExecute(
    host,
    deliveryList({
      query: {
        ...(filters.states ? { states: filters.states } : {}),
        ...(filters.kind ? { kind: filters.kind } : {}),
        ...(filters.days > 0 ? { days: filters.days } : {}),
        ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
        skip,
        take,
      },
    }),
    quiet,
  );
  return data ?? refusalOf(error);
}

/** A mail or a run of the log. */
export type DeliveryKind = 'email' | 'run';

/** A delivery's details; 'gone' when the log no longer holds it, or undefined without an answer. */
export async function readDelivery(
  host: UmbClassInterface,
  kind: DeliveryKind,
  id: number,
): Promise<DeliveryDetailResponseModel | 'gone' | undefined> {
  const { data, error } = await tryExecute(
    host,
    kind === 'email' ? deliveryGetEmail({ path: { id } }) : deliveryGetRun({ path: { id } }),
    quiet,
  );
  if (data) return data;
  return codeOf(error) === 'delivery_not_found' ? 'gone' : undefined;
}

/** How a retry ended: queued again, a confirmation it needs, changed meanwhile, refused, gone, or no answer. */
export type RetryAnswer =
  | { readonly kind: 'queued'; readonly detail: DeliveryDetailResponseModel }
  | { readonly kind: 'needsConfirmation' | 'changed' | 'forbidden' | 'gone' | 'failed' };

/** Retries a failed or uncertain delivery; an uncertain one only with the confirmation. */
export async function retryDelivery(
  host: UmbClassInterface,
  kind: DeliveryKind,
  id: number,
  confirmed: boolean,
): Promise<RetryAnswer> {
  const body = { confirmed };
  const { data, error } = await tryExecute(
    host,
    kind === 'email' ? deliveryRetryEmail({ path: { id }, body }) : deliveryRetryRun({ path: { id }, body }),
    quiet,
  );
  if (data) return { kind: 'queued', detail: data };
  switch (codeOf(error)) {
    case 'retry_needs_confirmation':
      return { kind: 'needsConfirmation' };
    case 'run_not_retryable':
    case 'concurrency_conflict':
      return { kind: 'changed' };
    case 'forbidden':
      return { kind: 'forbidden' };
    case 'delivery_not_found':
      return { kind: 'gone' };
    default:
      return { kind: 'failed' };
  }
}

/** The filters of the audit log, as the screen holds them; an empty one is left out. */
export interface AuditFilters {
  readonly what: '' | 'forms' | 'entries' | 'library' | 'settings';
  readonly user: string;
  readonly days: number;
  readonly search: string;
}

const auditQuery = (filters: AuditFilters) => ({
  ...(filters.what ? { what: filters.what } : {}),
  ...(filters.user ? { user: filters.user } : {}),
  ...(filters.days > 0 ? { days: filters.days } : {}),
  ...(filters.search.trim() ? { search: filters.search.trim() } : {}),
});

/** A page of the audit log. */
export async function readAudit(
  host: UmbClassInterface,
  filters: AuditFilters,
  skip: number,
  take: number,
): Promise<ListAnswer<AuditListResponseModel>> {
  const { data, error } = await tryExecute(host, auditList({ query: { ...auditQuery(filters), skip, take } }), quiet);
  return data ?? refusalOf(error);
}

/** The filtered log as a CSV file, or undefined when it couldn't be written. */
export async function exportAudit(host: UmbClassInterface, filters: AuditFilters): Promise<Blob | undefined> {
  const { data } = await tryExecute(host, auditExport({ query: auditQuery(filters), parseAs: 'blob' }), quiet);
  return data instanceof Blob ? data : undefined;
}
