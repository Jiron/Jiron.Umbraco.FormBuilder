// Jiron Form Builder's mark and its PNGs (ROADMAP 17.1, ADR-0267, assets/brand.md): scripts/icons.mjs rendered every
// size with a transparent background, the mark uses the brand's colours and nothing else, it is drawn on even units,
// and no Umbraco logo or name is in it.
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

/** A PNG's width, height, colour type and its pixels, unfiltered, from its IHDR and IDAT chunks. */
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
  const [width, height] = [png.readUInt32BE(16), png.readUInt32BE(20)];
  const raw = inflateSync(Buffer.concat(data));
  const stride = width * 4;
  const pixels = Buffer.alloc(stride * height);
  // PNG's five row filters, undone: each byte is stored as its difference from a prediction made of the byte one
  // pixel to the left (a), the one above (b) and the one above left (c).
  for (let y = 0; y < height; y += 1) {
    const filter = raw[y * (stride + 1)];
    for (let x = 0; x < stride; x += 1) {
      const a = x >= 4 ? pixels[y * stride + x - 4] : 0;
      const b = y > 0 ? pixels[(y - 1) * stride + x] : 0;
      const c = x >= 4 && y > 0 ? pixels[(y - 1) * stride + x - 4] : 0;
      const paeth = () => {
        const [pa, pb, pc] = [Math.abs(b - c), Math.abs(a - c), Math.abs(a + b - 2 * c)];
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      };
      const predicted = [0, a, b, (a + b) >> 1, paeth()][filter];
      pixels[y * stride + x] = (raw[y * (stride + 1) + 1 + x] + predicted) & 255;
    }
  }
  /** The pixel at a place given in the mark's 64 units, as '#RRGGBB' and its alpha. */
  const at = (unitX, unitY) => {
    const offset = Math.floor((unitY * height) / 64) * stride + Math.floor((unitX * width) / 64) * 4;
    const hex = [...pixels.subarray(offset, offset + 3)].map((value) => value.toString(16).padStart(2, '0')).join('');
    return { colour: `#${hex.toUpperCase()}`, alpha: pixels[offset + 3] };
  };
  return { width, height, depth: png[24], colourType: png[25], at };
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

/** The brand's colours, as assets/brand.md's table names them. */
function brand() {
  const text = asset('brand.md');
  const colour = (name) => text.match(new RegExp(`\\| \\*\\*${name}\\*\\* \\| \`(#[0-9A-F]{6})\``))?.[1];
  return { blue: colour('Blue'), deep: colour('Deep blue'), marigold: colour('Marigold'), ink: colour('Ink') };
}

test('the mark is rendered at 512, 256, 128 and 64 px, square, with a transparent background', () => {
  assert.deepEqual(SIZES, [512, 256, 128, 64]);
  for (const size of SIZES) {
    const file = pngPath(size);
    assert.ok(existsSync(file), `${file} exists; run node scripts/icons.mjs`);
    const { width, height, depth, colourType, at } = read(file);
    assert.deepEqual([width, height], [size, size], file);
    assert.deepEqual([depth, colourType], [8, 6], `${file} is 8-bit RGBA`);
    for (const [x, y] of [[0, 0], [63, 0], [0, 63], [63, 63], [32, 30]]) {
      assert.equal(at(x, y).alpha, 0, `${file} shows the background at ${x},${y}: the mark stands free, on no tile`);
    }
  }
});

test('every PNG is the ribbon J of assets/logo.svg: the deep bar, the blue stem and hook, the marigold end', () => {
  const { blue, deep, marigold } = brand();
  for (const size of SIZES) {
    const { at } = read(pngPath(size));
    assert.deepEqual(at(30, 12), { colour: deep, alpha: 255 }, `icon-${size}.png: the bar`);
    assert.deepEqual(at(49, 13), { colour: blue, alpha: 255 }, `icon-${size}.png: the fold, the strip's near side`);
    assert.deepEqual(at(46, 30), { colour: blue, alpha: 255 }, `icon-${size}.png: the stem`);
    assert.deepEqual(at(32, 52), { colour: blue, alpha: 255 }, `icon-${size}.png: the hook`);
    assert.deepEqual(at(18, 33), { colour: marigold, alpha: 255 }, `icon-${size}.png: the end`);
    assert.equal(at(13, 12).alpha, 0, `icon-${size}.png: the swallowtail is cut out of the bar's end`);
  }
});

