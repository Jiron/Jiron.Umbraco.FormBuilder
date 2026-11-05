// The rules builder's token (ROADMAP 9.5a, 9.5c, ADR-0175, ADR-0178): a sidebar that builds or changes one rule, a
// form-level validation rule too, which it edits as a rule whose effect is `message`.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { DesignDefinition, DesignRule } from '../definition';
import type { Effect, TypeFacts } from './rule-draft';

export const RULE_BUILDER_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.RuleBuilder';

/** A language of the site. */
export interface RuleBuilderLanguage {
  readonly culture: string;
  readonly name: string;
  /** Whether visitors read its texts where another language has none. */
  readonly isDefault?: boolean;
}

export interface RuleBuilderModalData {
  readonly definition: DesignDefinition;
  /** The form's key, which reads a field's options. */
  readonly form: string;
  /** The element the builder was opened from; the outcome, by its key, where `outcome` is given. */
  readonly origin: string;
  /**
   * The outcome the rule is an outcome's, by its number in the list, null for a new one (ROADMAP 10.1b, ADR-0185): its
   * effect is then `use`, it stays on its outcome, and every field and context of the form is a subject, as all the
   * answers are in once the form is sent.
   */
  readonly outcome?: { readonly number: number | null };
  /** The rule to change, on the origin (a validation rule with the effect `message`); null for a new one. */
  readonly rule: DesignRule | null;
  /** A new rule's effect. */
  readonly effect: Effect;
  /** The language the designer shows, in which options are said and a validation rule's message is written. */
  readonly culture: string;
  /** The site's languages; a condition may look at the language where there are several. */
  readonly languages: readonly RuleBuilderLanguage[];
  readonly types: Readonly<Record<string, TypeFacts>>;
  /** The name of every page, group, field and grid row ("Grid: Row"), by its key. */
  readonly names: Readonly<Record<string, string>>;
  /** Whether the editor model refuses the rule on an element: 'cycle' where it would wait on itself, else null. */
  readonly refusal: (target: string, rule: DesignRule) => 'cycle' | null;
  /** Called once the builder is shown. */
  readonly opened?: () => void;
  /**
   * Called once the builder is gone. Its sidebar answers before it slides out, and its dialog, as it closes, gives the
   * focus back to what opened it; a focus set after this stays.
   */
  readonly closed?: () => void;
}

/** The rule as it is to be kept on its element (a validation rule's on its field); null where it is deleted. */
export interface RuleBuilderModalValue {
  readonly target: string;
  readonly rule: DesignRule | null;
}

export const RULE_BUILDER_MODAL = new UmbModalToken<RuleBuilderModalData, RuleBuilderModalValue>(
  RULE_BUILDER_MODAL_ALIAS,
  { modal: { type: 'sidebar', size: 'medium' } },
);
