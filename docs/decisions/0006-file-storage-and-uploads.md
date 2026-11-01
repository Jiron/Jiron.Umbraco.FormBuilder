# 0006. File storage and uploads

- Status: accepted; narrows §1.1 (amended in 1.8); the field's limit and its type's check of a file's first bytes
  amended by [ADR-0071](0071-the-advanced-types-own-checks.md) in 5.4c; one token standing for one value of a
  submission amended by [ADR-0080](0080-reading-a-submission-after-the-phase-5-review.md) in 5.5f; the upload endpoint
  (the field in the query, the parts before the challenge is verified, the kinds of file, the token and the answers)
  extended by [ADR-0087](0087-the-upload-endpoint.md) in 6.1d; the no-JS post with files (read part by part before
  routing, within the form's limit) built by [ADR-0102](0102-files-in-a-post-without-javascript.md) in 6.2j; the token
  bound to the challenge it came with, and a form for members taking files only from a member it admits, in 7.1b
  ([ADR-0138](0138-the-challenge.md)); polyglots and double extensions refused in 7.3a
  ([ADR-0143](0143-what-an-uploads-bytes-and-name-may-hide.md)); the scanner and the orphan cleanup built in 7.3b
  ([ADR-0144](0144-cleanup-and-the-scanner.md)); the downloads and the archive built in 7.3c
  ([ADR-0145](0145-downloads-and-archives.md))
- Date: 2026-10-23
- Roadmap step: 1.4b (decided); built in 4.3 (the physical store), 6.1 (the upload endpoint), 6.2j (no-JS posts with
  files), 6.4 (the runtime's uploads), 7.3 (hardening, downloads), 15.2 (the blob sample); reviewed in 7.4 and 16.x

## Context

D15 asks for files stored outside the public web root "through a Jiron Form Builder `IFileSystem`, which also works
with Umbraco's official storage providers", random names, downloads only through authorised endpoints, an extension
allowlist with content sniffing, size and count limits, a scanner hook and deletion with the entry. §4.6 uploads one
file per request before the submission and returns a signed token. D9 requires forms to work without JavaScript,
file fields included, and D11 sends those posts through Umbraco's surface routing.

Step 1.4b built a streamed upload endpoint, two stores and a protected download in the sandbox on Umbraco 17.7.1,
and tested them with raw HTTP clients and in Edge, with the site's ASP.NET temp folder watched for buffering
([research](../research/spike-files.md)). What it found that shapes the decision:

- **Kestrel refuses synchronous reads of a request body,** and Umbraco's `IFileSystem` writes synchronously
  (`AddFile`); the official Azure Blob provider's `AddFile` calls the SDK's synchronous upload. Storing a request
  stream through `IFileSystem` would need sync-over-async or a buffered copy of the whole file.
- **Umbraco reads the form of every form post while it picks an endpoint.** Its `EagerMatcherPolicy` applies to every
  endpoint; when more than one endpoint matches a path (Umbraco's content route always does), it looks for `ufprt` in
  `Request.Form`. A multipart upload to a controller at `/jfb/api/v1` was therefore read in full before the action
  (64 KB in memory, the rest in a temp file) and the action found the stream used up. Adding `~/jfb/` to
  `ReservedPaths` changes nothing: the policy checks no path.
- **Umbraco sets the request limits site-wide** from `Umbraco:CMS:Runtime:MaxRequestLength` (in KB): Kestrel's body
  limit defaults to 52,428,800 bytes, and the multipart limit to unlimited.
- **What a client sees when the server refuses early.** On HTTP/1.1 a server that answers before it has read the
  body closes the connection, and a client that is still sending may see a reset instead of the answer. Raw clients
  got the `413` in some runs and a reset in others. A client that sends `Expect: 100-continue` got the whole `413`
  without sending the file. When the rest of the body fits within Kestrel's limit, Kestrel reads and discards it
  after the answer, and the client gets the answer whole. In Edge with JavaScript off, a 60 MB file sent to a 50 MB
  field showed `ERR_CONNECTION_RESET` when the server stopped at the limit, and the page with the error when Kestrel
  could drain the rest.
- **Umbraco's Azure Blob provider** (`Umbraco.StorageProviders.AzureBlob` 17.1.0, MIT, Umbraco HQ) registers named
  containers (`AddAzureBlobFileSystem(name, configure)`, settings in `Umbraco:Storage:AzureBlob:{name}`) and hands out
  `IAzureBlobFileSystem` instances whose `GetBlobClient(path)` gives the SDK's asynchronous client. Without
  `ContainerRootPath`, every blob name gets the virtual path as a prefix. The plain registration serves nothing; only
  the media variant adds middleware.
- **Authorization:** Umbraco's section requirement is internal and the allowed-applications claim is obsolete in
  17 (`CS0618`), but `IUser.AllowedSections` is public. A handler that reads it must be a singleton: Umbraco's
  singleton event authorizers consume `IAuthorizationService`, so a scoped handler fails start-up scope validation.

## Options

**The storage abstraction:**

1. **Umbraco's `IFileSystem`,** as D15 words it. It plugs into the official providers directly, but its writes are
   synchronous: a request stream would have to be buffered whole or read with sync-over-async.
2. **Jiron Form Builder's own asynchronous `IFormBuilderFileStore`,** with a physical default and a blob store
   that uses the official provider's container and the SDK's asynchronous client.
3. **A direct Azure SDK dependency in the core.** Every site would carry it, and it would bypass the provider's
   configuration. Rejected.

**Getting an upload past Umbraco's routing:**

1. **Reserve `/jfb/`.** Measured: no effect.
2. **A no-form feature before routing:** for every request under the public API base, an `IFormFeature` that reports
   no form content, so nothing reads the body before its action. The public API takes JSON, and streams uploads.
3. **The file as the raw body** with its name in a header. No form content type to read, but a client that sends one
   anyway is still buffered before the action refuses it, and §4.6 and 1.4b planned multipart.

**Refusing a body that is too large:**

1. **Strict:** stop reading at the limit, answer `413`, close. The server never reads past the limit; a client still
   sending may see a reset.
2. **A drain allowance:** let Kestrel read and discard a bounded rest after the answer, so a browser can read it.

**No-JS posts with files:**

1. **Umbraco's defaults.** The post is read under the site's limit; above it Umbraco swallows the error, finds no
   `ufprt` and renders the page as if it had been opened: the answers vanish and nothing says why.
2. **Limits set before routing:** the form names itself in its action, and a middleware sets the limits from the
   form's file fields, reads the form itself, and turns a post that is too large into an error at the field.
3. **No file fields without JavaScript.** Breaks D9.

## Decision

Option 2 for storage, option 2 for routing, strict refusals on the API and a drain allowance for no-JS posts, and
option 2 for no-JS posts.

- **The store.** `IFormBuilderFileStore` is asynchronous and small: `SaveAsync(id, stream)` stores all of it or
  nothing, `OpenReadAsync(id)` returns a stream or null, `DeleteAsync(id)`. Ids are 128 random bits as 32 lowercase
  hex characters, never derived from the file name; `jfbEntryFile.StoredName` holds the id. The public upload token is
  a separate time-limited, form-bound Data Protection token that names the file's key, never the stored id.
  - **The physical default** stores under `JironFormBuilder:Files:Root` (`~/umbraco/Data/JironFormBuilder/Uploads`),
    sharded by the id's first two characters, with no extension. It writes to `<id>.partial` and renames only when
    complete, and deletes the partial file on any failure. It refuses to start when the root lies inside the web root.
  - **Blob storage through the official provider** (a sample, 15.2): the site registers a container with
    `AddAzureBlobFileSystem("JironFormBuilder", …)`; the store calls `GetFileSystem("JironFormBuilder")
    .GetBlobClient("<xx>/<id>")` and uses `UploadAsync` (with `If-None-Match: *`), `OpenReadAsync` and
    `DeleteIfExistsAsync`. Blocks are staged as they arrive and committed only at the end, so a cut-off upload leaves
    no blob. The container is private, `ContainerRootPath` is set explicitly, and no middleware ever serves it.
  - This replaces D15's "a Jiron Form Builder `IFileSystem`": the store is Jiron Form Builder's own and asynchronous,
    and it works with Umbraco's official storage provider through the provider's own container configuration.
- **The streamed upload** (`POST /jfb/api/v1/forms/{key}/uploads`, one file per request):
  - An Umbraco pipeline filter registers, before routing, a middleware that gives every request under the public API
    base the no-form feature. The action also removes the form value providers.
  - In order: a declared `Content-Length` over the field's limit (its type's or the host's, whichever is smaller,
    ADR-0071) plus 64 KB is refused with `413` before a byte is read; Kestrel's limit for the request is set to the
    same sum; the body must be `multipart/form-data` with a
    boundary of at most 70 characters; the reader allows at most 8 headers and 4 KB of headers per part; the
    `challenge` part (at most 4 KB) must come first and is verified before the file's first byte is read; the first
    bytes of the `file` part are sniffed, and the file is refused with `415` unless its extension is allowed for the
    field and the bytes match that type, and always when they are an executable (PE, ELF, Mach-O or a `#!` script);
    the field's type checks the name and those bytes itself (ADR-0071: a signature is a PNG within its size in
    pixels); the store then reads through a stream that enforces the limit on every read and answers `413` past it.
  - Refusals close the connection and are strict: the server never reads past the limit. The runtime checks size and
    type in the browser first, with the same signature table, so the server's refusal is for clients that skip it.
- **No-JS posts with files:**
  - The form's action names the form: `<page>?jfb-post=<key>.<instance>#<instance>`. It is the same for every visitor,
    so cached markup stays cacheable.
  - The same pipeline filter adds a middleware for posts with a form content type and that marker. It looks up the
    published form, sets the form's limit (the sum of its file fields' limits plus 1 MB for the other fields), allows
    Kestrel twice that so it can drain the rest of a post that is too large, and reads the form itself with the
    largest field limit per part. ASP.NET buffers parts over 64 KB in the temp folder and deletes them when the
    request ends.
  - A post over the limit gets the no-form feature, its error is handed to the view component for that instance, and
    Umbraco renders the page. The answers cannot be kept, because the post was not read, and the message says so. A
    post above the drain allowance is cut off.
  - The surface action applies the upload's rules (size, allowlist, magic number, blocklist) to the buffered files
    before storing them through the same store.
