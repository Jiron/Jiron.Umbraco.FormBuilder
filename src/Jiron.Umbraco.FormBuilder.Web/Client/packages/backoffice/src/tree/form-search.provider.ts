// The Forms tab of Umbraco's header search (ADR-0165): forms by name and alias, as the user sees them; a result is a link to the
// form. The server decides what matches, and step 9.2 adds fields across forms.
import { UmbControllerBase } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type { UmbSearchProvider, UmbSearchRequestArgs, UmbSearchResultItemModel } from '@umbraco-cms/backoffice/search';
import { formSearch, type LibraryItemResponseModel } from '../api';
import { FORM_ENTITY_TYPE, SECTION_PATHNAME } from '../section/entity-types';

/** How many results the tab shows at most when it does not say. */
const TAKE = 20;

/** A form as the search lists it. */
export function resultOf(form: LibraryItemResponseModel): UmbSearchResultItemModel {
  return {
    unique: form.key,
    entityType: FORM_ENTITY_TYPE,
    name: form.alias ? `${form.name} (${form.alias})` : form.name,
    icon: 'icon-document',
    href: `section/${SECTION_PATHNAME}/workspace/${FORM_ENTITY_TYPE}/edit/${form.key}`,
  };
}

export class FormSearchProvider extends UmbControllerBase implements UmbSearchProvider<UmbSearchResultItemModel> {
  async search(args: UmbSearchRequestArgs) {
    const text = args.query.trim();
    if (!text) return { data: { items: [], total: 0 } };
    const { data, error } = await tryExecute(
      this,
      formSearch({ query: { text, skip: args.paging?.skip ?? 0, take: args.paging?.take ?? TAKE } }),
      { disableNotifications: true },
    );
    return error || !data
      ? { error: error as Error }
      : { data: { items: data.items.map(resultOf), total: data.total } };
  }
}

export { FormSearchProvider as api };
