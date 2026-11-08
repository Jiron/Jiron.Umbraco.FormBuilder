// The backoffice's own tokens (docs/ux/tokens.md, "The backoffice"): the few values UUI's tokens do not cover at AA,
// mixed from UUI's own colours so that they follow Umbraco's light, dark and high-contrast themes. Each element of
// the package adds these to its styles; everything else uses UUI's tokens by name.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const jfbBackofficeTokens = css`
  :host {
    --jfb-bo-color-text-muted: color-mix(in srgb, var(--uui-color-text) 50%, var(--uui-color-border-emphasis));
    --jfb-bo-color-control-border: color-mix(in srgb, var(--uui-color-text) 20%, var(--uui-color-border-emphasis));
    --jfb-bo-color-danger-text: color-mix(in srgb, var(--uui-color-danger) 50%, var(--uui-color-text));
    --jfb-bo-color-warning-text: color-mix(in srgb, var(--uui-color-warning-standalone) 50%, var(--uui-color-text));
    --jfb-bo-color-positive-text: color-mix(in srgb, var(--uui-color-positive) 50%, var(--uui-color-text));
    --jfb-bo-color-selected: color-mix(in srgb, var(--uui-color-selected) 85%, var(--uui-color-surface));
    --jfb-bo-color-current: color-mix(in srgb, var(--uui-color-current) 85%, var(--uui-color-surface));
    --jfb-bo-color-focus: color-mix(in srgb, var(--uui-color-focus) 70%, var(--uui-color-text));
    --jfb-bo-radius-round: 999px;
    --jfb-bo-type-title: 16px;
    --jfb-bo-type-display: 20px;
    --jfb-bo-font-mono: Consolas, 'Cascadia Mono', Menlo, monospace;
    --jfb-bo-motion-fast: 120ms;
    --jfb-bo-motion-ease: cubic-bezier(0.23, 1, 0.32, 1);
    /*
     * A filled button's label. Umbraco's dark theme gives it a light grey that meets 4.3:1 on the positive, default and
     * danger fills, under AA; white meets 5:1 there, and is what the light and high-contrast themes give it already, so
     * the section's buttons, Umbraco's footer and dialogs inside its elements among them, say it in white (ADR-0182).
     */
    --uui-color-positive-contrast: #fff;
    --uui-color-default-contrast: #fff;
    --uui-color-danger-contrast: #fff;
    /* The same for a selected item's label, the canvas's own accent among them. */
    --uui-color-selected-contrast: #fff;
    /* A warning's label, as on the state chip of a form with changes to publish: black, as in the other two themes. */
    --uui-color-warning-contrast: #000;
  }

  /*
   * UUI's own controls in the package's elements draw the section's focus ring: UUI's is 2.5:1 on the dark theme's
   * surface. Only these leaves take it, never an element of the package, whose own tokens mix it from UUI's again.
   */
  :is(uui-button, uui-input, uui-textarea, uui-select, uui-toggle, uui-checkbox, uui-ref-node, uui-menu-item) {
    --uui-color-focus: var(--jfb-bo-color-focus);
  }
`;

/**
 * Hides an element's text from sight and keeps it for assistive technology. It wins over any sizing rule, as a hidden
 * control is still a control the form styles size (a file input drawn at full width stuck out of its screen, 12.6).
 */
export const jfbVisuallyHidden = css`
  .visually-hidden {
    position: absolute !important;
    inline-size: 1px !important;
    block-size: 1px !important;
    overflow: hidden !important;
    clip-path: inset(50%) !important;
    white-space: nowrap !important;
  }
`;
