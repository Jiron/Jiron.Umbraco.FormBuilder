// Makes the screenshots' PNGs smaller without changing a pixel (ROADMAP 17.3), with nothing but Node's zlib: an alpha
// channel that is opaque everywhere is dropped, and the pixels are deflated at the highest level twice, unfiltered and
// with each row's best filter, keeping the smaller: a screenshot with text usually comes out smaller unfiltered, a
// picture of bands or gradients with filters. Only 8-bit RGB and RGBA images without interlacing are rewritten, which
// is what a browser's screenshot is, and only when no chunk changes how their pixels look (a transparent colour, a
// gamma, a colour profile or primaries); any other PNG comes back as it was. Text, sRGB and physical size chunks are
// left out, so a viewer reads the pixels as sRGB, as it would anyway.
import { crc32, deflateSync, inflateSync, constants } from 'node:zlib';

const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

/** The chunks that change how the pixels look, which a rewrite would drop: such a PNG is left as it is. */
const LOOKS = new Set(['tRNS', 'gAMA', 'iCCP', 'cHRM', 'sBIT']);

/** The chunks of a PNG, each with its type and data; throws on a file that is no PNG. */
function chunksOf(png) {
  if (png.length < SIGNATURE.length || !png.subarray(0, SIGNATURE.length).equals(SIGNATURE)) throw new Error('not a PNG');
  const chunks = [];
  let at = SIGNATURE.length;
  while (at + 12 <= png.length) {
    const length = png.readUInt32BE(at);
    const type = png.toString('latin1', at + 4, at + 8);
    chunks.push({ type, data: png.subarray(at + 8, at + 8 + length) });
    at += 12 + length;
    if (type === 'IEND') break;
  }
  return chunks;
}

const paeth = (a, b, c) => {
  const p = a + b - c;
  const pa = Math.abs(p - a);
  const pb = Math.abs(p - b);
  const pc = Math.abs(p - c);
  return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
};

/**
 * The pixels of an 8-bit RGB or RGBA PNG without interlacing: `{ width, height, channels, pixels }`, the rows one after
 * another without filter bytes; null for any other kind of PNG.
 */
export function decodePng(png) {
  const chunks = chunksOf(png);
  const header = chunks.find((chunk) => chunk.type === 'IHDR')?.data;
  if (!header) throw new Error('no IHDR');
  const width = header.readUInt32BE(0);
  const height = header.readUInt32BE(4);
  const [depth, colour, , , interlace] = header.subarray(8, 13);
  if (depth !== 8 || interlace !== 0 || (colour !== 2 && colour !== 6)) return null;
  if (chunks.some((chunk) => LOOKS.has(chunk.type))) return null;
  const channels = colour === 6 ? 4 : 3;
  const stride = width * channels;
  const raw = inflateSync(Buffer.concat(chunks.filter((chunk) => chunk.type === 'IDAT').map((chunk) => chunk.data)));
  if (raw.length !== height * (stride + 1)) throw new Error('the image data has the wrong length');
  const pixels = Buffer.alloc(height * stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const row = y * stride;
    const source = y * (stride + 1) + 1;
    for (let x = 0; x < stride; x++) {
      const value = raw[source + x];
      const left = x >= channels ? pixels[row + x - channels] : 0;
      const up = y > 0 ? pixels[row - stride + x] : 0;
      const upLeft = y > 0 && x >= channels ? pixels[row - stride + x - channels] : 0;
      let out;
      if (filter === 0) out = value;
      else if (filter === 1) out = value + left;
      else if (filter === 2) out = value + up;
      else if (filter === 3) out = value + ((left + up) >> 1);
      else if (filter === 4) out = value + paeth(left, up, upLeft);
      else throw new Error(`unknown filter ${filter}`);
      pixels[row + x] = out & 0xff;
    }
  }
  return { width, height, channels, pixels };
}

/** One row filtered each of the five ways, and the one whose bytes, read as signed, add up to the least. */
function bestRow(pixels, y, stride, channels) {
  const row = y * stride;
  let best = null;
  let bestScore = Infinity;
  for (let filter = 0; filter <= 4; filter++) {
    const out = Buffer.alloc(stride + 1);
    out[0] = filter;
    let score = 0;
    for (let x = 0; x < stride; x++) {
      const value = pixels[row + x];
      const left = x >= channels ? pixels[row + x - channels] : 0;
      const up = y > 0 ? pixels[row - stride + x] : 0;
      const upLeft = y > 0 && x >= channels ? pixels[row - stride + x - channels] : 0;
      const predicted =
        filter === 0 ? 0 : filter === 1 ? left : filter === 2 ? up : filter === 3 ? (left + up) >> 1 : paeth(left, up, upLeft);
      const byte = (value - predicted) & 0xff;
      out[x + 1] = byte;
      score += byte < 128 ? byte : 256 - byte;
      if (score >= bestScore) break;
    }
    if (score < bestScore) {
      bestScore = score;
      best = out;
    }
  }
  return best;
}

function chunk(type, data) {
  const head = Buffer.alloc(8);
  head.writeUInt32BE(data.length, 0);
  head.write(type, 4, 'latin1');
  const tail = Buffer.alloc(4);
  tail.writeUInt32BE(crc32(Buffer.concat([head.subarray(4), data])) >>> 0, 0);
  return Buffer.concat([head, data, tail]);
}

/** A PNG of exactly these pixels: each row with its best filter (`adaptive`), or every row unfiltered (`none`). */
export function encodePng({ width, height, channels, pixels }, filter = 'adaptive') {
  const stride = width * channels;
  const rows = [];
  for (let y = 0; y < height; y++) {
    rows.push(
      filter === 'none'
        ? Buffer.concat([Buffer.alloc(1), pixels.subarray(y * stride, (y + 1) * stride)])
        : bestRow(pixels, y, stride, channels),
    );
  }
  const data = deflateSync(Buffer.concat(rows), { level: 9, memLevel: 9, strategy: constants.Z_DEFAULT_STRATEGY });
  const header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0);
  header.writeUInt32BE(height, 4);
  header[8] = 8;
  header[9] = channels === 4 ? 6 : 2;
  return Buffer.concat([SIGNATURE, chunk('IHDR', header), chunk('IDAT', data), chunk('IEND', Buffer.alloc(0))]);
}

/** The pixels without their alpha channel when it is opaque everywhere; otherwise as they are. */
export function withoutOpaqueAlpha(image) {
  if (image.channels !== 4) return image;
  for (let at = 3; at < image.pixels.length; at += 4) if (image.pixels[at] !== 255) return image;
  const rgb = Buffer.alloc((image.pixels.length / 4) * 3);
  for (let from = 0, to = 0; from < image.pixels.length; from += 4, to += 3) {
    rgb[to] = image.pixels[from];
    rgb[to + 1] = image.pixels[from + 1];
    rgb[to + 2] = image.pixels[from + 2];
  }
  return { ...image, channels: 3, pixels: rgb };
}

/** The PNG made smaller with the same pixels, or the PNG as it was when it is of another kind or would not shrink. */
export function optimizePng(png) {
  const image = decodePng(png);
  if (!image) return png;
  const pixels = withoutOpaqueAlpha(image);
  return [encodePng(pixels, 'none'), encodePng(pixels, 'adaptive')].reduce(
    (best, candidate) => (candidate.length < best.length ? candidate : best),
    png,
  );
}
