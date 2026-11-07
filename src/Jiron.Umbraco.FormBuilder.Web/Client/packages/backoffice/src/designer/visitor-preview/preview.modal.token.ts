// The preview (docs/ux/information-architecture.md 5.7, ADR-0203, ADR-0204): Umbraco's full-screen sidebar with the
// saved draft as visitors would get it, in a frame sandboxed from the backoffice, at the designer's widths, light or
// dark, in a language of the site, empty or with the sample answers; what it sends are test entries.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { CanvasWidth } from '../canvas/widths';
import { PREVIEW_MODAL_ALIAS } from '../constants';
import type { SiteLanguage } from '../preview/look';
import type { PreviewAsk, PreviewRead, PreviewTicket, RunAnswer, TestEntryRead } from './preview-read';

/** An action as Run actions... lists it: its name, and what it does in one line, as the Actions view says them. */
export interface ActionLine {
  readonly name: string;
  readonly line: string;
}

export interface PreviewModalData {
  /** The form's name, which the headline says. */
  readonly name: string;
  /** The form's key, which the frame's asks name. */
  readonly form: string;
  /** The site's languages, the default first. */
  readonly languages: readonly SiteLanguage[];
  /** The language the designer shows the texts in; null for the site's default. */
  readonly culture: string | null;
  /** The width the preview starts at, the designer's. */
  readonly width: CanvasWidth;
  /** Whether the user may run the form's actions: Run actions for test entries and Run actions... show (ADR-0204). */
  readonly mayRunActions: boolean;
  /** The saved draft's document in a language, light or dark, empty or with the sample answers. */
  readonly read: (culture: string | null, ask: PreviewAsk) => Promise<PreviewRead>;
  /** A new ticket for the version the document shows, whose test entries run their actions or not; undefined without an answer. */
  readonly ticket: (version: number, runActions: boolean) => Promise<PreviewTicket | undefined>;
  /** The test entry the frame sent, by its submission id. */
  readonly testEntry: (submissionId: string) => Promise<TestEntryRead>;
  /** Runs the actions of the test entry the frame sent, once. */
  readonly runActions: (submissionId: string) => Promise<RunAnswer>;
  /** The form's actions by their keys, named and worded as the Actions view says them, in the user's language. */
  readonly describe: (keys: readonly string[]) => Promise<readonly ActionLine[]>;
}

export const PREVIEW_MODAL = new UmbModalToken<PreviewModalData, never>(PREVIEW_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'full' },
});
