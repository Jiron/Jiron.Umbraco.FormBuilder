// The ways to a new form (docs/ux/information-architecture.md, section 4; ADR-0166): blank, from the template gallery, or from a
// file. Each lands in the folder the "+" was on, or at the top of Forms, and opens the new form's screen.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formCreate } from '../../api';
import { FORM_ENTITY_TYPE, screenPath } from '../../section/entity-types';
import { changed, open, placeOf, sayRefusal } from '../library-changes';
import { IMPORT_MODAL, TEMPLATE_GALLERY_MODAL } from '../modals/tokens';

/** Creates a blank form, named for the editor to change at once, and opens it. */
export async function createBlankForm(host: UmbClassInterface, folderKey: string | null): Promise<string | undefined> {
  const localize = new UmbLocalizationController(host);
  const { data: form, error } = await tryExecute(
    host,
    formCreate({ body: { name: localize.term('jfbLibrary_untitledForm'), folderKey } }),
    {
      disableNotifications: true,
    },
  );
  if (!form) {
    await sayRefusal(host, error);
    return undefined;
  }

  await opened(host, form.key, folderKey);
  return form.key;
}

/** Opens the template gallery; the form it creates opens. */
export async function createFromTemplate(
  host: UmbClassInterface,
  folderKey: string | null,
): Promise<string | undefined> {
  const { formKey } = await umbOpenModal(host, TEMPLATE_GALLERY_MODAL, { data: { folderKey } });
  await opened(host, formKey, folderKey);
  return formKey;
}

/** Opens the import sidebar; the form it creates opens. */
export async function importForm(host: UmbClassInterface, folderKey: string | null): Promise<string | undefined> {
  const { formKey } = await umbOpenModal(host, IMPORT_MODAL, { data: { folderKey } });
  await opened(host, formKey, folderKey);
  return formKey;
}

async function opened(host: UmbClassInterface, formKey: string, folderKey: string | null) {
  await changed(host, placeOf(folderKey));
  open(screenPath(FORM_ENTITY_TYPE, formKey));
}
