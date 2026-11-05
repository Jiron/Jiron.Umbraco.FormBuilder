// What a box shows while its words are read (docs/ux/tokens.md, "Waiting"): still lines in the place of the text, one
// sentence for screen readers, and the box busy. They never pulse, and the box keeps about the height it will have.
import { css, html, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';

/** The lines' look; an element that shows them adds this and `jfbVisuallyHidden` to its styles. */
export const jfbSkeleton = css`
  .skeleton {
    display: block;
    height: 12px;
    margin: var(--uui-size-space-5);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface-alt);
  }
`;

/** Still lines of these widths (in percent of the box), and what is being read, for screen readers. */
export function skeletonLines(said: string, widths: readonly number[] = [58, 37]): TemplateResult {
  return html`<div aria-busy="true">
    ${widths.map((width) => html`<span class="skeleton" style="width: ${width}%"></span>`)}
    <span class="visually-hidden">${said}</span>
  </div>`;
}
