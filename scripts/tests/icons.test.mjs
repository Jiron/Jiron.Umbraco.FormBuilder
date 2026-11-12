// Jiron Form Builder's mark and its PNGs (ROADMAP 17.1, assets/brand.md): scripts/icons.mjs rendered every size with
// a transparent background, the mark uses the brand's colours and nothing else, and no Umbraco logo or name is in it.
// Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { inflateSync } from 'node:zlib';
import { pngPath, SIZES } from '../icons.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const asset = (name) => readFileSync(join(root, 'assets', name), 'utf8');

/** A PNG's width, height, colour type and the alpha of its top left pixel, from its IHDR and IDAT chunks. */
function read(file) {
  const png = readFileSync(file);
  assert.deepEqual([...png.subarray(0, 8)], [137, 80, 78, 71, 13, 10, 26, 10], `${file} is a PNG`);
  assert.equal(png.toString('latin1', 12, 16), 'IHDR');
  const data = [];
  for (let at = 8; at < png.length; ) {
    const length = png.readUInt32BE(at);
    if (png.toString('latin1', at + 4, at + 8) === 'IDAT') data.push(png.subarray(at + 8, at + 8 + length));
    at += 12 + length;
  }
  // Every filter predicts the first pixel of the first row from zeros, so its bytes are stored as they are.
  const pixels = inflateSync(Buffer.concat(data));
  return {
    width: png.readUInt32BE(16),
    height: png.readUInt32BE(20),
    depth: png[24],
    colourType: png[25],
    cornerAlpha: pixels[4],
  };
}

/** The colours an SVG paints with, its masks left out. */
const paints = (svg) =>
  [
    ...new Set(
      [...svg.replace(/<mask[\s\S]*?<\/mask>/g, '').matchAll(/(?:fill|stroke)="(#[0-9A-Fa-f]{6})"/g)].map((match) =>
        match[1].toUpperCase(),
      ),
    ),
  ].sort();

test('the mark is rendered at 512, 256, 128 and 64 px, square, with its corners transparent', () => {
  assert.deepEqual(SIZES, [512, 256, 128, 64]);
  for (const size of SIZES) {
    const file = pngPath(size);
    assert.ok(existsSync(file), `${file} exists; run node scripts/icons.mjs`);
    const { width, height, depth, colourType, cornerAlpha } = read(file);
    assert.deepEqual([width, height], [size, size], file);
    assert.deepEqual([depth, colourType], [8, 6], `${file} is 8-bit RGBA`);
    assert.equal(cornerAlpha, 0, `${file} shows the background past the card's round corner`);
  }
});

test("the mark uses the brand's two colours, and the one-colour mark the ink", () => {
  const brand = asset('brand.md');
  const colour = (name) => brand.match(new RegExp(`\\| \\*\\*${name}\\*\\* \\| \`(#[0-9A-F]{6})\``))?.[1];
  const [evergreen, marigold, ink] = [colour('Evergreen'), colour('Marigold'), colour('Ink')];
  assert.ok(evergreen && marigold && ink, 'brand.md names the three colours');
  assert.deepEqual(paints(asset('logo.svg')), [evergreen, marigold].sort());
  assert.match(asset('logo.svg'), new RegExp(`fill="${evergreen}"`), 'the card is evergreen');
  assert.match(asset('logo.svg'), new RegExp(`stroke="${marigold}"`), 'the check is marigold');
  assert.match(asset('logo-mono.svg'), new RegExp(`color="${ink}"`));
  assert.deepEqual(paints(asset('logo-mono.svg')), []);
  assert.deepEqual(paints(asset('icon-section.svg')), [], 'the backoffice icon draws in currentColor');
});

test("the one-colour mark cuts out the colour mark's two fields and its check", () => {
  const fields = (svg) => [...svg.matchAll(/<rect x="(\d+)" y="(\d+)" width="(\d+)" height="(\d+)"[^>]*fill="#000000"/g)].map((m) => m.slice(1).join(' '));
  const check = (svg) => svg.match(/<path d="([^"]+)"/)?.[1];
  assert.equal(fields(asset('logo.svg')).length, 2, 'two fields cut out of the colour mark');
  assert.deepEqual(fields(asset('logo-mono.svg')), fields(asset('logo.svg')), 'the same fields');
  assert.ok(check(asset('logo.svg')), 'the colour mark has its check');
  assert.equal(check(asset('logo-mono.svg')), check(asset('logo.svg')), 'the same check mark, cut out');
});

test('no Umbraco logo or name is in the artwork', () => {
  for (const name of ['logo.svg', 'logo-mono.svg', 'icon-section.svg']) {
    assert.doesNotMatch(asset(name).replace(/<!--[\s\S]*?-->/g, ''), /umbraco/i, name);
  }
});