test("the mark uses the brand's three colours, and the one-colour mark the ink", () => {
  const { blue, deep, marigold, ink } = brand();
  assert.ok(blue && deep && marigold && ink, 'brand.md names the four colours');
  // The owner's choices: Ligata's blue, the --accent of ligata.ch (2026-11-13, ADR-0245), on the ribbon J (2026-11-27,
  // ADR-0267).
  assert.equal(blue, '#2457F5', "the stem is Ligata's blue");
  const logo = asset('logo.svg');
  assert.deepEqual(paints(logo), [blue, deep, marigold].sort());
  const fills = [...logo.matchAll(/<path d="[^"]+" fill="(#[0-9A-F]{6})"/g)].map((match) => match[1]);
  assert.deepEqual(fills, [deep, blue, marigold], 'the bar, then the stem over it, then the end over the hook');
  assert.match(asset('logo-mono.svg'), new RegExp(`color="${ink}"`));
  assert.deepEqual(paints(asset('logo-mono.svg')), []);
  assert.deepEqual(paints(asset('icon-section.svg')), [], 'the backoffice icon draws in currentColor');
});

test('the mark is drawn on even units of its 64 unit grid, so every edge is a whole pixel at 32 px', () => {
  const logo = asset('logo.svg');
  assert.match(logo, /viewBox="0 0 64 64"/);
  const paths = [...logo.matchAll(/<path d="([^"]+)"/g)].map((match) => match[1]);
  assert.equal(paths.length, 3, 'the bar, the stem with its hook, and the end');
  for (const path of paths) {
    // An arc's two flags are single digits and not lengths: they are taken out before the numbers are read.
    const numbers = path.replace(/a(\d+) (\d+) 0 [01] [01]/g, 'a$1 $2 ').match(/-?\d*\.?\d+/g) ?? [];
    assert.ok(numbers.length > 0, path);
    for (const number of numbers) assert.equal(Math.abs(Number(number)) % 2, 0,`${number} in "${path}" is an even unit`);
  }
});

test("the one-colour mark is the colour mark's outline, cut at the fold and at the end", () => {
  const mono = asset('logo-mono.svg');
  assert.match(mono, /viewBox="0 0 64 64"/);
  const shapes = [...mono.replace(/<mask[\s\S]*?<\/mask>/g, '').matchAll(/<path d="([^"]+)"([^>]*)>/g)];
  assert.equal(shapes.length, 1, 'one shape');
  assert.match(shapes[0][2], /fill="currentColor"/);
  assert.match(shapes[0][2], /mask="url\(#jfb-mono-cut\)"/);
  // The outline runs through the colour mark's corners: the bar's two ends, the stem's foot, the end's top.
  for (const corner of ['M12 6h40', 'l6-6z', 'V28h12', 'a20 20 0 0 1-40 0']) {
    assert.ok(shapes[0][1].includes(corner), `the outline has ${corner}`);
  }
  const cuts = [...(mono.match(/<mask[\s\S]*?<\/mask>/)?.[0] ?? '').matchAll(/<path d="([^"]+)"[^>]*stroke="#000000"/g)];
  assert.deepEqual(cuts.map((cut) => cut[1]), ['M38 20 54 4', 'M10 38h15'], 'the fold, then the end');
});

test('no Umbraco logo or name is in the artwork', () => {
  for (const name of ['logo.svg', 'logo-mono.svg', 'icon-section.svg']) {
    assert.doesNotMatch(asset(name).replace(/<!--[\s\S]*?-->/g, ''), /umbraco/i, name);
  }
});
