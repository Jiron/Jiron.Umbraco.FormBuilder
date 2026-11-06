// An action's sidebar (ROADMAP 10.2a, IA 5.3, ADR-0188): Umbraco's medium sidebar over the Actions view, for a new
// action or one of the form's; the view opens the large one for an email, whose preview shows beside its settings
// (ADR-0189).
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { ActionTypeResponseModel, FieldTypeResponseModel } from '../../api';
import type { CheckAsk } from '../check/form-checker';
import { ACTION_MODAL_ALIAS } from '../constants';
import type { DesignDefinition } from '../definition';
import type { RuleBuilderLanguage } from '../rules/rule-builder.modal.token';
import type { LayoutChoice } from './action-editors';
import type { ActionChoice, ActionSetting, DesignAction, Trigger, TriggerNeed } from './action-model';
import type { EmailPreview } from './email-preview';

export interface ActionModalData {
  /** The action to change; null for a new one, which starts with the first choice of what happens. */
  readonly action: DesignAction | null;
  /** The draft as the view has it, which the action is checked in and its rule built on. */
  readonly definition: DesignDefinition;
  /** The form's key, which reads a field's options for the rules builder. */
  readonly form: string;
  /** The language the view shows texts in, and the site's languages. */
  readonly culture: string;
  readonly languages: readonly RuleBuilderLanguage[];
  /** The field types by alias, for the rule's words and its builder. */
  readonly fieldTypes: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The site's action types, whose settings the sidebar draws. */
  readonly actionTypes: readonly ActionTypeResponseModel[];
  /** What a new action may do (ADR-0188). */
  readonly choices: readonly ActionChoice[];
  /** The setting each trigger needs on the form before it runs, or null where it runs. */
  readonly needs: Readonly<Record<Trigger, TriggerNeed | null>>;
  /** Without `manageActions`: the settings shown as they are, nothing to change (IA 5.3). */
  readonly readOnly: boolean;
  /** Asks the form check about a definition, as the workspace does: the action as Add or Update would keep it. */
  readonly check: CheckAsk;
  /** The email layouts a mail may name, the built-in ones first; none offers only the site's default (ADR-0189). */
  readonly layouts?: readonly LayoutChoice[];
  /** The key of the layout a mail has without one of its own. */
  readonly defaultLayout?: string;
  /**
   * The mail of an email action of a definition, by the action's key, as the team gets it for the sample entry in a
   * language (ADR-0189); undefined without an answer. Without it, an email shows no preview.
   */
  readonly preview?: (
    definition: DesignDefinition,
    action: string,
    culture: string,
  ) => Promise<EmailPreview | undefined>;
  /** The box a Fix it, or Move to another trigger..., leads to. */
  readonly focus?: ActionSetting;
  /**
   * Called once the sidebar is gone. It answers before it slides out, and its dialog, as it closes, gives the focus back
   * to what opened it; a focus set after this stays.
   */
  readonly closed?: () => void;
}

/**
 * The action as Add or Update keeps it, with its place among its trigger's where one was chosen (null keeps it where it
 * is, or puts a new one, or one with another trigger, at the end), or the action deleted.
 */
export type ActionModalValue =
  { readonly action: DesignAction; readonly at: number | null } | { readonly deleted: true };

export const ACTION_MODAL = new UmbModalToken<ActionModalData, ActionModalValue>(ACTION_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'medium' },
});