- **Downloads** go only through the management API: the backoffice token scheme, an authenticated user, the Forms
  section through `IUser.AllowedSections` (a singleton handler), and the form permissions of ADR-0013. Responses are
  streamed with `Content-Disposition: attachment` and the original name, `X-Content-Type-Options: nosniff`,
  `Cache-Control: no-store`, and the content type of the sniffed type, never the client's.

**Measured** (one site on this machine): a 50 MB file was stored in 75 to 147 ms with 2.7 to 3.5 MB allocated and
nothing in the temp folder; on the blob store against the local emulator, with 1 MB blocks one at a time, in about
1 s with 6.7 to 8.0 MB allocated. A 60 MB upload to a 50 MB field was cut off after 49.6 to 50.0 MB were read.

## Consequences

- §2 D15 names the asynchronous store. §3.4's files note is resolved, and §3.4 gains the routing, limits and
  authorization facts. §4.6 describes the upload request, §4.8 caps field limits with `MaxFileBytes`, §4.9 describes
  the store's contract, and §4.10 gains the no-form rule, the no-JS limits and the refusal behaviour. WORKLOG records
  the change.
- 4.3 builds the physical store and tests that a failed save leaves nothing. 6.1 builds the endpoint, the no-form
  middleware and the strict limits, with the 1.4b integration test ported: a 60 MB upload to a 50 MB field cut off
  with nothing buffered, a renamed `.exe` refused on its first bytes, a declared length refused before reading.
