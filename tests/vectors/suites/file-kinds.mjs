// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The kinds of file a file's first bytes show (ROADMAP 6.4m, ADR-0143, ADR-0152): what the browser checks before it
// sends a file, and the server again as it reads one. A case holds an extension in lowercase without its point (empty
// for a name without one) and the file's first bytes in hexadecimal, and expects whether the bytes are a program or a
// script (`executable`), whether they are the kind the extension names (`matches`) and whether they hide a page behind
// another name (`hidesMarkup`). Every expectation is written out here, never computed by the code under test.

/** Bytes as hexadecimal: numbers as they are, text as its ASCII codes. */
const hex = (...parts) =>
  parts
    .flatMap((part) => (typeof part === 'string' ? [...part].map((letter) => letter.charCodeAt(0)) : [part]))
    .map((byte) => byte.toString(16).padStart(2, '0'))
    .join('');

const fine = { executable: false, matches: true, hidesMarkup: false };
const wrong = { executable: false, matches: false, hidesMarkup: false };
const program = { executable: true, matches: false, hidesMarkup: false };

const JPEG = hex(0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 'JFIF', 0x00);
const PNG = hex(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d);
const PDF = hex('%PDF-1.7', 0x0a);
const COMPOUND = hex(0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1, 0x00, 0x00);
const ZIP = hex(0x50, 0x4b, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00);
const WINDOWS = hex('MZ', 0x90, 0x00, 0x03, 0x00);
const TEXT = hex('Name;Amount', 0x0d, 0x0a);
/** An ISO media file: its box's size, `ftyp`, the major brand, a version, and the compatible brands. */
const media = (major, ...brands) => hex(0, 0, 0, 16 + brands.length * 4, 'ftyp', major, 0, 0, 0, 0, ...brands);

const one = (id, extension, head, expect) => ({ id, fileKind: { extension, head }, expect });

