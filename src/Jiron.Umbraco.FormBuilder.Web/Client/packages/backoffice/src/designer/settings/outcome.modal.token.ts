// The outcome's sidebar's token (ROADMAP 10.1b, ADR-0185): one outcome of After sending, or a new one, edited beside
// the Settings view and kept by Update or Add; Delete outcome takes it away. Umbraco's large sidebar, so the outcome's
// live preview has room beside its settings (10.1c, ADR-0187).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FieldTypeResponseModel } from '../../api';
import type { CheckAsk } from '../check/form-checker';
import type { DesignDefinition } from '../definition';
import type { RuleBuilderLanguage } from '../rules/rule-builder.modal.token';
import type { DesignOutcome, OutcomeSetting } from './outcome-model';
import type { OutcomePreview } from './outcome-preview';

export const OUTCOME_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.Outcome';

export interface OutcomeModalData {
  /** The outcome to change; null for a new one. */
  readonly outcome: DesignOutcome | null;
  /** Its number in the list, which its headline says. */
  readonly number: number;
  /** How many outcomes the list has, the one edited among them. */
  readonly count: number;
  /** The draft as the Settings view holds it. */
  readonly definition: DesignDefinition;
  /** The form's key, whose draft the merge tags and the rule's options are read from. */
  readonly form: string;
  /** The language the view shows, in which the message is written. */
  readonly culture: string;
  readonly languages: readonly RuleBuilderLanguage[];
  readonly types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The message visitors get where an outcome has none, in the language shown. */
  readonly defaultMessage: string;
  /** The longest text a message may be, as the save checks hold it. */
  readonly htmlLength: number;
  /** Asks the form check about a definition, as the workspace does. */
  readonly check: CheckAsk;
  /** Asks the server for the preview of an outcome of a definition, in a language of the site (ADR-0187). */
  readonly preview: (
    definition: DesignDefinition,
    outcome: string,
    culture: string,
  ) => Promise<OutcomePreview | undefined>;
  /** The stylesheets of the site's look, which the preview draws the outcome with, the base rules first. */
  readonly stylesheets: readonly string[];
  /** The setting to put the focus on, as a Fix it leads to it; the first one where none is given. */
  readonly focus?: OutcomeSetting;
  /** Called once the sidebar is gone, so the view can set the focus after Umbraco gives it back. */
  readonly closed?: () => void;
}

/** The outcome as Update or Add keeps it, at its place in the list from 0; or that Delete outcome takes it away. */
export type OutcomeModalValue = { readonly outcome: DesignOutcome; readonly at: number } | { readonly deleted: true };

export const OUTCOME_MODAL = new UmbModalToken<OutcomeModalData, OutcomeModalValue>(OUTCOME_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'large' },
});
