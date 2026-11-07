// The preview (docs/ux/information-architecture.md 5.7, ADR-0203): Umbraco's full-screen sidebar with the saved draft
// as visitors would get it, in a frame sandboxed from the backoffice, at the designer's widths, light or dark and in a
// language of the site.
import { UmbModalToken } from '@umbraco-cms/backoffice/modal';
import type { CanvasWidth } from '../canvas/widths';
import { PREVIEW_MODAL_ALIAS } from '../constants';
import type { SiteLanguage } from '../preview/look';
import type { PreviewRead, PreviewTicket } from './preview-read';

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
  /** The saved draft's document in a language, light or dark. */
  readonly read: (culture: string | null, dark: boolean) => Promise<PreviewRead>;
  /** A new ticket for the version the document shows; undefined without an answer. */
  readonly ticket: (version: number) => Promise<PreviewTicket | undefined>;
}

export const PREVIEW_MODAL = new UmbModalToken<PreviewModalData, never>(PREVIEW_MODAL_ALIAS, {
  modal: { type: 'sidebar', size: 'full' },
});
