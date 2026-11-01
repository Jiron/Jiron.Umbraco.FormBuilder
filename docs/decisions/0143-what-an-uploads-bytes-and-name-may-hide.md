# 0143. What an upload's bytes and name may hide

- Status: accepted; extends [ADR-0006](0006-file-storage-and-uploads.md) and
  [ADR-0087](0087-the-upload-endpoint.md) (the checks of a file, the default blocked extensions)
- Date: 2026-11-01
- Roadmap step: 7.3a

## Context

ADR-0087 holds a file's extension to its first bytes and left polyglots and double extensions to 7.3. A file can be two things
at once, and a name can say two things at once. Downloads are attachments with `nosniff`, so a browser opening a file from this
site is not the main risk. The risk is what happens next: an editor downloads the file and opens it, a store serves it, or a
copy lands on a web server that reads a name's middle.

## Decision

- **A blocked extension counts anywhere after a name's first part.** `report.exe.pdf`, `page.php.jpg` and `.exe.pdf` are
  refused as `fileType`, in any letters, with spaces around a part, and up to a colon, so `report.exe:.pdf` (an NTFS stream)
  does not hide it. The first part is a stem, so `exe.pdf` is a name. Some honest names are refused, such as
  `Intro.to.Node.js.pdf`; the visitor renames it, and the message says the type is not taken.
- **A name loses the marks that reorder how it reads** (U+061C, U+200E, U+200F, U+202A to U+202E, U+2066 to U+2069), so
  `photo` + U+202E + `gpj.exe` cannot show as a picture's name in a list of files.
- **Bytes that hide a page are `fileType`,** checked on the same first bytes, in lowercase, past a byte-order mark and without
  zero bytes, which UTF-16 holds with or without its mark and which some readers skip inside a tag:
  - a file that opens as a page, a drawing, XML or a server's script (`<html`, `<head`, `<body`, `<script`, `<svg`, `<iframe`,
    `<object`, `<embed`, `<?xml`, `<?php`, `<%`), whatever its extension, unless the extension names markup (`html`, `htm`,
    `xhtml`, `shtml`, `xml`, `svg`), which the site has then chosen not to block;
  - a binary kind of the table with one of the longer openings inside those bytes (`<!doctype html`, `<html`, `<script`,
    `<svg`, `<iframe`, `<?php`). `<%` is left out here: two bytes appear in an image by chance. A text file (`txt`, `csv`) may
    mention markup after its start, and an image's own XMP metadata does not match.
- **A file that ends as a ZIP is refused** when its kind is in the table and is not one. A ZIP reader looks for the closing
  record from the end, so a picture with an archive behind it is both (a GIF with a JAR behind it being the old example). The
  upload keeps its last 65,557 bytes in a pooled ring, the closing record with its longest comment, and only for those kinds; a
  record counts only when its comment reaches exactly to the last byte, so a stray signature in the bytes does not. The check
  runs once the bytes are stored, and a refusal deletes them.
- **The default blocked extensions grow** to `mjs`, `msp`, `scr`, `pif`, `cpl`, `jar`, `vbs`, `wsf`, `hta`, `lnk`, `reg`, `htm`,
  `xhtml`, `shtml`, `php`, `phtml`, `asp`, `aspx`, `jsp` and `cgi`. `com` stays out, because a blocked extension counts anywhere
  in a name and `example.com.pdf` is a common one; a file field takes only extensions its editor chose or a group's, and no
  group holds it.
- **The whole file is not searched.** Past the first bytes, the checks stop at the closing record: a scanner reads whole files
  (ADR-0144). Other formats inside a picture's later bytes are not a browser's or a ZIP reader's to find.

## Consequences

- The runtime refuses by the table of kinds and leaves these checks to the server, whose answer it already shows at the field.
- A site that wants `svg` or `html` files removes them from its own list, and they are taken as what they are. They are still
  only ever attachments.
- The test of a 60 MiB upload to a 50 MiB field measured the process's allocations against a quarter of what it read. Measured
  with and without this change it swings between 12.5 and 13.7 MB, on both sides of that limit, because the whole test host
  counts. It now holds them to half: buffering in memory would still allocate at least what it read, which is what it proves.

## Alternatives considered

- **Searching every byte for markup:** costs a scan per upload and finds what no browser reads there. Rejected.
- **Refusing every double extension:** `report.final.pdf` is honest. Only blocked extensions count.
- **Scanning for a ZIP's start instead of its end:** readers look from the end, and a start can be left out.
