// Review both versions (ROADMAP 10.3c, IA 5.7, ADR-0200): Umbraco's large sidebar listing each difference between the
// draft saved meanwhile and the user's, each taken from either side; Save answers the differences taken from the user's.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import { REVIEW_MODAL_ALIAS } from '../constants';
import type { ChangeRow } from '../history/change-words';
import type { Side } from './review-model';

/** A difference as the sidebar shows it: what it is about, each side's values, and the side it starts on. */
export interface ReviewDifference {
  readonly id: string;
  readonly title: string;
  /** The values that differ: the older side the draft saved meanwhile, the newer the user's. */
  readonly rows: readonly ChangeRow[];
  /** Where one side made it, that side; none otherwise, so it waits for a choice. */
  readonly start?: Side;
  /** Whether both sides changed it, which the sidebar says; unknown where neither comparison names it. */
  readonly both?: boolean;
}

export interface ReviewModalData {
  /** The draft saved meanwhile, named for who saved it: "Ana Novak's version". */
  readonly theirs: string;
  readonly differences: readonly ReviewDifference[];
}

/** The differences taken from the user's version; every other one keeps the draft saved meanwhile. */
export interface ReviewModalValue {
  readonly fromYours: readonly string[];
}

export const REVIEW_MODAL = new UmbModalToken<ReviewModalData, ReviewModalValue>(REVIEW_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'large' },
});
