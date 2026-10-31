// What the form picker reads (ADR-0133, ADR-0134): the library level by level, a search and the forms of given keys,
// through the generated client, within a data type's limits when its key is known. Each call answers undefined when
// the request fails, and shows no notification: the element that asked says so in place, once.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { pickerChildren, pickerForms, pickerSearch, type PickerItemResponseModel } from '../api';

/** A folder or a form as the picker shows it: its key, kind, name, a form's alias, a folder's children. */
export type PickerItem = PickerItemResponseModel;

/** One page of a level or of a search, with how many items there are in all. */
export interface PickerPage {
  items: PickerItem[];
  total: number;
}

/** How many items a level or a search loads at a time. */
export const PAGE_SIZE = 100;

/** The most keys one lookup asks for, the endpoint's limit. */
export const MAX_KEYS = 100;

/** The picker's reads for one data type. */
export interface FormPickerData {
  /** A page of a folder's folders and forms, or of the top level. */
  children(parentKey: string | undefined, skip: number): Promise<PickerPage | undefined>;
  /** A page of the forms whose name or alias holds the text. */
  search(text: string, skip: number): Promise<PickerPage | undefined>;
  /** The forms of these keys the picker may pick, in the order asked; the others are left out. */
  forms(keys: readonly string[]): Promise<PickerItem[] | undefined>;
}

const quiet = { disableNotifications: true };

/**
 * The picker's reads, made as the host.
 * @param host The element that asks, whose controllers carry the requests.
 * @param dataTypeKey The picker's data type, whose settings limit every answer, or none.
 */
export function formPickerData(host: UmbControllerHost, dataTypeKey: string | undefined): FormPickerData {
  const limits = dataTypeKey ? { dataTypeKey } : {};
  return {
    async children(parentKey, skip) {
      const query = { ...limits, ...(parentKey ? { parentKey } : {}), skip, take: PAGE_SIZE };
      const { data, error } = await tryExecute(host, pickerChildren({ query }), quiet);
      return error || !data ? undefined : data;
    },
    async search(text, skip) {
      const { data, error } = await tryExecute(
        host,
        pickerSearch({ query: { ...limits, text, skip, take: PAGE_SIZE } }),
        quiet,
      );
      return error || !data ? undefined : data;
    },
    async forms(keys) {
      if (keys.length === 0) return [];
      const found: PickerItem[] = [];
      for (let start = 0; start < keys.length; start += MAX_KEYS) {
        const query = { ...limits, keys: keys.slice(start, start + MAX_KEYS) };
        const { data, error } = await tryExecute(host, pickerForms({ query }), quiet);
        if (error || !data) return undefined;
        found.push(...data);
      }
      return found;
    },
  };
}
