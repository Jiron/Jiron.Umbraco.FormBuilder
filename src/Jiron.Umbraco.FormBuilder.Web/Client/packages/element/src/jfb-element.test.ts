import { describe, expect, it } from 'vitest';
import { defineElement, JfbFormElement } from './jfb-element';

describe('<jfb-form>', () => {
  it('is registered by the script', () => {
    expect(customElements.get('jfb-form')).toBe(JfbFormElement);
    expect(document.createElement('jfb-form')).toBeInstanceOf(JfbFormElement);
  });

  it('is registered once, however often the script runs', () => {
    expect(() => defineElement()).not.toThrow();
    expect(customElements.get('jfb-form')).toBe(JfbFormElement);
  });
});
