import type { Page } from '@playwright/test';

/**
 * The fonts the browser drew an element's text with, each with its count of glyphs, from the DevTools protocol's
 * `CSS.getPlatformFontsForNode`: what a layout spec names when it fails, as where text wraps depends on the machine's
 * fonts (ROADMAP 26.1: GitHub's runner resolves another sans-serif face than the development machine). Empty for an
 * element the selector does not find, or a browser without the protocol.
 */
export async function platformFonts(page: Page, selector: string): Promise<string[]> {
  try {
    const session = await page.context().newCDPSession(page);
    try {
      await session.send('DOM.enable');
      await session.send('CSS.enable');
      const { root } = await session.send('DOM.getDocument', { depth: 0 });
      const { nodeId } = await session.send('DOM.querySelector', { nodeId: root.nodeId, selector });
      if (!nodeId) return [];
      const { fonts } = await session.send('CSS.getPlatformFontsForNode', { nodeId });
      return fonts.map((font) => `${font.familyName} (${String(font.glyphCount)} glyphs)`);
    } finally {
      await session.detach();
    }
  } catch {
    return [];
  }
}

/** The fonts of an element as a phrase for a failure message. */
export async function fontsOf(page: Page, selector: string): Promise<string> {
  const fonts = await platformFonts(page, selector);
  return `the text of ${selector} is drawn in ${fonts.length > 0 ? fonts.join(', ') : 'fonts the browser does not name'}`;
}
