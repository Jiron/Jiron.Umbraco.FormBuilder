/**
 * A name typed instead of a signature (ROADMAP 6.4n, ADR-0103, ADR-0153), the same as Core's `TypedSignature`, held to it
 * by the shared vectors of tests/vectors/typed-names.json: the name as it is drawn and stored, and the PNG that carries it.
 * A module of its own, `@jfb/logic/signature`, as only a form with a signature loads it.
 */

const KEYWORD = 'jfb:typed-name';
/** Where a PNG's first chunk after its header starts: the eight bytes every PNG starts with, and the header's 25. */
const AFTER_HEADER = 33;
/** The joiners, which shape other scripts and stay in a name. */
const JOINERS = new Set([0x200c, 0x200d]);

/** The name as it is drawn and stored: NFC, without control and format characters but the joiners, each run of white space one space, trimmed; null when nothing is left. */
export function cleanName(typed: string): string | null {
  let cleaned = '';
  let space = false;
  for (const letter of typed.normalize('NFC')) {
    if (/\p{White_Space}/u.test(letter)) {
      space = cleaned.length > 0;
    } else if (!/\p{Cc}/u.test(letter) && (!/\p{Cf}/u.test(letter) || JOINERS.has(letter.codePointAt(0) ?? 0))) {
      cleaned += (space ? ' ' : '') + letter;
      space = false;
    }
  }
  return cleaned.length === 0 ? null : cleaned.normalize('NFC');
}

let table: Uint32Array | undefined;

function crcOf(bytes: Uint8Array): number {
  table ??= Uint32Array.from({ length: 256 }, (_, index) => {
    let value = index;
    for (let bit = 0; bit < 8; bit += 1) value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1;
    return value;
  });
  let crc = 0xffffffff;
  for (const byte of bytes) crc = (table[(crc ^ byte) & 0xff] as number) ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

/** A PNG with the name, in UTF-8, in an `iTXt` chunk right after its header: uncompressed, without a language tag, as the server writes it. */
export function pngWithName(png: Uint8Array, text: Uint8Array): Uint8Array {
  const keyword = Uint8Array.from(`iTXt${KEYWORD}`, (letter) => letter.charCodeAt(0));
  const typed = new Uint8Array(keyword.length + 5 + text.length);
  typed.set(keyword);
  typed.set(text, keyword.length + 5);

  const named = new Uint8Array(png.length + typed.length + 8);
  const view = new DataView(named.buffer);
  named.set(png.subarray(0, AFTER_HEADER));
  view.setUint32(AFTER_HEADER, typed.length - 4);
  named.set(typed, AFTER_HEADER + 4);
  view.setUint32(AFTER_HEADER + 4 + typed.length, crcOf(typed));
  named.set(png.subarray(AFTER_HEADER), AFTER_HEADER + 8 + typed.length);
  return named;
}
