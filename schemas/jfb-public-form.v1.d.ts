// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Made by scripts/generate-public-types.mjs from jfb-public-form.v1.schema.json; do not edit (ADR-0125).

/**
 * The form the definition endpoint answers with (GET /jfb/api/v1/forms/{keyOrAlias}, its form property): the public
 * projection with each text in the page's culture (ADR-0002, ADR-0035, ADR-0125). A property that may be null is
 * left out instead. The TypeScript types in jfb-public-form.v1.d.ts are made from this schema.
 */
export type JfbPublicForm = PublicForm;

export type ConditionOperand = string | {
  field: string;
} | {
  today: string;
} | (string | {
  field: string;
} | {
  today: string;
})[];

export type FieldCheck = PatternCheck | MatchesFieldCheck | UniquePerFormCheck;

export type FieldMessages = {
  required?: string;
  invalid?: string;
  minLength?: string;
  maxLength?: string;
  minWords?: string;
  maxWords?: string;
  min?: string;
  max?: string;
  step?: string;
  dateWindow?: string;
  selections?: string;
  fileType?: string;
  fileSize?: string;
  fileCount?: string;
  full?: string;
  confirmation?: string;
  digits?: string;
  rowCount?: string;
};

export type MatchesFieldCheck = {
  kind: "matchesField";
  field: string;
  message?: string;
};

export type PatternCheck = {
  kind: "pattern";
  pattern: string;
  message?: string;
};

export type PublicField = {
  key: string;
  type: string;
  alias?: string;
  span: number;
  label?: string;
  hideLabel: boolean;
  help?: string;
  placeholder?: string;
  required: boolean;
  messages: FieldMessages;
  defaultValue?: string | string[];
  prefill?: PublicPrefill;
  autocomplete?: string;
  cssClass?: string;
  validation: FieldCheck[];
  rules: PublicRule[];
  options?: PublicOptions;
  rows: PublicGridRow[];
  fields: PublicField[];
  settings?: Record<string, unknown>;
};

export type PublicForm = {
  key: string;
  settings: PublicFormSettings;
  pages: PublicPage[];
  validationRules: PublicValidationRule[];
};

export type PublicFormSettings = {
  submitLabel?: string;
  nextLabel?: string;
  backLabel?: string;
  summaryPage: boolean;
  progress: "steps" | "bar" | "none";
  resume: boolean;
  closedMessage?: string;
  membersOnly: boolean;
  protection: "inherit" | "standard" | "proofOfWork" | "captcha";
  captchaProvider?: string;
  theme?: string;
  requiredMarker: "inherit" | "required" | "optional" | "none";
  validationSummary: boolean;
  choiceLayout: "vertical" | "horizontal" | "cards";
  cssClass?: string;
  queryValues: string[];
};

export type PublicGridRow = {
  key: string;
  label?: string;
  required: boolean;
};

export type PublicGroup = {
  key: string;
  title?: string;
  description?: string;
  columns: number;
  style: "plain" | "card";
  rules: PublicRule[];
  fields: PublicField[];
};

export type PublicOption = {
  value: string;
  label?: string;
  description?: string;
  image?: string;
  parentValues: string[];
};

export type PublicOptions = {
  items: PublicOption[];
  fromSource: boolean;
  parent?: string;
};

export type PublicPage = {
  key: string;
  title?: string;
  description?: string;
  rules: PublicRule[];
  groups: PublicGroup[];
};

export type PublicPrefill = {
  query?: string;
  context?: string;
  fromMember: boolean;
  memberMode: "show" | "hide";
};

export type PublicRule = {
  key: string;
  effect: "show" | "hide" | "require" | "run" | "use";
  match: "all" | "any";
  conditions: (RuleCondition | RuleGroup)[];
};

export type PublicValidationRule = {
  key: string;
  field: string;
  message?: string;
  match: "all" | "any";
  conditions: (RuleCondition | RuleGroup)[];
};

export type RuleCondition = {
  field?: string;
  context?: string;
  op: string;
  value?: ConditionOperand;
};

export type RuleGroup = {
  match: "all" | "any";
  conditions: (RuleCondition | RuleGroup)[];
};

export type UniquePerFormCheck = {
  kind: "uniquePerForm";
  message?: string;
};
