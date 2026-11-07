// What the Export sidebar decides (ROADMAP 11.4, IA 6.4, ADR-0210), apart from drawing it: the choices it opens with, the
// fields a format offers, the request it sends, and the sentences that name the entries and the button. The server
// checks everything again; these only keep the sidebar from offering what it would refuse.
import type {
  EntryExportFormatResponseModel,
  EntryExportPlanResponseModel,
  EntryExportRequestModel,
  EntryExportStatusResponseModel,
  EntryExportViewResponseModel,
  EntryListFieldResponseModel,
  EntryListFilterRequestModel,
} from '../api';

/** A CSV file's delimiter, as the API names it. */
export type ExportDelimiter = 'comma' | 'semicolon' | 'tab';

/** How the columns are named. */
export type ExportHeaders = 'labels' | 'aliases';

/** The delimiters, in the order the sidebar offers them. */
export const DELIMITERS: readonly ExportDelimiter[] = ['comma', 'semicolon', 'tab'];

/** The headers, in the order the sidebar offers them. */
export const HEADERS: readonly ExportHeaders[] = ['labels', 'aliases'];

/** What the user chose: the format, the fields (null for every field), the sensitive fields and the options. */
export interface ExportChoices {
  readonly format: string;
  readonly fields: readonly string[] | null;
  readonly sensitive: boolean;
  readonly delimiter: ExportDelimiter;
  readonly headers: ExportHeaders;
  readonly byteOrderMark: boolean;
}

/** The choices of a user who never exported the form: the first format, every field, no sensitive one. */
export function defaultChoices(formats: readonly EntryExportFormatResponseModel[]): ExportChoices {
  return {
    format: formats[0]?.name ?? 'csv',
    fields: null,
    sensitive: false,
    delimiter: 'comma',
    headers: 'labels',
    byteOrderMark: true,
  };
}

const isDelimiter = (value: string): value is ExportDelimiter => (DELIMITERS as readonly string[]).includes(value);
const isHeaders = (value: string): value is ExportHeaders => (HEADERS as readonly string[]).includes(value);

/**
 * The choices the sidebar opens with: the user's last for this form, where the site still offers their format and they
 * may still see sensitive fields; otherwise the defaults for what no longer holds.
 */
export function choicesOf(view: EntryExportViewResponseModel): ExportChoices {
  const defaults = defaultChoices(view.formats);
  const last = view.choices;
  if (!last) return defaults;
  return {
    format: view.formats.some((format) => format.name === last.format) ? last.format : defaults.format,
    fields: last.fields ?? null,
    sensitive: last.sensitive && view.maySeeSensitive,
    delimiter: isDelimiter(last.options.delimiter) ? last.options.delimiter : defaults.delimiter,
    headers: isHeaders(last.options.headers) ? last.options.headers : defaults.headers,
    byteOrderMark: last.options.byteOrderMark,
  };
}

/** Whether a field is one of the form's own, not a field inside a repeatable group, which goes with its group. */
const topLevel = (field: EntryListFieldResponseModel): boolean => !field.parent;

/** Whether a field or a group's children hold files. */
function holdsFiles(field: EntryListFieldResponseModel, fields: readonly EntryListFieldResponseModel[]): boolean {
  return field.kind === 'file' || fields.some((child) => child.parent === field.key && child.kind === 'file');
}

/**
 * The fields a format offers: the form's own, in the list's order, the sensitive ones only when they go along; a format
 * of files offers the fields that hold files.
 */
export function fieldsFor(
  fields: readonly EntryListFieldResponseModel[],
  format: EntryExportFormatResponseModel | undefined,
  sensitive: boolean,
): EntryListFieldResponseModel[] {
  return fields.filter(
    (field) =>
      topLevel(field) && (sensitive || !field.sensitive) && (!format?.writesFiles || holdsFiles(field, fields)),
  );
}

/** Whether the form has a sensitive field that an export leaves out unless asked. */
export const hasSensitive = (fields: readonly EntryListFieldResponseModel[]): boolean =>
  fields.some((field) => topLevel(field) && field.sensitive);

/** The fields chosen among those offered: all of them for "All fields", otherwise those still offered, in their order. */
export function chosenFields(choices: ExportChoices, offered: readonly EntryListFieldResponseModel[]): string[] {
  if (choices.fields === null) return offered.map((field) => field.key);
  const chosen = new Set(choices.fields);
  return offered.filter((field) => chosen.has(field.key)).map((field) => field.key);
}

/** A list's filter as an export of its form takes it: the Inbox's Form filter names only that form. */
export function exportFilter(filter: EntryListFilterRequestModel, form: string): EntryListFilterRequestModel {
  return { ...filter, forms: filter.forms.filter((one) => one === form) };
}

/** What the sidebar sends: the format, the list's filter or the ticked entries, the fields and the options. */
export function requestOf(
  choices: ExportChoices,
  offered: readonly EntryListFieldResponseModel[],
  form: string,
  filter: EntryListFilterRequestModel | undefined,
  entries: readonly string[] | undefined,
): EntryExportRequestModel {
  return {
    format: choices.format,
    ...(entries && entries.length > 0
      ? { entries: [...entries] }
      : filter
        ? { filter: exportFilter(filter, form) }
        : {}),
    fields: choices.fields === null ? null : chosenFields(choices, offered),
    sensitive: choices.sensitive,
    options: { delimiter: choices.delimiter, headers: choices.headers, byteOrderMark: choices.byteOrderMark },
  };
}

/** Whether a filter narrows the list: a state, a time, a run, a field or a search. */
export function narrows(filter: EntryListFilterRequestModel | undefined): boolean {
  if (!filter) return false;
  return (
    filter.states.length > 0 ||
    !!filter.received ||
    !!filter.actions ||
    filter.fields.length > 0 ||
    !!filter.search?.trim()
  );
}

type Term = (key: string, ...args: unknown[]) => string;

/** The sentence that names the entries: the ticked ones, those the filters match, or all of them. */
export function entriesLine(
  term: Term,
  plan: EntryExportPlanResponseModel,
  ticked: boolean,
  filtered: boolean,
): string {
  if (ticked) return term('export_entriesTicked', plan.entries);
  if (plan.more) return term('export_entriesMore');
  return filtered ? term('export_entriesFiltered', plan.entries) : term('export_entriesAll', plan.entries);
}

/** The primary button: what happens, with the number of entries (copy.md: "Export 124 entries"). */
export function exportLabel(term: Term, format: string, plan: EntryExportPlanResponseModel | undefined): string {
  if (!plan) return term('export_start');
  if (plan.more) return term(format === 'print' ? 'export_printMore' : 'export_startMore');
  return term(format === 'print' ? 'export_printCount' : 'export_startCount', plan.entries);
}

/** A background export's state, as the API names it. */
export type ExportState = 'queued' | 'running' | 'ready' | 'failed';

/** Whether a background export has come to its end: ready or failed. */
export const isFinished = (status: EntryExportStatusResponseModel): boolean =>
  status.state === 'ready' || status.state === 'failed';

/** The sentence of a background export: how far it is, that it is ready, or that it failed. */
export function statusLine(term: Term, status: EntryExportStatusResponseModel): string {
  if (status.state === 'ready') return term('export_ready', status.done);
  if (status.state === 'failed') return term('export_failedOne', status.total);
  return status.done > 0
    ? term('export_progress', status.done, status.total, status.more)
    : term('export_preparing', status.total, status.more);
}
