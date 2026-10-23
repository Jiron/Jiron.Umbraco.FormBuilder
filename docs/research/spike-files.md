# Uploads and file storage: what the sandbox spike showed

Step 1.4b uploaded files to Umbraco 17.7.1 in the sandbox (SQLite, port 5510) both ways a form can send them: a
streamed upload to the public API before the submission, and a no-JS post of the whole form to its page. It stored
them in a folder outside the web root and, in a second run, in a blob container through Umbraco's official Azure Blob
provider, and served them back through a protected management API endpoint. The spike code is throwaway and
git-ignored (`.local/sandbox/Spike/Files/` and the no-JS additions to `Spike/Rendering/`); the decisions are in
[ADR-0006](../decisions/0006-file-storage-and-uploads.md). Measured on 2026-10-23 on the demo machine, with raw HTTP
clients (so the bytes on the wire are known) and in Microsoft Edge through Playwright. The site ran with
`ASPNETCORE_TEMP` pointing at a folder of its own, which the checks watched every 25 ms: any request body that
ASP.NET buffers to disk shows up there.

## 1. What was built

- **A form with a file field** ("application": a name and a CV of PDF, PNG or JPEG up to 50 MB) on a page of its own.
- **The streamed upload:** `POST /jfb/api/v1/forms/{key}/uploads`, one file per request, multipart with a
  `challenge` part and then a `file` part. The action reads the parts itself with ASP.NET's `MultipartReader`.
- **The checks on the bytes:** a signature table for PDF, PNG, JPEG, GIF and ZIP, and a blocklist for executables
  (Windows PE, ELF, Mach-O in both byte orders and fat binaries, and `#!` scripts). A field accepts an extension
  only when the bytes are that type.
- **Two stores behind one asynchronous interface:** a physical store under the content root
  (`umbraco/Data/JironFormBuilder/files`, sharded by the id's first two characters, no extension, written to
  `.partial` and renamed when complete), and a blob store on `Umbraco.StorageProviders.AzureBlob` 17.1.0, run
  against a local storage emulator (Azurite, a dev-only tool).
- **The no-JS path with files:** the form posts `multipart/form-data` to its page; the surface action applies the
  same checks to the files ASP.NET has buffered and stores them.
- **The protected download:** `GET /umbraco/jfb/management/api/v1/files/{fileKey}`, behind a policy that needs the
  backoffice token, a signed-in user and access to the spike's section.
- **For comparison,** a lab endpoint that takes the file the usual way, as an `IFormFile` through model binding.

## 2. Umbraco reads the form before the action

The first upload failed with `500` and "Unexpected end of Stream, the content may have already been read by another
component", and the temp folder held 97,892 bytes of a 100 KB file while the request ran. A lab middleware that
records the stack of the first read of the body named the reader:

`EagerMatcherPolicy.ApplyAsync` → `HttpRequestExtensions.GetUfprt` → `GetFormOrNull` → `FormFeature.ReadForm`

Umbraco's `EagerMatcherPolicy` applies to every endpoint. When more than one endpoint matches the path, and
Umbraco's own content route matches every path, it looks for the `ufprt` route token, first in the form. For any
request with a form content type that reads the whole body (64 KB in memory, the rest in a temp file) before the
endpoint is chosen, synchronously, and the stream is then used up for the action.

- **Reserving the path does not help.** With `~/jfb/` appended to `GlobalSettings.ReservedPaths` (the lab confirmed
  the setting took effect), the upload failed the same way: the policy checks no path.
- **What works:** a middleware that runs before routing (an Umbraco pipeline filter, `PrePipeline`) and gives every
  request under `/jfb/api/v1` an `IFormFeature` whose `HasFormContentType` is false and whose form is empty. Umbraco's
  check then finds no form and reads nothing, and the upload action reads the body as a stream. The rest of the
  public API takes JSON, so nothing there needs a form. With it, the temp folder stayed empty for every upload.

## 3. The streamed upload

The action works in this order, and each step happens before any later byte is read:

1. A `Content-Length` over the field's limit plus 64 KB of multipart overhead is refused with `413` at once.
2. Kestrel's body limit for the request is set to the same sum (`IHttpMaxRequestBodySizeFeature`), so a chunked body
   is cut off too.
3. The content type must be `multipart/form-data` with a boundary of at most 70 characters, otherwise `415`.
4. The reader allows at most 8 headers and 4 KB of headers per part.
5. The `challenge` part (at most 4 KB) comes first and is verified before the file part is opened. Without it the
   answer is `400 challenge_required`.
6. The first bytes of the `file` part are sniffed. An executable, an extension the field does not accept, or bytes
   that are not the type the extension claims are refused with `415 unsupported_media_type`.
7. The store reads the rest through a stream that counts every read and throws past the limit; the answer is
   `413 payload_too_large`, and the store removes what it had written.

Refusals send `Connection: close`. Model binding never runs on the body: a resource filter removes the form value
providers, as a second guard.

