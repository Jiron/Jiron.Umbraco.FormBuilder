/**
 * The page's model data (ADR-0015, ADR-0093): the form endpoint's answer for the page's culture, in the form's
 * `<script type="application/json" data-jfb-model>`. These are the parts the runtime reads; the endpoint's OpenAPI
 * document (docs/api/jfb-public.json) describes them all.
 */
import type {
  CultureFormats,
  DisplayWords,
  MergeValue,
  ProjectedField,
  ProjectedRule,
  RecallForm,
  TemplateContext,
} from '@jfb/logic';

/** A field of the public projection (ADR-0002, ADR-0035), as far as the runtime reads it: its rules too (ADR-0111). */
export interface ModelField extends ProjectedField {
  readonly prefill?: { readonly query?: string | null } | null;
  /** The label in the page's culture, and whether it is hidden from sight. */
  readonly label?: string | null;
  readonly hideLabel?: boolean;
  /** Its help and placeholder in the page's culture, which merge tags may fill (ADR-0115). */
  readonly help?: string | null;
  readonly placeholder?: string | null;
  /** What its controls start with, a list for a field of several answers; merge tags may fill it (ADR-0118). */
  readonly defaultValue?: string | readonly string[] | null;
  /** The editor's own messages by the kind of error. */
  readonly messages?: Readonly<Record<string, string>> | null;
  /** The field's own checks, each with the editor's own message. */
  readonly validation?:
    | readonly {
        readonly kind: string;
        readonly pattern?: string;
        readonly field?: string;
        readonly message?: string | null;
      }[]
    | null;
  /** Its own options, or a list or source the server resolves; the field whose answers filter them. */
  readonly options?: {
    readonly items?: readonly {
      readonly value: string;
      readonly label?: string | null;
      readonly parentValues?: readonly string[] | null;
    }[];
    readonly fromSource?: boolean;
    readonly parent?: string | null;
  } | null;
  /** A grid's rows. */
  readonly rows?: readonly { readonly key: string; readonly label?: string | null; readonly required?: boolean }[];
  /** A repeatable group's child fields. */
  readonly fields?: readonly ModelField[];
}

export interface ModelGroup {
  readonly key: string;
  /** Its title and description in the page's culture, which merge tags may fill (ADR-0115). */
  readonly title?: string | null;
  readonly description?: string | null;
  readonly rules?: readonly ProjectedRule[] | null;
  readonly fields: readonly ModelField[];
}

export interface ModelPage {
  readonly key: string;
  /** Its title and description in the page's culture, which merge tags may fill (ADR-0115). */
  readonly title?: string | null;
  readonly description?: string | null;
  readonly rules?: readonly ProjectedRule[] | null;
  readonly groups: readonly ModelGroup[];
}

/** A visitor text that holds merge tags (ADR-0085): its owner's key, the property it is, and its context. */
export interface ModelText {
  readonly owner: string;
  /** `title`, `description`, `label`, `help`, `placeholder`, or `settings.` and a setting's path. */
  readonly text: string;
  readonly context: TemplateContext;
  /** The paths of its tags that its place refuses for their source, written as they stand (ADR-0118). */
  readonly refused?: readonly string[];
}

/** What the page renders its visitor texts and its default values with (ADR-0085, ADR-0115, ADR-0118). */
export interface ModelMergeTags {
  readonly texts: readonly ModelText[];
  /** By context, the values of the tags that are the same for every visitor of the page. */
  readonly values: Readonly<Record<string, Readonly<Record<string, MergeValue>>>>;
  /** The form answer recall reads: the texts' owners and the fields they recall, in the form's order. */
  readonly recall: RecallForm;
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
  readonly form: {
    readonly key: string;
    readonly pages: readonly ModelPage[];
    readonly validationRules?:
      | readonly (ProjectedRule & { readonly key: string; readonly field: string; readonly message?: string | null })[]
      | null;
    /**
     * The query values the rules and hidden fields may read, by name (ADR-0088), and whether the error summary lists
     * the fields' errors (ADR-0113).
     */
    readonly settings?: {
      readonly queryValues?: readonly string[] | null;
      readonly validationSummary?: boolean | null;
    } | null;
  };
  /** By each field's key, a repeatable group's child fields too. */
  readonly fields: Readonly<Record<string, ModelFieldLogic>>;
  readonly consents: Readonly<Record<string, string>>;
  /** The culture's formats, which the messages print values in (ADR-0109). */
  readonly formats: CultureFormats;
  /** The culture's words for yes and no. */
  readonly words: DisplayWords;
  /** The visitor texts that hold merge tags, and what they render with; null for a form without one. */
  readonly mergeTags?: ModelMergeTags | null;
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

/** Whether the merge tags are none, or the shape the runtime renders texts with. */
function isMergeTags(value: unknown): boolean {
  if (value === undefined || value === null) return true;
  return (
    isObject(value) &&
    isArrayOf(
      value['texts'],
      (text) =>
        isObject(text) &&
        typeof text['owner'] === 'string' &&
        typeof text['text'] === 'string' &&
        typeof text['context'] === 'string' &&
        (text['refused'] === undefined || isArrayOf(text['refused'], (path) => typeof path === 'string')),
    ) &&
    isObject(value['values']) &&
    isObject(value['recall']) &&
    Array.isArray(value['recall']['pages'])
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
  return fine && isMergeTags(data['mergeTags']) ? (data as unknown as FormModel) : null;
}
