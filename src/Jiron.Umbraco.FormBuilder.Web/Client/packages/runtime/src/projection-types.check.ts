// The published visitor form (schemas/jfb-public-form.v1.d.ts, ADR-0125) fits what the runtime and the shared logic
// read of it: their interfaces are narrower, written for what they use, and this file fails the type check when the
// projection drifts from them. It holds types only, and nothing imports it.
import type { ProjectedField, ProjectedForm, ProjectedItem, ProjectedRule, ProjectedValidationRule } from '@jfb/logic';
import type {
  JfbPublicForm,
  PublicField,
  PublicPage,
  PublicRule,
  PublicValidationRule,
  RuleCondition,
  RuleGroup,
} from '../../../../../../schemas/jfb-public-form.v1';
import type { FormModel, ModelField, ModelPage } from './model';

/** True when every value of A is a value of B. */
type Fits<A, B> = [A] extends [B] ? true : false;

/** Compiles only for true. */
type Holds<T extends true> = T;

export type ProjectionChecks = [
  Holds<Fits<JfbPublicForm, FormModel['form']>>,
  Holds<Fits<JfbPublicForm, ProjectedForm>>,
  Holds<Fits<PublicPage, ModelPage>>,
  Holds<Fits<PublicField, ModelField>>,
  Holds<Fits<PublicField, ProjectedField>>,
  Holds<Fits<PublicRule, ProjectedRule>>,
  Holds<Fits<RuleCondition | RuleGroup, ProjectedItem>>,
  Holds<Fits<PublicValidationRule, ProjectedValidationRule>>,
];
