// What the email layout screens ask the management API (ROADMAP 12.1, ADR-0215): the layouts, the site's languages and
// mail's own colours, one layout and its changes, the forms whose email actions use it, the preview of a sample entry,
// and a test mail of the saved layout.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  emailLayoutLibraryCreate,
  emailLayoutLibraryDelete,
  emailLayoutLibraryDuplicate,
  emailLayoutLibraryEditor,
  emailLayoutLibraryGet,
  emailLayoutLibraryPreview,
  emailLayoutLibraryRows,
  emailLayoutLibraryTest,
  emailLayoutLibraryUpdate,
  emailLayoutLibraryUsedBy,
  type EmailLayoutDesignRequestModel,
  type EmailLayoutDesignResponseModel,
  type EmailLayoutEditorResponseModel,
  type EmailLayoutPreviewResponseModel,
  type EmailLayoutResponseModel,
  type EmailLayoutRowsResponseModel,
  type LibraryUsersResponseModel,
} from '../api';
import {
  deleteAnswerOf,
  saveAnswerOf,
  type DeleteAnswer,
  type ReadAnswer,
  type SaveAnswer,
} from '../library/items/library-items';
import { codeOf } from '../library/library-changes';
import { EMAIL_LAYOUT_ENTITY_TYPE } from './constants';
import { COLOURS, type Colours, type EmailLayoutDesign, type EmailLayoutDetailModel } from './email-layout-model';

const quiet = { disableNotifications: true } as const;

const coloursOf = (colours: Record<string, string | null | undefined> | null | undefined): Colours =>
  Object.fromEntries(COLOURS.flatMap((name) => (colours?.[name] ? [[name, colours[name]]] : [])));

/** A design as the API answers with it. */
export const designOf = (design: EmailLayoutDesignResponseModel): EmailLayoutDesign => ({
  look: design.look === 'letter' ? 'letter' : 'card',
  logoMedia: design.logoMedia ?? null,
  logoUrl: design.logoUrl ?? null,
  logoAlt: { ...design.logoAlt },
  light: coloursOf(design.light),
  dark: coloursOf(design.dark),
  header: { ...design.header },
  footer: { ...design.footer },
});

/** A design as a save or the preview sends it. */
export const designBody = (design: EmailLayoutDesign): EmailLayoutDesignRequestModel => ({
  look: design.look,
  logoMedia: design.logoMedia,
  logoUrl: design.logoMedia ? null : design.logoUrl,
  logoAlt: design.logoAlt,
  light: design.light,
  dark: design.dark,
  header: design.header,
  footer: design.footer,
});

/** The workspace's model of a layout as the API answers with it. */
export const detailOf = (layout: EmailLayoutResponseModel): EmailLayoutDetailModel => ({
  entityType: EMAIL_LAYOUT_ENTITY_TYPE,
  unique: layout.key,
  name: layout.name,
  alias: layout.alias,
  design: designOf(layout.design),
  concurrencyVersion: layout.concurrencyVersion,
});

const valuesOf = (model: EmailLayoutDetailModel) => ({
  alias: model.alias,
  name: model.name.trim(),
  design: designBody(model.design),
});

const answerOf = (data: EmailLayoutResponseModel | undefined, error: unknown): SaveAnswer<EmailLayoutDetailModel> =>
  saveAnswerOf(data ? detailOf(data) : undefined, error);

/** A page of the layouts; 'forbidden' for a user without the library, undefined without an answer. */
export async function readRows(
  host: UmbClassInterface,
  skip: number,
  take: number,
): Promise<EmailLayoutRowsResponseModel | 'forbidden' | undefined> {
  const { data, error } = await tryExecute(host, emailLayoutLibraryRows({ query: { skip, take } }), quiet);
  return data ?? (codeOf(error) === 'forbidden' ? 'forbidden' : undefined);
}

let editor: Promise<EmailLayoutEditorResponseModel | undefined> | undefined;

/** The site's languages, mail's own colours and the site's default layout, read once while the backoffice is open. */
export function readEditor(host: UmbClassInterface): Promise<EmailLayoutEditorResponseModel | undefined> {
  editor ??= tryExecute(host, emailLayoutLibraryEditor(), quiet).then(({ data }) => {
    if (!data) editor = undefined;
    return data;
  });
  return editor;
}

/** A layout, or the refusal of its read. */
export async function readLayout(host: UmbClassInterface, key: string): Promise<ReadAnswer<EmailLayoutDetailModel>> {
  const { data, error } = await tryExecute(host, emailLayoutLibraryGet({ path: { key } }), quiet);
  return data ? { data: detailOf(data) } : { error };
}

/** Creates a layout under its key. */
export async function createLayout(host: UmbClassInterface, model: EmailLayoutDetailModel) {
  const { data, error } = await tryExecute(
    host,
    emailLayoutLibraryCreate({ body: { key: model.unique, layout: valuesOf(model) } }),
    quiet,
  );
  return answerOf(data, error);
}

/** Saves a layout, based on the version read. */
export async function updateLayout(host: UmbClassInterface, model: EmailLayoutDetailModel) {
  const { data, error } = await tryExecute(
    host,
    emailLayoutLibraryUpdate({
      path: { key: model.unique },
      body: { concurrencyVersion: model.concurrencyVersion, layout: valuesOf(model) },
    }),
    quiet,
  );
  return answerOf(data, error);
}

/** Deletes a layout, based on the version read. */
export async function deleteLayout(
  host: UmbClassInterface,
  key: string,
  concurrencyVersion: number,
): Promise<DeleteAnswer> {
  const { error } = await tryExecute(
    host,
    emailLayoutLibraryDelete({ path: { key }, query: { concurrencyVersion } }),
    quiet,
  );
  return deleteAnswerOf(error, 'email_layout_not_found');
}

/** A copy of a layout with the name given; undefined when it was refused. */
export async function duplicateLayout(host: UmbClassInterface, key: string, name: string) {
  const { data } = await tryExecute(host, emailLayoutLibraryDuplicate({ path: { key }, body: { name } }), quiet);
  return data ? detailOf(data) : undefined;
}

/** The forms whose email actions use a layout, as the user may see them; undefined without an answer. */
export async function readUsers(host: UmbClassInterface, key: string): Promise<LibraryUsersResponseModel | undefined> {
  const { data } = await tryExecute(host, emailLayoutLibraryUsedBy({ path: { key } }), quiet);
  return data;
}

/** A sample entry's mail in a design not saved yet, in a culture, or the problems; undefined without an answer. */
export async function previewLayout(
  host: UmbClassInterface,
  design: EmailLayoutDesign,
  culture: string,
  signal: AbortSignal,
): Promise<EmailLayoutPreviewResponseModel | undefined> {
  const { data } = await tryExecute(
    host,
    emailLayoutLibraryPreview({ body: { design: designBody(design), culture }, signal }),
    quiet,
  );
  return data;
}

/** A test mail of the saved layout to the user who asks: the address it goes to, or the refusal. */
export async function sendTest(
  host: UmbClassInterface,
  key: string,
  culture: string,
): Promise<{ readonly to?: string; readonly error?: unknown }> {
  const { data, error } = await tryExecute(host, emailLayoutLibraryTest({ path: { key }, body: { culture } }), quiet);
  return data ? { to: data.to } : { error };
}
