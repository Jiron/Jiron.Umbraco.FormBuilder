// The Forms tab of Umbraco's header search (docs/ux/information-architecture.md, section 10; ADR-0165, ADR-0166): the forms the text
// names, by name or alias, with their folder; then the fields across all forms by label or alias ("Email, in Contact us"), from
// the field catalogue. A form's result opens the form, a field's the form with that field selected. The server decides what
// matches and what the user sees.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type { UmbSearchProvider, UmbSearchRequestArgs, UmbSearchResultItemModel } from '@umbraco-cms/backoffice/search';
import { formSearch, formSearchFields, type FieldMatchResponseModel, type LibraryItemResponseModel } from '../api';
import { formPath, FORM_ENTITY_TYPE, screenPath } from '../section/entity-types';

/** How many forms, and how many fields, the tab shows at most when it does not say. */
const TAKE = 20;

/** The words a result's name needs: "%name%, in %place%". */
export type InWords = (name: string, place: string) => string;

/** A form as the search lists it: its name, and its folder when it is in one. */
export function resultOf(form: LibraryItemResponseModel, inWords: InWords): UmbSearchResultItemModel {
  return {
    unique: form.key,
    entityType: FORM_ENTITY_TYPE,
    name: form.folderName ? inWords(form.name, form.folderName) : form.name,
    icon: 'icon-document',
    href: screenPath(FORM_ENTITY_TYPE, form.key),
  };
}

/** A field as the search lists it: its label in its form, linking to the form with the field selected. */
export function fieldResultOf(field: FieldMatchResponseModel, inWords: InWords): UmbSearchResultItemModel {
  return {
    unique: field.fieldKey,
    entityType: FORM_ENTITY_TYPE,
    name: inWords(field.label || field.alias, field.formName),
    icon: 'icon-autofill',
    href: formPath(field.formKey, { field: field.fieldKey }),
  };
}

export class FormSearchProvider extends UmbControllerBase implements UmbSearchProvider<UmbSearchResultItemModel> {
  readonly #localize = new UmbLocalizationController(this);

  async search(args: UmbSearchRequestArgs) {
    const text = args.query.trim();
    if (!text) return { data: { items: [], total: 0 } };
    const take = args.paging?.take ?? TAKE;
    const skip = args.paging?.skip ?? 0;
    const [forms, fields] = await Promise.all([
      tryExecute(this, formSearch({ query: { text, skip, take } }), { disableNotifications: true }),
      tryExecute(this, formSearchFields({ query: { text, skip, take } }), { disableNotifications: true }),
    ]);
    if (!forms.data || !fields.data) return { error: (forms.error ?? fields.error) as Error };
    const inWords: InWords = (name, place) => this.#localize.term('jfbLibrary_inPlace', name, place);
    return {
      data: {
        items: [
          ...forms.data.items.map((form) => resultOf(form, inWords)),
          ...fields.data.items.map((field) => fieldResultOf(field, inWords)),
        ],
        total: forms.data.total + fields.data.total,
      },
    };
  }
}

export { FormSearchProvider as api };
