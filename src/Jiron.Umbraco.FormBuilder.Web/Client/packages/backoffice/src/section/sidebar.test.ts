// The section's sidebar (docs/ux/information-architecture.md, section 1; ADR-0257): each place and each tab tells itself
// apart at a glance, and a workspace knows its place.
import { describe, expect, it } from 'vitest';
import { PLACES, placeOf } from './sidebar';

describe('the sidebar', () => {
  it('gives every place an icon of its own, and every tab one of its own, so no two look alike', () => {
    const places = PLACES.map((place) => place.icon);
    const tabs = PLACES.flatMap((place) => place.tabs.map((tab) => tab.icon));
    expect(new Set(places).size).toBe(places.length);
    expect(new Set(tabs).size).toBe(tabs.length);
  });

  it("finds a workspace's place by its entity type: a tab's screen, or an editor a tab opens", () => {
    expect(placeOf('jfb-settings-email')?.id).toBe('settings');
    expect(placeOf('jfb-spam')?.id).toBe('inbox');
    expect(placeOf('jfb-privacy-collection')?.id).toBe('privacy');
    expect(['jfb-option-list', 'jfb-theme', 'jfb-email-layout'].map((type) => placeOf(type)?.id)).toEqual([
      'library',
      'library',
      'library',
    ]);
    // A form and an entry belong to the tree, not to a place.
    expect([placeOf('jfb-form'), placeOf('jfb-entry')]).toEqual([undefined, undefined]);
  });
});
