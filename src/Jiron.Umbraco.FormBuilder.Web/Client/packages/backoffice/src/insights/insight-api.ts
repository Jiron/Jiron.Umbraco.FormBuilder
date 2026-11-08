// What the insights read and keep through the management API (ROADMAP 11.5 and 11.5b, ADR-0211, ADR-0212): a form's
// report over a span and the span it compares with, its answers' summaries over a span and the entries list's filter
// with the next free-text answers of a field, the site's table of forms, and the boxes the user shows as tables.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  insightAnswers,
  insightAnswerTexts,
  insightGlance,
  insightReport,
  insightTables,
  type EntryListFilterRequestModel,
  type InsightAnswersResponseModel,
  type InsightAnswerTextsResponseModel,
  type InsightGlanceItemResponseModel,
  type InsightReportResponseModel,
} from '../api';
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

/** A form's answers summarised per field, of the entries a span and a filter hold; undefined without an answer. */
export async function readAnswers(
  host: UmbClassInterface,
  form: string,
  span: Span,
  filter: EntryListFilterRequestModel,
): Promise<InsightAnswersResponseModel | undefined> {
  const { data } = await tryExecute(
    host,
    insightAnswers({ path: { key: form }, body: { from: span.from, to: span.to, filter } }),
    { disableNotifications: true },
  );
  return data;
}

/** A field's next free-text answers after where the ones shown end; undefined without an answer. */
export async function readAnswerTexts(
  host: UmbClassInterface,
  form: string,
  field: string,
  span: Span,
  filter: EntryListFilterRequestModel,
  after: string,
): Promise<InsightAnswerTextsResponseModel | undefined> {
  const { data } = await tryExecute(
    host,
    insightAnswerTexts({ path: { key: form, field }, body: { from: span.from, to: span.to, filter, after } }),
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
