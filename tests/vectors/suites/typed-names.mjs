// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// A name typed instead of a signature (ROADMAP 6.4n, ADR-0103, ADR-0153): the page cleans it as the server does, and the PNG
// it draws holds the name as the server's does, so the server reads it back the same. `typedName` holds a name as typed and
// expects it cleaned; `typedPng` holds a PNG in hexadecimal and a name, and expects the PNG with the name in an iTXt chunk
// after its header, with what a reader finds in it. The chunks are written out here, never by the code under test.
import { crc32 } from 'node:zlib';

const ZERO_WIDTH_JOINER = String.fromCodePoint(0x200d);
const ZERO_WIDTH_NON_JOINER = String.fromCodePoint(0x200c);
const LEFT_TO_RIGHT_MARK = String.fromCodePoint(0x200e);
const NO_BREAK_SPACE = String.fromCodePoint(0xa0);
const IDEOGRAPHIC_SPACE = String.fromCodePoint(0x3000);
const E_ACUTE = String.fromCodePoint(0xe9);
const COMBINING_ACUTE = String.fromCodePoint(0x301);
const U_DIAERESIS = String.fromCodePoint(0xfc);
const JAPANESE = String.fromCodePoint(0x5c71, 0x7530);
const GRINNING = String.fromCodePoint(0x1f600);

const big = (value) => Buffer.from([value >>> 24, (value >>> 16) & 0xff, (value >>> 8) & 0xff, value & 0xff]);

const chunk = (type, body) => {
  const typed = Buffer.concat([Buffer.from(type, 'ascii'), body]);
  return Buffer.concat([big(body.length), typed, big(crc32(typed))]);
};

const SIGNATURE = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);
const header = (width, height) =>
  chunk('IHDR', Buffer.concat([big(width), big(height), Buffer.from([8, 6, 0, 0, 0])]));
const END = chunk('IEND', Buffer.alloc(0));

const png = (width, height, ...between) => Buffer.concat([SIGNATURE, header(width, height), ...between, END]);

const named = (name) =>
  chunk('iTXt', Buffer.concat([Buffer.from('jfb:typed-name', 'ascii'), Buffer.alloc(5), Buffer.from(name, 'utf8')]));

const typedName = (id, typed, name) => ({ id, typedName: { typed }, expect: { name } });

const typedPng = (id, width, height, name) => ({
  id,
  typedPng: { image: png(width, height).toString('hex'), name },
  expect: { png: png(width, height, named(name)).toString('hex'), name, width, height },
});

export default {
  suite: 'typed-names',
  defaults: {},
  fields: {},
  cases: [
    typedName('a-name-as-it-is', 'Ana Lima', 'Ana Lima'),
    typedName('its-ends-trimmed', '  Ana Lima  ', 'Ana Lima'),
    typedName('a-run-of-spaces-one-space', 'Ana \t\n  Lima', 'Ana Lima'),
    typedName('a-no-break-space-a-space', `Ana${NO_BREAK_SPACE}Lima`, 'Ana Lima'),
    typedName('an-ideographic-space-a-space', `Ana${IDEOGRAPHIC_SPACE}Lima`, 'Ana Lima'),
    typedName('composed', `Jose${COMBINING_ACUTE}`, `Jos${E_ACUTE}`),
    typedName('without-control-characters', `Ana${String.fromCodePoint(7)} Lima${String.fromCodePoint(0x7f)}`, 'Ana Lima'),
    typedName('without-a-direction-mark', `${LEFT_TO_RIGHT_MARK}Ana Lima`, 'Ana Lima'),
    typedName('a-space-after-a-mark-at-the-start-is-gone', `${LEFT_TO_RIGHT_MARK} Ana`, 'Ana'),
    typedName('its-joiners-kept', `a${ZERO_WIDTH_JOINER}b${ZERO_WIDTH_NON_JOINER}c`, `a${ZERO_WIDTH_JOINER}b${ZERO_WIDTH_NON_JOINER}c`),
    typedName('another-script-kept', JAPANESE, JAPANESE),
    typedName('beyond-the-first-plane-kept', `Ana ${GRINNING}`, `Ana ${GRINNING}`),
    typedName('nothing-is-no-name', '', null),
    typedName('only-spaces-is-no-name', ' \t ', null),
    typedName('only-marks-is-no-name', `${LEFT_TO_RIGHT_MARK}${String.fromCodePoint(7)}`, null),
    typedPng('a-name-on-the-default-pad', 900, 300, 'Ana Lima'),
    typedPng('a-name-with-a-letter-of-two-bytes', 900, 300, `Ana M${U_DIAERESIS}ller`),
    typedPng('a-name-in-another-script', 900, 300, JAPANESE),
    typedPng('a-name-beyond-the-first-plane', 900, 300, `Ana ${GRINNING}`),
    typedPng('a-long-name-on-a-widened-image', 2700, 300, 'Ana Lima'.repeat(12)),
    typedPng('a-pad-of-its-own-size', 640, 200, 'Ana'),
  ],
};
