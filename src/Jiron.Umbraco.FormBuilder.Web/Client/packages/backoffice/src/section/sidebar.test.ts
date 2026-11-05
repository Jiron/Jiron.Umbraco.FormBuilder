// The section's sidebar (docs/ux/information-architecture.md, section 1): each item tells itself apart at a glance.
import { describe, expect, it } from 'vitest';
import { SIDEBAR } from './sidebar';

describe('the sidebar', () => {
  it('gives every item an icon of its own, so no two places look alike', () => {
    const icons = SIDEBAR.flatMap((group) => group.items.map((item) => item.icon));
    expect(new Set(icons).size).toBe(icons.length);
  });
});
