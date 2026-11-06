// The Details of an action's last run (ROADMAP 10.2f, IA 5.3, ADR-0195): Umbraco's small sidebar over the Actions view,
// for a run that failed or is uncertain.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import { ACTION_RUN_MODAL_ALIAS } from '../constants';
import type { RunDetails } from './run-words';

export interface ActionRunModalData {
  /** The action's name, the sidebar's headline. */
  readonly name: string;
  /** What the run's Details say. */
  readonly details: RunDetails;
  /** Whether the action is a webhook (Slack and Teams too), whose Test asks its server again. */
  readonly webhook: boolean;
  /**
   * Tests the action as the draft holds it, as its card's Test does (ADR-0190), calling `started` once it runs; without
   * it, no Test, as for who may only read the actions.
   */
  readonly test?: (started: () => void) => Promise<void>;
}

export const ACTION_RUN_MODAL = new UmbModalToken<ActionRunModalData, never>(ACTION_RUN_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'small' },
});
