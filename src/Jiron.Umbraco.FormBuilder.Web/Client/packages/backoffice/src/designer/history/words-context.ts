// What a comparison in words needs (ROADMAP 10.3a and 10.3c, ADR-0196, ADR-0200): the dictionaries, the site's languages
// and default texts, its field and action types, and the names of the pages a redirect leads to. History's comparisons
// and Review both versions read it alike.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { UmbDocumentItemRepository } from '@umbraco-cms/backoffice/document';
import type { UmbLocalizationController } from '@umbraco-cms/backoffice/localization-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { actionTypeList, type ActionTypeResponseModel, type FieldTypeResponseModel } from '../../api';
import type { DesignDefinition } from '../definition';
import { readLook, word } from '../preview/look';
import { targetOf, type DesignOutcome } from '../settings/outcome-model';
import type { ChangeWordsContext } from './change-words';

/** The site's action types, read once per backoffice session; undefined when they can't be read, and tried again. */
let actionTypes: Promise<Readonly<Record<string, ActionTypeResponseModel>> | undefined> | undefined;

/** The context that words a comparison of two definitions, the older first. */
export async function wordsContextOf(
  host: UmbClassInterface,
  localize: UmbLocalizationController,
  fieldTypes: Readonly<Record<string, FieldTypeResponseModel>>,
  older: DesignDefinition,
  newer: DesignDefinition,
): Promise<ChangeWordsContext> {
  actionTypes ??= tryExecute(host, actionTypeList(), { disableNotifications: true }).then(({ data }) => {
    if (!data) actionTypes = undefined;
    return data ? Object.fromEntries(data.types.map((type) => [type.alias, type])) : undefined;
  });
  const look = await readLook(host);
  const culture = look?.languages.find((language) => language.isDefault)?.culture ?? look?.culture ?? '';
  const term = (key: string, ...args: unknown[]) => localize.term(key, ...args);
  return {
    term,
    say: (text) => (text ? localize.string(text) : ''),
    culture,
    locale: localize.lang(),
    languages: look?.languages ?? [],
    fieldTypes,
    actionTypes: (await actionTypes) ?? {},
    older,
    newer,
    pages: await pagesOf(host, [older, newer], culture),
    defaultMessage: look ? word(look, 'thankYou') : '',
  };
}

/** The names of the pages the definitions' redirects lead to, in the site's default language where a page has it. */
async function pagesOf(
  host: UmbClassInterface,
  definitions: readonly DesignDefinition[],
  culture: string,
): Promise<ReadonlyMap<string, string>> {
  const keys = [
    ...new Set(
      definitions.flatMap((definition) =>
        ((definition['outcomes'] ?? []) as readonly DesignOutcome[])
          .map((outcome) => targetOf(outcome))
          .flatMap((target) => (target.kind === 'page' ? [target.key] : [])),
      ),
    ),
  ];
  const names = new Map<string, string>();
  if (keys.length === 0) return names;
  // A page that can't be read is said as "a page of the site".
  const { data } = await new UmbDocumentItemRepository(host).requestItems(keys).catch(() => ({ data: undefined }));
  const wanted = culture.toLowerCase();
  for (const item of data ?? []) {
    const variant =
      item.variants.find((one) => one.culture?.toLowerCase() === wanted) ??
      item.variants.find((one) => !one.culture) ??
      item.variants[0];
    if (variant?.name) names.set(item.unique, variant.name);
  }
  return names;
}
