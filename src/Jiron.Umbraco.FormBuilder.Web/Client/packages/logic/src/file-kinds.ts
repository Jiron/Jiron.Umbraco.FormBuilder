/**
 * The kinds of file a file's first bytes show (ROADMAP 6.4m, ADR-0152), the same as Core's `FileKinds`, held to it by the
 * shared vectors of tests/vectors/file-kinds.json. The browser checks them before it sends a file, so a file the server
 * would refuse costs no upload; the server checks them again, as anything in a browser can be changed. A module of its
 * own, `@jfb/logic/files`, as only a form with a file field loads it.
 */

/** How many of a file's first bytes the checks read: as many as the server's. */
export const HEAD_LENGTH = 512;

const ascii = (text: string): number[] => [...text].map((letter) => letter.charCodeAt(0));

const startsWith = (head: Uint8Array, start: readonly number[], at = 0): boolean =>
  head.length >= at + start.length && start.every((byte, index) => head[at + index] === byte);

const text = (head: Uint8Array, start: string, at = 0): boolean => startsWith(head, ascii(start), at);

const JPEG = [0xff, 0xd8, 0xff];
const PNG = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
const COMPOUND = [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1];
const ZIP_ENTRY = [0x50, 0x4b, 0x03, 0x04];
const ZIP_EMPTY = [0x50, 0x4b, 0x05, 0x06];
const ZIP_SPANNED = [0x50, 0x4b, 0x07, 0x08];
const SEVEN_ZIP = [0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c];
const RAR = [0x52, 0x61, 0x72, 0x21, 0x1a, 0x07];
const RTF = ascii('{\\rtf');
const GZIP = [0x1f, 0x8b];

const AVIF_BRANDS = new Set(['avif', 'avis']);
const HEIF_BRANDS = new Set(['heic', 'heix', 'hevc', 'hevx', 'heim', 'heis', 'hevm', 'hevs', 'mif1', 'msf1']);

/** An ISO media file whose ftyp box names one of the brands, as its major brand or among its compatible ones. */
function hasBrand(head: Uint8Array, brands: ReadonlySet<string>): boolean {
  if (head.length < 16 || !text(head, 'ftyp', 4)) return false;
  const declared = ((head[0] ?? 0) * 2 ** 24 + ((head[1] ?? 0) << 16) + ((head[2] ?? 0) << 8) + (head[3] ?? 0)) >>> 0;
  const size = Math.min(declared, head.length);
  for (let at = 8; at + 4 <= size; at += at === 8 ? 8 : 4) {
    if (brands.has(String.fromCharCode(...head.subarray(at, at + 4)))) return true;
  }
  return false;
}

const isZipEntry = (head: Uint8Array): boolean => startsWith(head, ZIP_ENTRY);
const isZip = (head: Uint8Array): boolean =>
  isZipEntry(head) || startsWith(head, ZIP_EMPTY) || startsWith(head, ZIP_SPANNED);
const isCompound = (head: Uint8Array): boolean => startsWith(head, COMPOUND);
const isJpeg = (head: Uint8Array): boolean => startsWith(head, JPEG);
const isHeif = (head: Uint8Array): boolean => hasBrand(head, HEIF_BRANDS);
/** Text: no NUL byte, unless the bytes start with a UTF-16 byte-order mark; an empty file is text too. */
const isText = (head: Uint8Array): boolean =>
  startsWith(head, [0xff, 0xfe]) || startsWith(head, [0xfe, 0xff]) || !head.includes(0);