| Upload | Result |
| --- | --- |
| A 100 KB PDF | stored, `200` with a signed token, the size and the sniffed type; nothing in the temp folder |
| A 20 MB `.exe` renamed `setup.pdf` (chunked, and with a `Content-Length`) | `415` after 1.6 to 5.6 ms on the server, when the client had sent 4 MB (what the sockets held); nothing stored |
| A PDF named `photo.png`; a text file named `notes.txt` | `415` |
| 60 MB, chunked, to the 50 MB field | `413`; the server read 49.6 to 50.0 MB of the file; the client had sent 54 MB; 4.1 to 4.2 MB allocated in the whole process during the request; no temp file; no partial file left |
| 60 MB with `Content-Length` | refused before a byte was read |
| Exactly 50 MB | stored in 75 to 147 ms, with 2.7 to 3.5 MB allocated |
| The same files through model binding (`IFormFile`), for comparison | a 40 MB PDF filled the temp folder to 37.6 to 40 MB before the action ran; a renamed 20 MB `.exe` was refused only after all 20 MB had arrived and been buffered |

Kestrel refuses synchronous reads of a request body (`InvalidOperationException`), so a store that writes
synchronously, as Umbraco's `IFileSystem` does, cannot take the request stream directly.

## 4. What a client sees when the server refuses early

On HTTP/1.1 a server that answers before it has read the body closes the connection afterwards. A client that is
still sending may then see the connection reset instead of the answer: in these runs raw clients got the `413`
headers, the whole answer, or a reset, depending on timing. Two things made the answer arrive whole:

- **`Expect: 100-continue`.** A client that asks first never sends the body of a request with a declared length over
  the limit: it got the whole `413` and had sent 0 bytes of the file.
- **A rest within Kestrel's limit.** When the action answers without reading everything, Kestrel reads and discards
  the rest after the answer, as long as the rest fits within the request's body limit. The renamed `.exe` was
  refused on its first bytes, the remaining 20 MB fitted, and every client got the whole `415`.

Not reading past the limit does the opposite: the `413` for a 60 MB upload to a 50 MB field reached raw clients only
sometimes.

For the streamed upload ADR-0006 keeps the strict behaviour: the browser runtime checks size and type before it
sends, so only clients that skip it meet the refusal. For the no-JS post it chose the other way (section 5).

## 5. No-JS posts with files

A no-JS post goes to the page, and Umbraco reads its whole form while routing it (section 2), under the site's limits:
Umbraco sets Kestrel's body limit from `Umbraco:CMS:Runtime:MaxRequestLength` (in KB, 52,428,800 bytes by default)
and the multipart limit from the same setting (unlimited by default). ASP.NET buffers parts over 64 KB in the temp
folder and deletes them when the request ends (measured: a peak of about 100 KB for a 100 KB file, empty
afterwards). When the post is over the limit, Umbraco catches the error, finds no `ufprt`, and renders the page as if
it had been opened: the answers vanish and nothing says why.

The spike's answer: the form names itself in its action, `<page>?jfb-post=<key>.<instance>#<instance>` (the same for
every visitor, so the page stays cacheable), and a middleware before routing:

