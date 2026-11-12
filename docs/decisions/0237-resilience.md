# 0237. Resilience: what goes on when a part fails

- Status: accepted
- Date: 2026-11-12
- Roadmap step: 16.1 (part 16.4, resilience)

## Context

Part 16.4 asks that a site keeps working, and loses nothing, when one of the parts around it fails: the mail server, a
webhook's receiver, the database for a moment, a server that restarts in the middle of an action, a server whose cache
has not heard of a change yet, two servers that pick up the same work, the network during a send, a browser that blocks
storage, and one broken form on a page. Most of these were designed for when their features came (ADR-0007, ADR-0043,
ADR-0120, ADR-0158 and Tandem's ADR-0065, whose transient retry Jiron Form Builder ported), and each has its tests. A
mapping of each case to the code and its tests found four that did not hold:

- **A mail server that is down used up the mail's attempts.** A refused connection counted as a failed attempt, so five
  attempts' back-off (1, 3, 9 and 27 minutes) failed every mail of an outage of about forty minutes, and a person had to
  send each again. ADR-0007 spent no attempt while no mail server was set up, but one that was set up and not answering
  was not told apart from one that refused a mail.
- **One form that failed while it rendered failed the page.** A reference that named no form rendered nothing, but an
  exception while a form's model was built, or in its view (a site's override of a view, a field type of its own), went
  up to the page, which answered `500`, its other forms with it. The website script, too, enhanced every form in one loop,
  so one form that threw stopped the forms after it.
- **A late cache refresher told a visitor the form had changed.** A form's published version is kept per server until the
  server that changed it tells the others (ADR-0084), which Umbraco's database messenger carries a few seconds later. A
  visitor who loaded the newer revision on one server and sent it to another in between was answered `409 form_changed`,
  and a form published in between was answered as missing.
- **Blocked storage and a lost answer had no browser test.** The runtime keeps nothing in the browser (ADR-0009), and the
  receipt check after a lost answer was tested on the runtime alone, not on a real page with a real server.

## Decision

- **No mail server, no attempt.** An attempt that ends in a socket's error (the connection refused, no such host, the
  network unreachable) sent nothing, so the mail goes back to the queue with its attempt, the code `no_connection`
  (whose words the deliveries log already has) and the exception's type. The server that found no mail server then sends
  nothing for a pause that grows from half a minute through one, two and four minutes to five, and stays there: one try a
  pause however many mails wait, and an outage of any length fails nothing. The first mail that leaves ends the pause.
  Each server keeps its own (`MailServerReach`), as each reaches the mail server itself, and the health check *Email
  sending* says so while it lasts. A timeout is not a refusal: the mail may have left, so it stays uncertain (ADR-0007).
  A mail server that answers and refuses the mail still spends an attempt, as before.
- **A page outlives its form.** On a page, the view component renders the form's view into text of its own and writes it
  only once it has rendered in full, so nothing of a form that fails reaches the page. A form that fails renders nothing
  for a visitor, as a form waiting for the database update does (ADR-0033), and for a signed-in backoffice user a short
  note that names the exception's type, written without a view, as a view may be what failed. The page is kept out of
  every cache, so its next view renders the form again, and the log gets the exception's types and stack, never its
  message (ADR-0226). `IFormBuilderRenderer` does the same for a site's code. The render endpoint renders one form, and
  answers a failure as its own. The website script runs each form's work on its own: what one form throws is reported as
  an uncaught error is, for the console and a site's error handler, and that form is left to the post without JavaScript.
  A form whose address for counting the browser refuses works all the same.
- **The submission catches up.** A submission reads the form's row anyway (step 1 of section 4.4). Where that row names a
  newer public revision than this server's copy, the copy is read again before the answers are read, so a late
  instruction costs one read instead of a visitor's send. A post to the public API (a challenge, an upload, a page's
  check, a send, a saved form, a receipt) for a form this server keeps as unpublished reads its row, and the form again
  when it is published now. Only this server's copy is renewed; the server that published told the others. A key that
  names no form still costs one lookup (ADR-0149), and a read of the definition or the options answers from the copy. Pages and the definition endpoint keep serving the copy until the instruction comes, which is safe: an
  older revision's answers are read by the current fields (ADR-0002).
- **Browser tests for what only a browser shows.** A send whose answer is lost after the entry was stored shows that
  entry on the retry, with the receipt the lost answer had; and a browser that throws at every touch of `localStorage`,
  `sessionStorage` and `indexedDB` sends the form with no error on the page.

## The cases and their tests

| Case | Held by | Tested in |
| --- | --- | --- |
| The mail server is down: no attempt spent | `MailServerReach`, `EmailAttempts` | `MailServerReachTests`, `EmailAttemptTests`, `MailServerDownTests` (Umbraco's own sender against a port nothing listens on), `HealthProbeTests` |
| Mail is not set up: no attempt spent | `EmailQueue` (ADR-0007) | `EmailAttemptTests` |
| A webhook's receiver times out | the attempt's time limit, uncertain | `WebhookActionTests`, `ActionProcessorTests` |
| A transient database error | the outermost unit of work retried (Tandem ADR-0065) | `TransientRetryTests`, `FormBuilderDatabaseErrorsTests` |
| A host restart during an action run | a stopped attempt or an expired lease turns uncertain; retried only when a person confirms | `ActionProcessorTests`, `EmailAttemptTests`, `DeliveryApiTests` |
| A late cache refresher | the submission's catch-up | `PublicFormEntryTests`, `PublicFormCacheTests` |
| Double processing on several servers | claims by a conditional update, outcomes by the claim's token | `TwoProcessorsTests`, `EmailAttemptTests` (`TwoPassesAtOnce_SendEachMailOnce`), `EntryExportTests`, `FileScanTests` |
| The network drops during a send | the submission id and the receipt check (ADR-0120) | `send.test.ts`, `sending-on-the-page.spec.ts` |
| The browser blocks storage | the runtime keeps nothing there (ADR-0009) | `sending-on-the-page.spec.ts`, `privacy-facts.spec.ts` |
| One broken form on a page | `FormRenderFailures`, the view component, the frame's `alone` | `BrokenFormRenderingTests`, `frame.test.ts` |

## Consequences

- An outage of the mail server delays mail and fails none of it; the deliveries log shows the mails as waiting, and the
  health check says why. A server that keeps finding no mail server tries once every five minutes.
- A broken form costs its own place on a page, and only a backoffice user sees why. A visitor sees nothing there, as for
  a form not published; the log has what to fix.
- A form's view on a page is held as text until it has rendered, which a page's view component did not do before. The
  markup is the same.
- A submission on a server whose copy is behind reads the form once more; on every other submission nothing changes.
