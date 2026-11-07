// What the insights read and keep through the management API (ROADMAP 11.5, ADR-0211): a form's report over a span and
// the span it compares with, the site's table of forms, the boxes the user shows as tables, and the site's switch on
// Privacy > Data collection, whose refusal comes back as what the screen says.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  insightGlance,
  insightReport,
  insightTables,
  privacySettingsGet,
  privacySettingsSave,
  type InsightGlanceItemResponseModel,
  type InsightReportResponseModel,
  type PrivacySettingsResponseModel,
} from '../api';
import { codeOf } from '../library/library-changes';
import type { Grouping, Span } from './insight-model';

/** A form's report; undefined without an answer. */
export async function readReport(
  host: UmbClassInterface,
  form: string,
  span: Span,
  groupBy: Grouping,
  compare: Span | null,
): Promise<InsightReportResponseModel | undefined> {
  const { data } = await tryExecute(
    host,
    insightReport({
      path: { key: form },
      query: {
        from: span.from,
        to: span.to,
        groupBy,
        ...(compare ? { compareFrom: compare.from, compareTo: compare.to } : {}),
      },
    }),
    { disableNotifications: true },
  );
  return data;
}

/** The site's table of forms over a span, compared with another; undefined without an answer. */
export async function readGlance(
  host: UmbClassInterface,
  span: Span,
  compare: Span,
): Promise<readonly InsightGlanceItemResponseModel[] | undefined> {
  const { data } = await tryExecute(
    host,
    insightGlance({ query: { from: span.from, to: span.to, compareFrom: compare.from, compareTo: compare.to } }),
    { disableNotifications: true },
  );
  return data?.items;
}

/** Keeps the boxes the user shows as tables; false when they weren't kept. */
export async function keepTables(host: UmbClassInterface, tables: readonly string[]): Promise<boolean> {
  const { data } = await tryExecute(host, insightTables({ body: { tables: [...tables] } }), {
    disableNotifications: true,
  });
  return data !== undefined;
}

/** How a save of the site's switch ended. */
export type PrivacySave =
  | { readonly kind: 'saved'; readonly settings: PrivacySettingsResponseModel }
  | { readonly kind: 'conflict' }
  | { readonly kind: 'locked' }
  | { readonly kind: 'failed' };

/** The privacy settings; 'forbidden' for a user who may not manage settings, or undefined without an answer. */
export async function readPrivacy(
  host: UmbClassInterface,
): Promise<PrivacySettingsResponseModel | 'forbidden' | undefined> {
  const { data, error } = await tryExecute(host, privacySettingsGet(), { disableNotifications: true });
  if (data) return data;
  return codeOf(error) === 'forbidden' ? 'forbidden' : undefined;
}

/** Saves the site's switch with the version read. */
export async function savePrivacy(host: UmbClassInterface, version: number, insights: boolean): Promise<PrivacySave> {
  const { data, error } = await tryExecute(host, privacySettingsSave({ body: { version, insights } }), {
    disableNotifications: true,
  });
  if (data) return { kind: 'saved', settings: data };
  switch (codeOf(error)) {
    case 'concurrency_conflict':
      return { kind: 'conflict' };
    case 'setting_locked':
      return { kind: 'locked' };
    default:
      return { kind: 'failed' };
  }
}
