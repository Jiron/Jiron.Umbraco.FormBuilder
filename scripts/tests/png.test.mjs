// The screenshots' PNG re-encoder (ROADMAP 17.3): the pixels stay exactly as they were, an opaque alpha channel goes,
// a transparent one stays, every filter decodes, and a PNG of another kind comes back untouched.
// Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { test } from 'node:test';
import { crc32, deflateSync, inflateSync } from 'node:zlib';
import { decodePng, encodePng, optimizePng, withoutOpaqueAlpha } from '../png.mjs';

const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function chunk(type, data) {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(data.length, 0);
  head.write(type, 4, 'latin1');
  const tail = Buffer.alloc(4);
  tail.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])) >>> 0, 0);
  return Buffer.concat([head, data, tail]);
}

/** A PNG written the plain way: every row unfiltered (filter 0), deflated at a low level. */
function plainPng(width, height, channels, pixel, { depth = 8, extra = [] } = {}) {
  const stride = width * channels;
  const raw = Buffer.alloc(height * (stride + 1));
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const value = pixel(x, y);
      for (let c = 0; c < channels; c++) raw[y * (stride + 1) + 1 + x * channels + c] = value[c];
    }
  }
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = depth;
  header[9] = channels === 4 ? 6 : 2;
  return Buffer.concat([
    SIGNATURE,
    chunk('IHDR', header),
    ...extra,
    chunk('IDAT', deflateSync(raw, { level: 1 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

// A picture with flat areas, gradients and noise, so each filter wins somewhere.
const picture = (x, y) => [
  x < 20 ? 245 : (x * 7) & 0xff,
  y < 10 ? 245 : (y * 13 + x) & 0xff,
  (x * y * 31 + 7) & 0xff,
  255,
];

test('an opaque screenshot keeps every pixel, loses its alpha channel and shrinks', () => {
  const png = plainPng(64, 40, 4, picture, { extra: [chunk('tEXt', Buffer.from('Software\0browser', 'latin1'))] });
  const before = decodePng(png);
  const smaller = optimizePng(png);
  const after = decodePng(smaller);
  assert.ok(smaller.length < png.length, `${smaller.length} < ${png.length}`);
  assert.equal(after.channels, 3);
  assert.deepEqual(after.pixels, withoutOpaqueAlpha(before).pixels);
  assert.equal(smaller.includes(Buffer.from('tEXt', 'latin1')), false);
});

test('a picture with transparency keeps its alpha channel and every pixel', () => {
  const png = plainPng(32, 32, 4, (x, y) => [x * 8, y * 8, 120, (x + y) % 2 === 0 ? 255 : 90]);
  const after = decodePng(encodePng(decodePng(png)));
  assert.equal(after.channels, 4);
  assert.deepEqual(after.pixels, decodePng(png).pixels);
});

test('rows written with each of the five filters decode to the same pixels', () => {
  const image = { width: 50, height: 30, channels: 3, pixels: Buffer.alloc(50 * 30 * 3) };
  for (let y = 0; y < 30; y++)
    for (let x = 0; x < 50; x++) image.pixels.set(picture(x, y).slice(0, 3), (y * 50 + x) * 3);
  const encoded = encodePng(image);
  assert.deepEqual(decodePng(encoded).pixels, image.pixels);
  // The re-encoder chose several filters for this picture, so the decoder met several.
  const at = encoded.indexOf(Buffer.from('IDAT', 'latin1'));
  const raw = inflateSync(encoded.subarray(at + 4, at + 4 + encoded.readUInt32BE(at - 4)));
  const filters = new Set(Array.from({ length: 30 }, (_, y) => raw[y * (50 * 3 + 1)]));
  assert.ok(filters.size >= 3, `filters used: ${[...filters].join(', ')}`);
});

test('the smaller of the unfiltered and the filtered encoding wins, and both decode to the same pixels', () => {
  // Bands of flat colour with a few edges, like a screenshot of a page.
  const flat = (x, y) => (y % 40 < 30 ? (x % 200 < 150 ? [250, 250, 250, 255] : [15, 110, 92, 255]) : [22, 24, 29, 255]);
  const png = plainPng(400, 200, 4, flat);
  const pixels = withoutOpaqueAlpha(decodePng(png));
  const none = encodePng(pixels, 'none');
  assert.deepEqual(decodePng(none).pixels, pixels.pixels);
  const adaptive = encodePng(pixels, 'adaptive');
  assert.deepEqual(decodePng(adaptive).pixels, pixels.pixels);
  assert.equal(optimizePng(png).length, Math.min(none.length, adaptive.length, png.length));
});

test('a PNG with a chunk that changes how it looks comes back as it was', () => {
  for (const type of ['tRNS', 'gAMA', 'iCCP']) {
    const png = plainPng(16, 16, 3, picture, { extra: [chunk(type, Buffer.alloc(6))] });
    assert.equal(optimizePng(png), png, type);
  }
});

test('a PNG of another kind comes back as it was, and a file that is no PNG is refused', () => {
  const sixteen = plainPng(4, 4, 3, () => [1, 2, 3], { depth: 16 });
  assert.equal(optimizePng(sixteen), sixteen);
  assert.throws(() => optimizePng(Buffer.from('GIF89a')), /not a PNG/);
});
