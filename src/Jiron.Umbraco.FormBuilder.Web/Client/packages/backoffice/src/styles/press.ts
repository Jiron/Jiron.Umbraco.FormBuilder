// How a small button of the package's own answers a press (docs/ux/tokens.md, "Motion"; ADR-0182, ADR-0206): it
// shrinks a little at once, on the motion tokens, as the designer's own buttons do; with reduced motion nothing moves.
// An element adds the rule for its small buttons to its styles: a "...", a remove button, a choice of a few.
import { css, unsafeCSS, type CSSResult } from '@umbraco-cms/backoffice/external/lit';

/** The press for the buttons a selector names, which an element's own styles define. */
export function pressOf(selector: string): CSSResult {
  const buttons = unsafeCSS(selector);
  return css`
    ${buttons} {
      transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
    }

    ${buttons}:active {
      transform: scale(0.97);
    }

    @media (prefers-reduced-motion: reduce) {
      ${buttons} {
        transition: none;
      }

      ${buttons}:active {
        transform: none;
      }
    }
  `;
}
