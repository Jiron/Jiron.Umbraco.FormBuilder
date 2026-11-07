// A small button's press (ADR-0182, ADR-0206): it shrinks at once on the motion tokens and moves not at all with reduced
// motion, and each of the Phase 10 screens gives it to its own small buttons.
import type { CSSResultGroup } from '@umbraco-cms/backoffice/external/lit';
import { describe, expect, it } from 'vitest';
import { ActionListElement } from '../designer/actions/action-list.element';
import { ActionModalElement } from '../designer/actions/action.modal.element';
import { EmailPreviewElement } from '../designer/actions/email-preview.element';
import { FormHistoryViewElement } from '../designer/history/form-history.view';
import { PreviewModalElement } from '../designer/visitor-preview/preview.modal.element';
import { pressOf } from './press';

/** An element's styles as one text, without the spaces that formatting leaves. */
const textOf = (styles: CSSResultGroup | undefined): string => {
  const texts: string[] = [];
  const read = (style: unknown): void => {
    if (Array.isArray(style)) for (const one of style) read(one);
    else if (style && typeof style === 'object' && 'cssText' in style) texts.push(String(style.cssText));
  };
  read(styles);
  return texts.join('\n').replace(/\s+/g, ' ');
};

describe("a small button's press", () => {
  it('shrinks at once on the motion tokens, and moves nothing with reduced motion', () => {
    const text = textOf(pressOf('.more'));
    expect(text).toContain('.more { transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease); }');
    expect(text).toContain('.more:active { transform: scale(0.97); }');
    expect(text).toMatch(
      /@media \(prefers-reduced-motion: reduce\) \{ \.more \{ transition: none; \} \.more:active \{ transform: none; \} \}/,
    );
  });

  it.each([
    [ActionListElement, '.more'],
    [ActionModalElement, '.remove'],
    [EmailPreviewElement, '.schemes > button'],
    [FormHistoryViewElement, '.more'],
    [PreviewModalElement, '.widths > button'],
  ] as const)('is given by %o to %s', (element, selector) => {
    expect(textOf(element.styles)).toContain(`${selector}:active { transform: scale(0.97); }`);
  });
});
