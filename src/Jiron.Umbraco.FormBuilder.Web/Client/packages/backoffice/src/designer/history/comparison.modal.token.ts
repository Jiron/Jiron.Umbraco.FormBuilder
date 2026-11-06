// Two versions compared in words (ROADMAP 10.3a, IA 5.6, ADR-0196): Umbraco's medium sidebar, for two ticked versions
// or for one version and the current draft.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import { VERSION_COMPARISON_MODAL_ALIAS } from '../constants';
import type { ChangePart } from './change-words';

/** What a read of the comparison gives: its parts in words, a version that can't be read, or no answer. */
export type ComparisonRead =
  | { readonly kind: 'compared'; readonly parts: readonly ChangePart[] }
  | { readonly kind: 'unreadable'; readonly name: string }
  | { readonly kind: 'failed' };

export interface ComparisonModalData {
  /** "Version 12 and version 14", "Version 14 and the current draft". */
  readonly headline: string;
  /** When and by whom, for one version compared with the draft. */
  readonly made?: string;
  /** The columns' headings: the older side, then the newer. */
  readonly olderName: string;
  readonly newerName: string;
  /** Reads the comparison; called again by Try again. */
  readonly read: () => Promise<ComparisonRead>;
}

export const COMPARISON_MODAL = new UmbModalToken<ComparisonModalData, never>(VERSION_COMPARISON_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'medium' },
});
