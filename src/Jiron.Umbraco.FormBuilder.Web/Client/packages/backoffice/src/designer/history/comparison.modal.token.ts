// Two versions compared in words (ROADMAP 10.3a and 10.3b, IA 5.6, ADR-0196, ADR-0198): Umbraco's medium sidebar, for
// two ticked versions, or for one version and the current draft, which it can restore as a new draft.
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
  /** Whether the older side can be restored as a new draft: one version compared with the current draft. */
  readonly restorable?: boolean;
  /** Whether Restore and publish... is offered beside it: not for the live version, which visitors get already. */
  readonly publishable?: boolean;
  /** Whether the draft has changes not saved yet, which the comparison leaves out and a restore saves first. */
  readonly unsaved?: boolean;
}

/** Which restore was chosen: as a new draft, or that and then the publish dialog. */
export interface ComparisonModalValue {
  readonly restore: 'draft' | 'publish';
}

export const COMPARISON_MODAL = new UmbModalToken<ComparisonModalData, ComparisonModalValue>(
  VERSION_COMPARISON_MODAL_ALIAS,
  {
    modal: { type: 'sidebar', size: 'medium' },
  },
);
