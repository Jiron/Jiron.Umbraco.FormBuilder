// The rules overview's token (ROADMAP 9.5c, IA 5.1, ADR-0178): a sidebar that lists every rule of the form by page.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { FieldTypeResponseModel } from '../../api';
import type { DesignDefinition } from '../definition';

export const RULES_OVERVIEW_MODAL_ALIAS = 'Jiron.FormBuilder.Modal.RulesOverview';

export interface RulesOverviewModalData {
  readonly definition: DesignDefinition;
  readonly types: Readonly<Record<string, FieldTypeResponseModel>>;
  /** The language the designer shows, in which the rules are said. */
  readonly culture: string;
  /** The site's languages. */
  readonly languages: readonly { readonly culture: string; readonly name: string }[];
  /** Called once the overview is shown. */
  readonly opened?: () => void;
  /**
   * Called once the overview is gone. Its sidebar answers before it slides out, and its dialog, as it closes, gives the
   * focus back to what opened it; a focus set after this stays.
   */
  readonly closed?: () => void;
}

/** Where the overview leads: to an element, to the outcomes in Settings, or to a new rule on the element chosen. */
export type RulesOverviewModalValue =
  | { readonly go: 'element'; readonly key: string }
  | { readonly go: 'outcomes' }
  | { readonly go: 'add'; readonly key: string };

export const RULES_OVERVIEW_MODAL = new UmbModalToken<RulesOverviewModalData, RulesOverviewModalValue>(
  RULES_OVERVIEW_MODAL_ALIAS,
  { modal: { type: 'sidebar', size: 'small' } },
);