const KINDS = new Map<string, (head: Uint8Array) => boolean>([
  ['jpg', isJpeg],
  ['jpeg', isJpeg],
  ['png', (head) => startsWith(head, PNG)],
  ['gif', (head) => text(head, 'GIF87a') || text(head, 'GIF89a')],
  ['webp', (head) => text(head, 'RIFF') && text(head, 'WEBP', 8)],
  ['avif', (head) => hasBrand(head, AVIF_BRANDS)],
  ['heic', isHeif],
  ['heif', isHeif],
  ['pdf', (head) => text(head, '%PDF-')],
  ['doc', isCompound],
  ['xls', isCompound],
  ['ppt', isCompound],
  ['docx', isZipEntry],
  ['xlsx', isZipEntry],
  ['pptx', isZipEntry],
  ['odt', isZipEntry],
  ['ods', isZipEntry],
  ['odp', isZipEntry],
  ['rtf', (head) => startsWith(head, RTF)],
  ['txt', isText],
  ['csv', isText],
  ['zip', isZip],
  ['7z', (head) => startsWith(head, SEVEN_ZIP)],
  ['rar', (head) => startsWith(head, RAR)],
  ['gz', (head) => startsWith(head, GZIP)],
  ['tar', (head) => text(head, 'ustar', 257)],
]);

/** The extensions that name a page or a drawing that runs script. */
const MARKUP_EXTENSIONS = new Set(['html', 'htm', 'xhtml', 'shtml', 'xml', 'svg']);

const OPENS_AS_MARKUP = [
  '<!doctype html',
  '<html',
  '<head',
  '<body',
  '<script',
  '<svg',
  '<iframe',
  '<object',
  '<embed',
  '<?xml',
  '<?php',
  '<%',
].map(ascii);

const HOLDS_MARKUP = ['<!doctype html', '<html', '<script', '<svg', '<iframe', '<?php'];

/** Whether the bytes are a program or a script: a Windows, ELF or Mach-O binary, or a script that names its interpreter. */
export function isExecutable(head: Uint8Array): boolean {
  return (
    text(head, 'MZ') ||
    startsWith(head, [0x7f, 0x45, 0x4c, 0x46]) ||
    startsWith(head, [0xfe, 0xed, 0xfa, 0xce]) ||
    startsWith(head, [0xfe, 0xed, 0xfa, 0xcf]) ||
    startsWith(head, [0xce, 0xfa, 0xed, 0xfe]) ||
    startsWith(head, [0xcf, 0xfa, 0xed, 0xfe]) ||
    startsWith(head, [0xca, 0xfe, 0xba, 0xbe]) ||
    text(head, '#!')
  );
}

/** Whether the bytes are the kind the extension names; true for an extension of no kind, whose bytes nothing holds to. */
export function matchesKind(extension: string, head: Uint8Array): boolean {
  return KINDS.get(extension)?.(head) ?? true;
}

/** The bytes as a browser may read them, in lowercase: without a byte-order mark, and without zero bytes. */
function readable(head: Uint8Array): number[] {
  const from =
    startsWith(head, [0xff, 0xfe]) || startsWith(head, [0xfe, 0xff]) ? 2 : startsWith(head, [0xef, 0xbb, 0xbf]) ? 3 : 0;
  const read: number[] = [];
  for (const unit of head.subarray(from)) if (unit !== 0) read.push(unit >= 65 && unit <= 90 ? unit + 32 : unit);
  return read;
}

/**
 * Whether the bytes hide a page behind another name: a file whose extension does not name markup and that opens as a page,
 * a drawing or a server's script, and a binary kind with one inside its first bytes. Text may mention markup after its
 * start.
 */
export function hidesMarkup(extension: string, head: Uint8Array): boolean {
  if (MARKUP_EXTENSIONS.has(extension)) return false;
  const read = readable(head);
  const at = read.findIndex(
    (byte) => byte !== 0x20 && byte !== 0x09 && byte !== 0x0d && byte !== 0x0a && byte !== 0x0c,
  );
  const start = at < 0 ? [] : read.slice(at);
  if (OPENS_AS_MARKUP.some((opening) => opening.every((byte, index) => start[index] === byte))) return true;
  if (extension === 'txt' || extension === 'csv' || !KINDS.has(extension)) return false;
  const whole = String.fromCharCode(...read);
  return HOLDS_MARKUP.some((inside) => whole.includes(inside));
}
