// The backoffice's own tokens (docs/ux/tokens.md, "The backoffice"): the few values UUI's tokens do not cover at AA,
// mixed from UUI's own colours so that they follow Umbraco's light, dark and high-contrast themes. Each element of
// the package adds these to its styles; everything else uses UUI's tokens by name.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const jfbBackofficeTokens = css`
  :host {
    --jfb-bo-color-text-muted: color-mix(in srgb, var(--uui-color-text) 50%, var(--uui-color-border-emphasis));
    --jfb-bo-color-control-border: color-mix(in srgb, var(--uui-color-text) 20%, var(--uui-color-border-emphasis));
    --jfb-bo-color-danger-text: color-mix(in srgb, var(--uui-color-danger) 50%, var(--uui-color-text));
    --jfb-bo-color-positive-text: color-mix(in srgb, var(--uui-color-positive) 50%, var(--uui-color-text));
    --jfb-bo-color-selected: color-mix(in srgb, var(--uui-color-selected) 85%, var(--uui-color-surface));
    --jfb-bo-radius-round: 999px;
    --jfb-bo-type-title: 16px;
    --jfb-bo-type-display: 20px;
    --jfb-bo-font-mono: Consolas, 'Cascadia Mono', Menlo, monospace;
  }
`;

/** Hides an element's text from sight and keeps it for assistive technology. */
export const jfbVisuallyHidden = css`
  .visually-hidden {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }
`;
