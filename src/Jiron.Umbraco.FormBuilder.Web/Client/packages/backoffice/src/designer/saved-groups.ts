// The site's saved groups (ROADMAP 12.1, ADR-0217): the field picker lists them and inserts a fresh copy, and a group's
// "..." on the canvas saves one, as the canvas shows it with the validation rules of its fields, for a user who may
// manage the library.
import type { UmbClassInterface } from '@umbraco-cms/backoffice/class-api';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { templateList, templateSaveGroup, type TemplateResponseModel } from '../api';
import { myPermissions } from '../section/my-permissions';
import {
  allFields,
  normalise,
  validationRulesOf,
  type DesignDefinition,
  type DesignGroup,
  type DesignValidationRule,
} from './definition';

const quiet = { disableNotifications: true } as const;

/**
 * A group the site saved as a template (ADR-0217): its name, the group as a definition holds it, and the validation rules
 * of its fields.
 */
export interface SavedGroup {
  readonly key: string;
  readonly name: string;
  readonly group: DesignGroup;
  readonly checks: readonly DesignValidationRule[];
}

/**
 * A saved group as the field picker lists it, read as the model reads a definition (the server's canonical form leaves
 * out every empty list), with the validation rules of its fields; none for a template that holds no group.
 */
export function savedGroupOf(template: TemplateResponseModel): SavedGroup[] {
  const definition = normalise(template.definition);
  const group = definition.pages[0]?.groups[0];
  return group ? [{ key: template.key, name: template.name, group, checks: validationRulesOf(definition) }] : [];
}

/** The site's saved groups, by name; none without an answer. */
export async function readSavedGroups(host: UmbClassInterface): Promise<SavedGroup[]> {
  const { data } = await tryExecute(host, templateList({ query: { kind: 'group', take: 500 } }), quiet);
  return (data?.items ?? []).flatMap(savedGroupOf);
}

/** Whether the user may save a group as a template: manageLibrary. */
export async function maySaveGroups(host: UmbClassInterface): Promise<boolean> {
  return (await myPermissions(host))?.global.includes('manageLibrary') ?? false;
}

/** What a group's save left out: the rules and the validation rules that name a field outside it. */
export interface LeftOut {
  readonly rules: number;
  readonly checks: number;
}

/**
 * Saves a group as a template, with the validation rules of the definition that show their message on one of its fields:
 * what it left out for naming a field outside the group, or the refusal.
 */
export async function saveGroup(
  host: UmbClassInterface,
  definition: DesignDefinition,
  group: DesignGroup,
  name: string,
  description: string | null,
): Promise<{ readonly leftOut?: LeftOut; readonly error?: unknown }> {
  const own = new Set(allFields({ pages: [{ key: '', groups: [group] }] }).map((field) => field.key));
  const validationRules = validationRulesOf(definition).filter((rule) => own.has(rule.field));
  const { data, error } = await tryExecute(
    host,
    templateSaveGroup({ body: { name, description, group, validationRules } }),
    quiet,
  );
  return data ? { leftOut: { rules: data.rulesLeftOut, checks: data.checksLeftOut } } : { error };
}
