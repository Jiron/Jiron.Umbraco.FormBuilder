# 0087. The upload endpoint

- Status: accepted; extends [ADR-0006](0006-file-storage-and-uploads.md) (how a request names its field, the parts
  until the challenge is verified, the checks of a file's first bytes, the token and the answers) and
  [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) (the declared body limit of an upload); a form's
  availability checked before the file, with the submission's codes, extended by
  [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md) in 6.1e; the same checks and token applied to a
  post without JavaScript by [ADR-0102](0102-files-in-a-post-without-javascript.md) in 6.2j; what a name and a file's bytes may
  hide, and the default blocked extensions, extended by [ADR-0143](0143-what-an-uploads-bytes-and-name-may-hide.md) in 7.3a;
  an upload's scan state and its cleanup by [ADR-0144](0144-cleanup-and-the-scanner.md) in 7.3b
- Date: 2026-10-28
- Roadmap step: 6.1d (decided and built); 6.1e checks the form's availability for uploads too, 6.2j applies the same
  checks to posts without JavaScript (ADR-0102), 6.4 sends uploads and shares the table of kinds, 7.1 verifies the challenge, 7.3
  hardens the checks and adds the scanner

## Context

ADR-0006 decided how a file reaches the site before the submission: one file per request, streamed, the challenge
part first, limits enforced while reading, the first bytes sniffed, an asynchronous store, and a signed token that
names the file's key. ADR-0071 gave each type that stores files its own limit and its own check of a file's name and
first bytes. Section 4.8 caps every field's limit with `Files:MaxFileBytes` and blocks `Files:BlockedExtensions` on
every field.

Open were: how a request names the field before its body is read; what the endpoint does with the challenge part
before 7.1 verifies challenges; which bytes match which extension; the token's contents and lifetime; what a refusal
answers; how the host's blocked extensions and a field's type groups meet; and what the frame declares for a body
whose limit depends on the field.

## Options

1. **Naming the field.** The field's limit decides whether a declared length is refused before a byte is read, so the
   field must be known before the body.
   - A part before the file: known only after reading the body. Rejected.
   - `forms/{key}/fields/{fieldKey}/uploads`: a route section 4.6 does not name.
   - **A `fieldKey` query value** on the route section 4.6 names. A field key is no secret, so the query is fine.
     Chosen.
2. **The challenge before 7.1.** No endpoint gives challenges until 7.1, and 6.4's runtime uploads before then.
   - Require the part now: every client would send a placeholder.
   - **Read the part when it is there, first and within its limit, and verify nothing yet;** 7.1 makes it required
     and verifies it before the file is read. Chosen.
3. **Bytes and extensions.**
   - Extension only: a renamed executable passes.
   - **A table of kinds by their first bytes, each with its extensions:** an extension with a kind takes only bytes of
     that kind; an extension without one (an editor's own, such as `dwg`) takes any bytes but an executable's.
     Chosen; 7.3 adds polyglots and double extensions.
4. **Blocked extensions and type groups.** A site may block an extension a group holds (`rar` of the archives).
   - Refuse the group at publish: the site could not use the group at all.
   - **A group holds only the extensions the site does not block,** so it never takes a blocked one; an extension
     the editor lists is refused at publish when the site blocks it. Chosen.
5. **The frame's body limit.** The frame refuses a declared length above an action's declaration before the action
   runs, and the document shows the declaration.
   - No declaration for uploads: the declarations test fails, and so it should.
   - **The largest upload the site takes:** `Files:MaxFileBytes` plus the multipart allowance, read from the host's
     configuration; the action holds each upload to its field's own limit. Chosen.

## Decision

- **The request:** `POST forms/{key}/uploads?fieldKey=<fieldKey>`, `multipart/form-data`, the operation `FormUpload`.
  Only the endpoint reads the body: the public API's form feature tells everything before it that the request has no
  form, so no value provider reads or buffers it.
  - `key` is a published form's key, of every form not in the trash, as the options endpoint serves (ADR-0086);
    anything else answers `404 form_not_found`. A missing or unreadable `fieldKey` answers `400 invalid_request`, a
    key that names no field of the form whose type stores files (a repeatable group's among them) `404 not_found`.
  - Another content type answers `415 unsupported_media_type`; a boundary that is missing or longer than 70
    characters, a part that is not `form-data`, a part other than an optional `challenge` (not a file, at most 4 KB)
    followed by one `file` (a file with a name), or anything after the file, `400 invalid_request`. A part has at
    most 8 headers and 4 KB of them.
- **The limits,** before a byte of the file is read: the field's limit is its type's or `Files:MaxFileBytes`,
  whichever is smaller (ADR-0071). A declared length above it plus 65,536 bytes answers `413 payload_too_large`;
  the server's own limit for the request is set to the same sum, and the body is counted against it as it is read, so
  a body without a declared length is cut off there too. The file itself is counted against the field's limit on
  every read. Past either, `413`. The frame declares the largest upload the site takes, `Files:MaxFileBytes` plus
  65,536 (`FormBuilderBodyLimitAttribute.LargestUpload`, read from the host's limits), refuses a declared length above
  it before the action runs, and sets the server's limit of every endpoint with a body to its declaration, since
  Kestrel's default would refuse a file the host allows. A server or proxy in front of the site with a lower limit of
  its own, such as IIS's `maxAllowedContentLength`, refuses larger files first; the setting's description says so.
- **The checks of a file,** on its first 512 bytes, or all of a shorter file, before any is stored:
  - its name as the device gave it, cut to its last path segment, without control characters, normalised to NFC and
    at most 255 characters with its extension kept; a part without one, or one that is empty once cleaned, answers
    `400 invalid_request`;
  - in order, each answering `415 unsupported_media_type` with the kind of error: bytes of an executable (Windows,
    ELF or Mach-O, or a script that starts with `#!`) and an extension the site blocks, `fileType`; the type's own
    check (the extensions a file field takes, a signature's PNG within its size), the kind it reports; and bytes that
    are not the kind the extension names, `fileType`. A `fileType` the type does not declare is `invalid`, as a file
    past its limit is `fileSize` or `invalid` (ADR-0071), so the field has a message for every refusal: a
    signature's refusals are all `invalid`.
  - **The kinds:** JPEG (`jpg`, `jpeg`), PNG, GIF, WebP, AVIF, HEIC (`heic`, `heif`), PDF, the office formats in a
    compound file (`doc`, `xls`, `ppt`), those in a ZIP (`docx`, `xlsx`, `pptx`, `odt`, `ods`, `odp`) and RTF, text
    (`txt`, `csv`: no NUL byte in the first bytes, unless they start with a UTF-16 byte-order mark), and the archives
    ZIP, 7z, RAR, gzip (`gz`) and tar (its `ustar` at byte 257). An extension outside the table takes any bytes but an
    executable's.
- **Storing:** the bytes go to the file store under a random id through a stream that counts them and computes their
  SHA-256, then a `jfbEntryFile` row without an entry records the cleaned name, the content type of the kind
  (`application/octet-stream` for an extension outside the table; never the client's), the size, the hash and the
  scan state `unscanned`, which 7.3's scanner replaces. The form's row is read before the bytes are stored, and the
  row of the file is added only once the request ends after the file. A file cut off or refused leaves nothing in the
  store; when a part follows the file (`400`), the row cannot be added (the form went meanwhile) or anything fails
  after the bytes are stored, they are deleted again, and bytes that cannot be deleted are logged without the name and
  left for retention (13.1). A store must read the upload to its end; one that returns early is an error. The file's
  name is never logged.
- **The token** names the form, the field, the file's key (never the stored id) and when it expires: 24 hours after
  the upload on the site's clock, as long as an upload without an entry is kept (ADR-0009). It is protected with Data
  Protection under a purpose of its own (`Jiron.FormBuilder.Upload.v1`), so a token of another purpose never reads as
  one; a load-balanced site shares its key ring (ADR-0006). It is the protected payload in base64url: a version byte,
  the three keys and the expiry's ticks. A token longer than 1,024 characters, the longest a file field's answer
  takes, reads as none, as does one for another form, an expired one and a changed one.
- **The answer:** `200 {token, name, size, contentType}`. Every refusal closes the connection on HTTP/1.x, since the
  rest of the body is never read. A `413` and a `415` of the file carry `errors: [{code, path: "file", message}]`
  with the field's kind of error (`fileSize`, `fileType`, `invalid`), so a client shows the field's own message for
  it; the frame's `413` for a declared length past the largest upload carries none, as no field is known yet. A
  `400` names where the request went wrong (`fieldKey`, `Content-Type`, `challenge` or `file`).
- **The host's limits:** `Files:MaxFileBytes` (52,428,800 by default; 1 to 999,999,999,999,999) and
  `Files:BlockedExtensions` (by default `exe`, `dll`, `js`, `bat`, `cmd`, `ps1`, `sh`, `msi`, `html` and `svg`; a
  site's own list replaces it; lowercase letters and digits, at most 10, each once). Start-up refuses any other value.
  Publishing refuses a file field whose `maxBytes` is above `Files:MaxFileBytes` at `settings.maxBytes`, and an
  extension it lists that the site blocks at `settings.extensions[i]`; a type group holds only the extensions the site
  does not block. A field type sees the host's limits in its settings check (`FieldSettingsCheck.Host`).
- **The declarations:** the `upload` rate-limit policy, the largest upload as the body limit, and 300 seconds, the
  longest time limit, so a large file on a slow connection finishes.

## Consequences

- A file reaches the store only once the form, the field, its size and its first bytes passed, and never past its
  limit; a renamed executable costs the server its first bytes.
- An upload to a closed form, or one whose entry limit is reached, is stored and waits for an entry that never comes
  until 6.1e checks the form's availability here as the submission does; the orphan cleanup of 7.3 removes such files.
- A members-only form takes uploads from anyone until 7.1's challenge is bound to the member (ADR-0004).
- 6.4's runtime refuses a file of the wrong size or kind before it sends it, with the table of kinds shared as test
  vectors, so only clients that skip it meet the server's refusal.
- **Revisit** when a kind visitors need is missing from the table; adding one is a change of the table and its vectors.
