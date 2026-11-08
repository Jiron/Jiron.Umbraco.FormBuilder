// What the theme screens ask the management API (ROADMAP 12.1, ADR-0215): the themes, the base themes with their tokens,
// one theme and its changes, the forms that use it, its export and an import, the preview's layer and the sample page.
// Each answer comes back as what the screens say: a refusal by its code, the problems a save answered with by their
// paths.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  themeLibraryCreate,
  themeLibraryDelete,
  themeLibraryDuplicate,
  themeLibraryEditor,
  themeLibraryExport,
  themeLibraryGet,
  themeLibraryImport,
  themeLibraryPreview,
  themeLibraryRows,
  themeLibrarySample,
  themeLibraryUpdate,
  themeLibraryUsedBy,
  type LibraryUsersResponseModel,
  type ThemeEditorResponseModel,
  type ThemeExportResponseModel,
  type ThemePreviewResponseModel,
  type ThemeResponseModel,
  type ThemeRowsResponseModel,
  type ThemeSampleResponseModel,
} from '../api';
import {
  deleteAnswerOf,
  problemsOf,
  saveAnswerOf,
  type DeleteAnswer,
  type ReadAnswer,
  type SaveAnswer,
} from '../library/items/library-items';
import { codeOf } from '../library/library-changes';
import { THEME_ENTITY_TYPE } from './constants';
import type { ThemeDetailModel } from './theme-model';

const quiet = { disableNotifications: true } as const;

/** The workspace's model of a theme as the API answers with it. */
export const detailOf = (theme: ThemeResponseModel): ThemeDetailModel => ({
  entityType: THEME_ENTITY_TYPE,
  unique: theme.key,
  name: theme.name,
  alias: theme.alias,
  baseTheme: theme.baseTheme,
  tokens: { light: { ...theme.tokens.light }, dark: { ...theme.tokens.dark } },
  customCss: theme.customCss ?? '',
  concurrencyVersion: theme.concurrencyVersion,
});

/** A theme's values as a save sends them. */
const valuesOf = (model: ThemeDetailModel) => ({
  alias: model.alias,
  name: model.name.trim(),
  baseTheme: model.baseTheme,
  tokens: model.tokens,
  customCss: model.customCss.trim() === '' ? null : model.customCss,
});

const answerOf = (data: ThemeResponseModel | undefined, error: unknown): SaveAnswer<ThemeDetailModel> =>
  saveAnswerOf(data ? detailOf(data) : undefined, error);

/** A page of the themes; 'forbidden' for a user without the library, undefined without an answer. */
export async function readRows(
  host: UmbClassInterface,
  skip: number,
  take: number,
): Promise<ThemeRowsResponseModel | 'forbidden' | undefined> {
  const { data, error } = await tryExecute(host, themeLibraryRows({ query: { skip, take } }), quiet);
  return data ?? (codeOf(error) === 'forbidden' ? 'forbidden' : undefined);
}

let editor: Promise<ThemeEditorResponseModel | undefined> | undefined;

/** The base themes with their tokens and the site's default, read once while the backoffice is open. */
export function readEditor(host: UmbClassInterface): Promise<ThemeEditorResponseModel | undefined> {
  editor ??= tryExecute(host, themeLibraryEditor(), quiet).then(({ data }) => {
    if (!data) editor = undefined;
    return data;
  });
  return editor;
}

/** A theme, or the refusal of its read. */
export async function readTheme(host: UmbClassInterface, key: string): Promise<ReadAnswer<ThemeDetailModel>> {
  const { data, error } = await tryExecute(host, themeLibraryGet({ path: { key } }), quiet);
  return data ? { data: detailOf(data) } : { error };
}

/** Creates a theme, under the key of its alias. */
export async function createTheme(host: UmbClassInterface, model: ThemeDetailModel) {
  const { data, error } = await tryExecute(host, themeLibraryCreate({ body: { theme: valuesOf(model) } }), quiet);
  return answerOf(data, error);
}

/** Saves a theme, based on the version read. */
export async function updateTheme(host: UmbClassInterface, model: ThemeDetailModel) {
  const { data, error } = await tryExecute(
    host,
    themeLibraryUpdate({
      path: { key: model.unique },
      body: { concurrencyVersion: model.concurrencyVersion, theme: valuesOf(model) },
    }),
    quiet,
  );
  return answerOf(data, error);
}

/** Deletes a theme, based on the version read. */
export async function deleteTheme(
  host: UmbClassInterface,
  key: string,
  concurrencyVersion: number,
): Promise<DeleteAnswer> {
  const { error } = await tryExecute(host, themeLibraryDelete({ path: { key }, query: { concurrencyVersion } }), quiet);
  return deleteAnswerOf(error, 'theme_not_found');
}

/** A copy of a theme with the name given; undefined when it was refused. */
export async function duplicateTheme(host: UmbClassInterface, key: string, name: string) {
  const { data } = await tryExecute(host, themeLibraryDuplicate({ path: { key }, body: { name } }), quiet);
  return data ? detailOf(data) : undefined;
}

/** The forms that use a theme, as the user may see them; undefined without an answer. */
export async function readUsers(host: UmbClassInterface, key: string): Promise<LibraryUsersResponseModel | undefined> {
  const { data } = await tryExecute(host, themeLibraryUsedBy({ path: { key } }), quiet);
  return data;
}

/** A theme's export: its file's name and document; undefined without an answer. */
export async function exportTheme(host: UmbClassInterface, key: string): Promise<ThemeExportResponseModel | undefined> {
  const { data } = await tryExecute(host, themeLibraryExport({ path: { key } }), quiet);
  return data;
}

/** An import's new theme, or what refused the document. */
export async function importTheme(
  host: UmbClassInterface,
  document: string,
): Promise<{ readonly theme?: ThemeDetailModel; readonly error?: unknown }> {
  const { data, error } = await tryExecute(host, themeLibraryImport({ body: { document } }), quiet);
  return data ? { theme: detailOf(data) } : { error };
}

/** The layer the preview applies, what the sanitiser took out and the problems; undefined without an answer. */
export async function previewTheme(
  host: UmbClassInterface,
  model: Pick<ThemeDetailModel, 'baseTheme' | 'tokens' | 'customCss'>,
  signal: AbortSignal,
): Promise<ThemePreviewResponseModel | undefined> {
  const { data } = await tryExecute(
    host,
    themeLibraryPreview({
      body: { baseTheme: model.baseTheme, tokens: model.tokens, customCss: model.customCss },
      signal,
    }),
    quiet,
  );
  return data;
}

/** The sample page in a base theme and a culture; undefined without an answer. */
export async function readSample(
  host: UmbClassInterface,
  baseTheme: string,
  culture: string,
): Promise<ThemeSampleResponseModel | undefined> {
  const { data } = await tryExecute(host, themeLibrarySample({ query: { baseTheme, culture } }), quiet);
  return data;
}

export { problemsOf };