- 6.2 builds the marker, the limits middleware and the file rules in the surface action. Its message for a post that
  is too large is designed once (the spike showed it three times: a notice, the summary and the field). A file that
  passed its checks is kept across a re-render for other errors, through a signed token in the form, so the visitor
  does not choose it again.
- 6.4's runtime refuses an oversize or mistyped file before uploading, with signature test vectors shared with the
  server (D10), and treats a reset during an upload as a failed upload the visitor can retry.
- 7.3 hardens the rules and checks over HTTPS with HTTP/2, where the server can end a request stream without closing
  the connection, that a refused upload's answer reaches the browser.
- 15.2's blob sample follows this ADR and passes the store's contract tests against a local storage emulator. Only
  the samples project references the provider (an Umbraco HQ package, MIT) and, through it, the Azure SDK; the
  product's packages never do, so no site gets them unless it adds the sample's store itself.
- This narrows §1.1, which promised file storage "through Umbraco's file system providers": they write
  synchronously, so the official blob provider is reached through the sample store, and §1.1 now says so.
- Load-balanced sites need a shared store (the blob sample or a shared folder) as well as a shared Data Protection key
  ring, because the upload token and the file must be found by whichever server takes the submission.
- **Revisit** if Umbraco stops reading forms in its matcher policy or adds a supported opt-out, if `IFileSystem`
  gains asynchronous writes, or if Kestrel gains a way to answer early without dropping the connection on HTTP/1.1.
