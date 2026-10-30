/**
 * The page's model data (ADR-0015, ADR-0093): the form endpoint's answer for the page's culture, in the form's
 * `<script type="application/json" data-jfb-model>`. These are the parts the runtime reads; the endpoint's OpenAPI
 * document (docs/api/jfb-public.json) describes them all.
 */

/** A field of the public projection (ADR-0002, ADR-0035), as far as the runtime reads it. */
export interface ModelField {
  readonly key: string;
  readonly type: string;
  readonly alias?: string | null;
  readonly settings?: Readonly<Record<string, unknown>> | null;
  readonly prefill?: { readonly query?: string | null } | null;
  /** A grid's rows. */
  readonly rows?: readonly { readonly key: string }[];
  /** A repeatable group's child fields. */
  readonly fields?: readonly ModelField[];
}

export interface ModelGroup {
  readonly key: string;
  readonly fields: readonly ModelField[];
}

export interface ModelPage {
  readonly key: string;
  readonly groups: readonly ModelGroup[];
}

/** What the browser decides a field by (ADR-0109). */
export interface ModelFieldLogic {
  readonly kind?: string | null;
  readonly rowKind?: string | null;
  readonly canBeRequired: boolean;
  readonly builtIn: boolean;
}

export interface FormModel {
  readonly alias: string;
  readonly revision: number;
  readonly culture: string;
  readonly timeZone: string;
  readonly open: boolean;
  readonly form: { readonly key: string; readonly pages: readonly ModelPage[] };
  /** By each field's key, a repeatable group's child fields too. */
  readonly fields: Readonly<Record<string, ModelFieldLogic>>;
  readonly consents: Readonly<Record<string, string>>;
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

const isArrayOf = (value: unknown, each: (item: unknown) => boolean): boolean =>
  Array.isArray(value) && value.every(each);

function isField(value: unknown): boolean {
  return (
    isObject(value) &&
    typeof value['key'] === 'string' &&
    typeof value['type'] === 'string' &&
    (value['alias'] === undefined || value['alias'] === null || typeof value['alias'] === 'string') &&
    (value['settings'] === undefined || value['settings'] === null || isObject(value['settings'])) &&
    (value['rows'] === undefined ||
      isArrayOf(value['rows'], (row) => isObject(row) && typeof row['key'] === 'string')) &&
    (value['fields'] === undefined || isArrayOf(value['fields'], isField))
  );
}

/**
 * The model data a form carries, or null when it is not the shape this runtime reads: such a form is left to the post
 * without JavaScript. Only the shape is checked; the server made the data, so its values are taken as they are.
 */
export function parseModel(text: string): FormModel | null {
  let data: unknown;
  try {
    data = JSON.parse(text);
  } catch {
    return null;
  }
  if (!isObject(data) || typeof data['alias'] !== 'string' || !isObject(data['fields']) || !isObject(data['consents']))
    return null;
  const form = data['form'];
  if (!isObject(form) || typeof form['key'] !== 'string') return null;
  const pages = form['pages'];
  const fine = isArrayOf(
    pages,
    (page) =>
      isObject(page) &&
      typeof page['key'] === 'string' &&
      isArrayOf(
        page['groups'],
        (group) => isObject(group) && typeof group['key'] === 'string' && isArrayOf(group['fields'], isField),
      ),
  );
  return fine ? (data as unknown as FormModel) : null;
}
