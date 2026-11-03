// What the option editor and the canvas read from the server (ADR-0172): the kinds and code sources, which describe the
// site's code and are read once; the library's lists, read again each time, as they change; and a draft's options as
// a source resolves them, kept by the form, the field, what its options hold and the culture, so the canvas asks again
// only when one of them changes.
import type { UmbControllerHost } from '@umbraco-cms/backoffice/controller-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import {
  draftOptionsResolve,
  optionListList,
  optionSourceList,
  type OptionListItemResponseModel,
  type OptionSourcesResponseModel,
  type ResolvedOptionsResponseModel,
} from '../../api';
import type { DesignField } from '../definition';

let sources: Promise<OptionSourcesResponseModel | undefined> | undefined;

/** The kinds a field may configure and the site's code sources, read once; tried again after a failed read. */
export function readOptionSources(host: UmbControllerHost): Promise<OptionSourcesResponseModel | undefined> {
  sources ??= tryExecute(host, optionSourceList(), { disableNotifications: true }).then(({ data }) => {
    if (!data) sources = undefined;
    return data;
  });
  return sources;
}

/** The library's option lists, by name; an empty list when they can't be read. */
export async function readOptionLists(host: UmbControllerHost): Promise<OptionListItemResponseModel[]> {
  const { data } = await tryExecute(host, optionListList(), { disableNotifications: true });
  return data?.items ?? [];
}

const resolved = new Map<string, Promise<ResolvedOptionsResponseModel | undefined>>();

/** The options of a draft's field from its source, in a culture; undefined when they can't be read. */
export function resolveOptions(
  host: UmbControllerHost,
  form: string,
  field: DesignField,
  culture: string,
): Promise<ResolvedOptionsResponseModel | undefined> {
  const options = field['options'] ?? {};
  const kept = JSON.stringify([form, field.key, field.type, options, culture]);
  let found = resolved.get(kept);
  if (!found) {
    found = tryExecute(
      host,
      draftOptionsResolve({
        path: { key: form },
        body: { field: field.key, type: field.type, options, culture },
      }),
      { disableNotifications: true },
    ).then(({ data }) => data);
    // A failed read, or a source that gave nothing, is asked again next time; the newest 200 answers are kept.
    void found.then((answer) => {
      if (!answer?.isAvailable) resolved.delete(kept);
    });
    resolved.set(kept, found);
    if (resolved.size > 200) resolved.delete(resolved.keys().next().value as string);
  }
  return found;
}
