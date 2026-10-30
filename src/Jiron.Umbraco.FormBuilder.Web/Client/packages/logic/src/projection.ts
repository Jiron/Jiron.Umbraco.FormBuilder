// The form engine's model of a form made from the form endpoint's answer (ROADMAP 6.4c, ADR-0111), as Core's
// DefinitionLogic makes it from the definition: the ids are the keys, a field's kinds and whether it can be required
// are the answer's `fields` (ADR-0109), a grid's rows are subjects of conditions, and a calculation carries its formula
// and places. The page knows only the switched-on rules and has no actions or outcomes; the fixture
// tests/fixtures/definitions/logic.json holds both runtimes to one model.
import { own } from './errors.ts';
import type { LogicField, LogicForm, LogicFormGroup, LogicGridRow, LogicPage, LogicValidationRule } from './form.ts';
import type { Condition, Group, Kind, Operand, Rule } from './rules.ts';

/** A condition or a group of them, as the projection writes them. */
export interface ProjectedItem {
  readonly field?: string | null;
  readonly context?: string | null;
  readonly op?: string;
  readonly value?: Operand | null;
  readonly match?: string | null;
  readonly conditions?: readonly ProjectedItem[] | null;
}

/** A switched-on rule as the projection writes it. */
export interface ProjectedRule {
  readonly effect: string;
  readonly match?: string | null;
  readonly conditions?: readonly ProjectedItem[] | null;
}

export interface ProjectedField {
  readonly key: string;
  readonly type: string;
  readonly alias?: string | null;
  readonly required?: boolean;
  readonly settings?: Readonly<Record<string, unknown>> | null;
  readonly rules?: readonly ProjectedRule[] | null;
  readonly rows?: readonly { readonly key: string }[] | null;
  readonly fields?: readonly ProjectedField[] | null;
}

export interface ProjectedForm {
  readonly pages: readonly {
    readonly key: string;
    readonly rules?: readonly ProjectedRule[] | null;
    readonly groups: readonly {
      readonly key: string;
      readonly rules?: readonly ProjectedRule[] | null;
      readonly fields: readonly ProjectedField[];
    }[];
  }[];
  readonly validationRules?: readonly (ProjectedRule & { readonly key: string; readonly field: string })[] | null;
}

/** What the browser decides a field by, from the form endpoint's answer (ADR-0109). */
export interface FieldLogic {
  readonly kind?: string | null;
  readonly rowKind?: string | null;
  readonly canBeRequired: boolean;
  readonly builtIn: boolean;
}

/** The form engine's model of the form the endpoint's answer shows. */
export function logicFormOf(form: ProjectedForm, fields: Readonly<Record<string, FieldLogic>>): LogicForm {
  const field = (shown: ProjectedField): LogicField => {
    const logic = own(fields, shown.key);
    const rowKind = logic?.rowKind;
    return {
      id: shown.key,
      kind: (logic?.kind ?? null) as Kind | null,
      canBeRequired: logic?.canBeRequired ?? false,
      alias: shown.alias ?? null,
      calculation: logic?.builtIn === true && shown.type === 'calculation' ? calculationOf(shown.settings) : null,
      required: shown.required ?? false,
      rules: rules(shown.rules),
      fields: (shown.fields ?? []).map(field),
      rows:
        rowKind == null ? [] : (shown.rows ?? []).map((row): LogicGridRow => ({ id: row.key, kind: rowKind as Kind })),
    };
  };
  return {
    pages: form.pages.map((page): LogicPage => ({
      id: page.key,
      rules: rules(page.rules),
      groups: page.groups.map((group): LogicFormGroup => ({
        id: group.key,
        rules: rules(group.rules),
        fields: group.fields.map(field),
      })),
    })),
    validationRules: (form.validationRules ?? []).map((rule): LogicValidationRule => ({
      id: rule.key,
      field: rule.field,
      enabled: true,
      match: rule.match ?? 'all',
      conditions: items(rule.conditions),
    })),
  };
}

const rules = (given: readonly ProjectedRule[] | null | undefined): Rule[] =>
  (given ?? []).map((rule) => ({
    effect: rule.effect,
    enabled: true,
    match: rule.match ?? 'all',
    conditions: items(rule.conditions),
  }));

function items(given: readonly ProjectedItem[] | null | undefined): (Condition | Group)[] {
  return (given ?? []).map((item): Condition | Group => {
    if (item.op === undefined) return { match: item.match ?? 'all', conditions: items(item.conditions) };
    return {
      ...(item.field == null ? {} : { field: item.field }),
      ...(item.context == null ? {} : { context: item.context }),
      op: item.op,
      ...(item.value == null ? {} : { value: item.value }),
    };
  });
}

/**
 * A calculation's formula and places, as Core reads its settings: a formula that is no text gives an empty one, which
 * the form engine refuses, and places that are not a whole number give the default.
 */
function calculationOf(settings: Readonly<Record<string, unknown>> | null | undefined): {
  formula: string;
  decimals: number | null;
} {
  const formula = settings?.['formula'];
  const format = settings?.['format'];
  const decimals =
    typeof format === 'object' && format !== null ? (format as Record<string, unknown>)['decimals'] : undefined;
  return {
    formula: typeof formula === 'string' ? formula : '',
    decimals: typeof decimals === 'number' && Number.isInteger(decimals) ? decimals : null,
  };
}
