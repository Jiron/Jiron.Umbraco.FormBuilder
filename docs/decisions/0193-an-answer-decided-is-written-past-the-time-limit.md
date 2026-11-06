# 0193. An answer the action built is written, even past the time limit

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.2d (its gate); amends [ADR-0084](0084-the-public-apis-frame-and-the-form-endpoint.md) (the public
  API's frame)

## Context

ADR-0084 gives every public endpoint a time limit on the site's clock: the filter links the limit to the visitor's own
token and hands the endpoint the linked token in `RequestAborted`; the middleware answers a cancellation after the
limit ran out with `504 timeout`. The filter is a resource filter, so the linked token was still in place while the
answer was written.

The public API writes its JSON answers as MVC file results of bytes it built. A file result that sees its token
cancelled while it writes does not throw: it aborts the connection, so that no part of a file goes out as a whole one. A
limit that ran out after the action had built its answer, and before the answer was written, therefore cut the
connection. The visitor got neither the answer nor `504`, and the middleware never saw a cancellation to answer. On a
loaded machine the gate met it once: a challenge that took longer than its ten seconds, in a test that ran 16 seconds,
failed with "The application aborted the request."

## Decision

**The time limit bounds the endpoint's work, not the writing of bytes it built.** The filter is also an always-run
result filter: before a file result of built bytes (`FileContentResult`) is written, it puts the visitor's own token
back in `RequestAborted`. Work that runs out of time still answers `504 timeout`, and a visitor who leaves still cancels
the write. A result that does its work as it writes, the render endpoint's view, keeps the time limit: a limit that runs
out there throws before the answer starts, which the middleware answers with `504`.

## Options considered

- **Answer `504` whenever the limit has run out by the time the answer is written.** The work is done by then: an entry
  is stored or a challenge issued. A `504` would send the visitor to a retry for something that succeeded, and a limit
  running out during the write would still cut the connection. Not chosen.
- **Every result on the visitor's token.** The render endpoint renders its view while its result writes, so its reads
  would leave the time limit. Not chosen.
- **Built bytes on the visitor's token.** Chosen. Writing them is no work, and the server's own limits for slow readers
  still apply.

## Consequences

- No public endpoint aborts a connection because of its time limit; the time-limit test makes a limit run out after the
  answer is built, and the answer arrives.
- A new endpoint that answers with a stream or a result that works as it writes keeps the time limit for that work.