- recognises a post with that marker, looks up the form and sets its limit (its file fields' limits plus 1 MB);
- allows Kestrel twice that, so it can drain the rest of a post that is a little too large;
- reads the form itself, with the field's limit per part;
- when the post is too large, gives the request the no-form feature, hands the error to the view component, and lets
  Umbraco render the page.

| No-JS post | Result |
| --- | --- |
| A 100 KB PDF and a name | stored; `302` to `?jfb-sent=jfb-1#jfb-1`; the buffer removed |
| A renamed `.exe` | the page again, with the error at the field; nothing stored |
| 60 MB with `Content-Length` (browsers always send one) | the page with "The file is larger than 50 MB" before a byte was read; nothing stored, no entry |
| 60 MB chunked | the same after 50 MB were read; the buffer removed |
| 160 MB, beyond the drain allowance | refused before reading; the client saw a reset after 3 to 4 MB |
| 35 MB without the marker | the site's defaults applied: within Umbraco's 50 MB limit, stored |
| Edge with JavaScript off, a 60 MB file | the page with the error at the field; **without the drain allowance Edge showed `ERR_CONNECTION_RESET`** |
| Edge with JavaScript off, a 100 KB PDF | "Thank you" |

The answers of a post that was too large are lost, because the post was never read. The spike's page said so in a
notice, the error summary and at the field: three times. 6.2 designs that message once.

## 6. The stores

- **Physical:** `FileStream`s with `FileOptions.Asynchronous`, `FileMode.CreateNew` on the `.partial` file, and
  `File.Move` when complete. A cut-off upload left nothing behind. The root is refused at start-up when it lies
  inside the web root.
- **Blob, through Umbraco's provider.** `AddAzureBlobFileSystem("JironFormBuilder", options)` registers a named
  container (settings in `Umbraco:Storage:AzureBlob:JironFormBuilder`). `IAzureBlobFileSystemProvider.GetFileSystem`
  returns an `IAzureBlobFileSystem`, and its `GetBlobClient(path)` gives the SDK's `BlobClient`, which has
  asynchronous upload, read and delete. The provider's own `AddFile` is synchronous.
  - `UploadAsync` with `If-None-Match: *` and 1 MB blocks, one at a time: blocks are staged as they arrive and the
    blob exists only when the block list is committed, so the 60 MB upload cut off at the limit left no blob.
  - The provider prefixes every blob name with `ContainerRootPath`, which falls back to the virtual path when it is
    not set (`jfb-files/<xx>/<id>` instead of `<xx>/<id>`). The store sets it.
  - The container is created private (`PublicAccessType.None`). The plain registration adds no middleware; the
    blob's own URL, the container listing and the virtual path all refused anonymous requests.
  - All 41 checks passed on it. A 50 MB file took about 1 s against the emulator, with 6.7 to 8.0 MB allocated:
    the SDK's per-block requests cost more than the physical store's writes, but the memory does not grow with the
    file.

## 7. Downloads and authorization

- `AllowedApplicationRequirement`, Umbraco's own section requirement, is internal (`CS0122`), and the
  allowed-applications claim constant is obsolete in 17 (`CS0618`, removed in 18). The spike's requirement and
  handler read `IUser.AllowedSections` through `IAuthorizationHelper.TryGetUmbracoUser`, both public on 17.7.1 and
  18.2.1.
- The handler must be a **singleton**: registered as scoped, the site failed to start, because Umbraco's singleton
  server-event authorizers take `IAuthorizationService` and scope validation rejects the chain.
- The policy names the backoffice's token scheme (`OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme`).
  The backoffice sends a placeholder bearer value and the real token travels in an `HttpOnly` cookie, so the check
  signs in through the login screen and calls the endpoint from that browser context.

| Request | Result |
| --- | --- |
| Anonymous | `401` |
| A signed-in user of the Editors group (no access to the section) | `403` |
| The administrator | `200`, the same bytes, `Content-Disposition: attachment; filename=cv.pdf; filename*=UTF-8''cv.pdf`, `application/pdf` from the sniffed type, `nosniff`, `no-store` |
| An unknown file key | `404` |

None of 11 guessed addresses on the site (the store's path in several spellings, path traversal through
`/jfb/assets/`, `/media/`, `/App_Data/`, the bare id) returned the file; with the blob store, 15 including the blob's
own URL and the container listing.

## 8. The runs

| Run | Site | Result |
| --- | --- | --- |
| dev | physical store, the no-form middleware and the drain allowance on | 41 of 41 checks passed |
| blob | the blob store through the provider, against the local emulator | 41 of 41 checks passed |
| dev without the no-form middleware | `SPIKE_NO_GUARD=1` | the first upload failed: Umbraco's `GetFormOrNull` read the body first, 97,892 bytes in the temp folder; the run stopped there |
| dev without the drain allowance | `SPIKE_NOJS_DRAIN=0` | 40 of 41: Edge showed `ERR_CONNECTION_RESET` for the 60 MB no-JS post |
| 1.4a's rendering checks, after these changes | dev | 70 of 70 |

Screenshots are in `.local/sandbox/checks/shots/nojs-upload-*.png` (not committed).

## 9. Notes for later steps

- **4.3:** the physical store as above, with the root setting of §4.8, and a test that a failed save leaves nothing.
- **6.1:** the no-form middleware covers the configurable public API base, not a fixed path. Every token type gets its
  own Data Protection purpose (challenge, upload token). The file name from the client is untrusted: keep only the
  last segment, cap its length, and never log it (§4.10).
- **6.2:** the marker, the limits middleware and the file rules in the surface action. Keep a file that passed its
  checks across a re-render for other errors, through a signed token in the form.
- **6.4:** the runtime builds the multipart body with the challenge first, checks size and type with the shared
  signature vectors before sending, shows progress with `XMLHttpRequest` (fetch has no upload progress), and treats a
  reset as a failed upload with a retry.
- **7.3:** check over HTTPS with HTTP/2 that a refused upload's answer reaches the browser; Kestrel also gives up on a
  slow drain after a time limit, which the spike did not measure.
- **Docs:** no-JS uploads are buffered in the ASP.NET temp folder up to the form's limit; server-to-server clients
  should send `Expect: 100-continue`; load-balanced sites need a shared store and a shared key ring.
- **15.2:** the blob sample with an explicit `ContainerRootPath`, a private container, and block size and concurrency
  as options (1 MB, one at a time, was the spike's choice for bounded memory, not for speed).

## 10. What changed in the roadmap

- §2 D15: the asynchronous `IFormBuilderFileStore` replaces "a Jiron Form Builder `IFileSystem`" (ADR-0006).
- §3.4: the files note is resolved, and the facts about Umbraco reading forms while routing, its request limits, and
  section authorization are added.
- §4.6: the upload request is described. §4.8: `MaxFileBytes` caps every field's limit. §4.9: the store's contract.
  §4.10: the no-form rule, the no-JS limits and the refusal behaviour.
- 4.3, 6.1, 6.2, 6.4, 7.3 and 15.2 gain the work and the checks of ADR-0006's consequences.