export default {
  suite: 'file-kinds',
  defaults: {},
  fields: {},
  cases: [
    one('jpg-a-jpeg', 'jpg', JPEG, fine),
    one('jpeg-a-jpeg', 'jpeg', JPEG, fine),
    one('jpg-a-png', 'jpg', PNG, wrong),
    one('png-a-png', 'png', PNG, fine),
    one('png-a-pdf', 'png', PDF, wrong),
    one('gif-87', 'gif', hex('GIF87a', 1, 0), fine),
    one('gif-89', 'gif', hex('GIF89a', 1, 0), fine),
    one('gif-a-text', 'gif', hex('GIF90a', 1, 0), wrong),
    one('webp-a-webp', 'webp', hex('RIFF', 0x24, 0, 0, 0, 'WEBPVP8 '), fine),
    one('webp-a-wave', 'webp', hex('RIFF', 0x24, 0, 0, 0, 'WAVEfmt '), wrong),
    one('webp-too-short', 'webp', hex('RIFF', 0x24), wrong),
    one('avif-major-brand', 'avif', media('avif'), fine),
    one('avif-compatible-brand', 'avif', media('mif1', 'avif'), fine),
    one('avif-a-heic', 'avif', media('heic'), wrong),
    one('heic-major-brand', 'heic', media('heic'), fine),
    one('heif-mif1', 'heif', media('mif1'), fine),
    one('heic-a-video', 'heic', media('isom', 'mp42'), wrong),
    one('heic-too-short', 'heic', hex(0, 0, 0, 16, 'ftyp'), wrong),
    one('pdf-a-pdf', 'pdf', PDF, fine),
    one('pdf-a-text', 'pdf', TEXT, wrong),
    one('doc-a-compound-file', 'doc', COMPOUND, fine),
    one('xls-a-compound-file', 'xls', COMPOUND, fine),
    one('ppt-a-compound-file', 'ppt', COMPOUND, fine),
    one('doc-a-zip', 'doc', ZIP, wrong),
    one('docx-a-zip', 'docx', ZIP, fine),
    one('xlsx-a-zip', 'xlsx', ZIP, fine),
    one('pptx-a-zip', 'pptx', ZIP, fine),
    one('odt-a-zip', 'odt', ZIP, fine),
    one('ods-a-zip', 'ods', ZIP, fine),
    one('odp-a-zip', 'odp', ZIP, fine),
    one('docx-an-empty-zip', 'docx', hex(0x50, 0x4b, 0x05, 0x06, 0, 0, 0, 0), wrong),
    one('rtf-a-rtf', 'rtf', hex('{\\rtf1\\ansi'), fine),
    one('txt-a-text', 'txt', TEXT, fine),
    one('csv-a-text', 'csv', TEXT, fine),
    one('txt-empty', 'txt', '', fine),
    one('txt-utf16', 'txt', hex(0xff, 0xfe, 'a', 0, 'b', 0), fine),
    one('txt-utf16-big-endian', 'txt', hex(0xfe, 0xff, 0, 'a', 0, 'b'), fine),
    one('txt-with-a-nul', 'txt', hex('a', 0, 'b'), wrong),
    one('zip-with-an-entry', 'zip', ZIP, fine),
    one('zip-empty', 'zip', hex(0x50, 0x4b, 0x05, 0x06, 0, 0, 0, 0), fine),
    one('zip-spanned', 'zip', hex(0x50, 0x4b, 0x07, 0x08, 0x50, 0x4b, 0x03, 0x04), fine),
    one('zip-a-pdf', 'zip', PDF, wrong),
    one('7z-a-7z', '7z', hex(0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c, 0, 4), fine),
    one('rar-a-rar', 'rar', hex('Rar!', 0x1a, 0x07, 0x00), fine),
    one('rar-5', 'rar', hex('Rar!', 0x1a, 0x07, 0x01, 0x00), fine),
    one('rar-a-zip', 'rar', ZIP, wrong),
    one('gz-a-gz', 'gz', hex(0x1f, 0x8b, 0x08), fine),
    one('tar-too-short', 'tar', hex('ustar'), wrong),
    one('an-extension-of-no-kind', 'dat', PDF, fine),
    one('no-extension', '', PDF, fine),
    one('an-empty-pdf', 'pdf', '', wrong),
    one('a-windows-program-as-pdf', 'pdf', WINDOWS, program),
    one('a-windows-program-as-txt', 'txt', WINDOWS, program),
    one('an-elf-binary', 'pdf', hex(0x7f, 'ELF', 2, 1), program),
    one('a-mach-o-32', 'pdf', hex(0xfe, 0xed, 0xfa, 0xce), program),
    one('a-mach-o-64', 'pdf', hex(0xfe, 0xed, 0xfa, 0xcf), program),
    one('a-mach-o-32-swapped', 'pdf', hex(0xce, 0xfa, 0xed, 0xfe), program),
    one('a-mach-o-64-swapped', 'pdf', hex(0xcf, 0xfa, 0xed, 0xfe), program),
    one('a-universal-binary', 'pdf', hex(0xca, 0xfe, 0xba, 0xbe), program),
    one('a-script', 'txt', hex('#!/bin/sh', 0x0a), { executable: true, matches: true, hidesMarkup: false }),
    one('a-page-as-txt', 'txt', hex('<!DOCTYPE html><p>'), { executable: false, matches: true, hidesMarkup: true }),
    one('a-page-as-png', 'png', hex('<html>'), { executable: false, matches: false, hidesMarkup: true }),
    one('a-page-after-spaces', 'txt', hex('  ', 0x0a, '<script>'), { executable: false, matches: true, hidesMarkup: true }),
    one('a-page-as-utf16', 'txt', hex(0xff, 0xfe, '<', 0, 's', 0, 'v', 0, 'g', 0), {
      executable: false,
      matches: true,
      hidesMarkup: true,
    }),
    one('a-page-with-a-byte-order-mark', 'txt', hex(0xef, 0xbb, 0xbf, '<?xml '), {
      executable: false,
      matches: true,
      hidesMarkup: true,
    }),
    one('a-server-script', 'txt', hex('<?php echo 1;'), { executable: false, matches: true, hidesMarkup: true }),
    one('a-server-page', 'csv', hex('<% x %>'), { executable: false, matches: true, hidesMarkup: true }),
    one('text-that-mentions-a-page', 'txt', hex('See <html> here'), fine),
    one('a-page-inside-a-jpeg', 'jpg', hex(0xff, 0xd8, 0xff, 0xe0, 0, '<script>'), {
      executable: false,
      matches: true,
      hidesMarkup: true,
    }),
    one('an-svg-inside-a-pdf', 'pdf', hex('%PDF-1.7 <SVG>'), { executable: false, matches: true, hidesMarkup: true }),
    one('a-page-named-as-one', 'html', hex('<html>'), fine),
    one('an-svg-named-as-one', 'svg', hex('<svg>'), fine),
    one('a-page-of-no-kind', 'dat', hex('<html>'), { executable: false, matches: true, hidesMarkup: true }),
    one('a-mention-of-no-kind', 'dat', hex('See <html> here'), fine),
  ],
};
