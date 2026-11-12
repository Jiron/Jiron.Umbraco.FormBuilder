// Umbraco's switch for its own controls' focus outlines stops at the sheet's edge (ADR-0243). Umbraco sets
// --uui-show-focus-outline on the body at the first press of a pointer after a load or a Tab, and the property is
// inherited: without a value of the sheet's own, that one change restyled every element of the form drawn, most of the
// 9 to 18 ms of restyling it added to the first click on a form of 300 fields. The sheet's own rings never read it.
import { describe, expect, it } from 'vitest';
import { canvasChrome } from './canvas.styles';

const SWITCH = '--uui-show-focus-outline';

/** The declarations of each rule whose selector is exactly the one given, comments left out. */
function blocksOf(text: string, selector: string): string[] {
  const stripped = text.replace(/\/\*[\s\S]*?\*\//g, '');
  const blocks: string[] = [];
  const pattern = /([^{};]+)\{([^{}]*)\}/g;
  for (const match of stripped.matchAll(pattern)) if ((match[1] ?? '').trim() === selector) blocks.push(match[2] ?? '');
  return blocks;
}

describe("Umbraco's focus outline switch on the canvas", () => {
  it("has a value of the sheet's own, so a change on the body stops at the sheet", () => {
    const values = blocksOf(canvasChrome.cssText, '.bo-viewport').flatMap((block) =>
      [...block.matchAll(new RegExp(`${SWITCH}:\\s*([^;]+);`, 'g'))].map((match) => match[1]?.trim()),
    );
    // Shown: a control of Umbraco's on the sheet keeps its ring, as the sheet's own controls keep theirs.
    expect(values).toEqual(['1']);
  });

  it("is read by none of the canvas's own rules, so its value there changes nothing the canvas draws", () => {
    expect(canvasChrome.cssText.replace(/\/\*[\s\S]*?\*\//g, '')).not.toMatch(new RegExp(`var\\(\\s*${SWITCH}`));
  });
});
