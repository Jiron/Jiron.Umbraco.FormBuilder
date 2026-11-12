// A view's column on a phone (ROADMAP 16.6, ADR-0241). Umbraco's page stays at least 920 px wide, so a narrower window
// scrolls sideways and shows only the first part of the 620 px workspace (ADR-0182). While it does, the view's column
// (its `.page`) starts at the workspace's start and keeps to the window's width: once the workspace is in sight the whole
// column is too, no line runs past the window's edge, and the column's container queries lay it out for the phone (two
// boxes one under the other, a closed group's line under its name). A wider window changes nothing.
import { css } from '@umbraco-cms/backoffice/external/lit';

/** Added after a view's own styles, so it wins over the column's own width and centring. */
export const jfbPhoneColumn = css`
  @media (max-width: 919px) {
    .page {
      max-inline-size: min(100%, 100vw - 2 * var(--uui-size-layout-1));
      margin-inline: 0 auto;
    }
  }
`;
