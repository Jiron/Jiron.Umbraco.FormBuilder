# 0155. Moving through pages in the browser

- Status: accepted; lifts what ADR-0120, ADR-0152 and ADR-0153 left to this step
- Date: 2026-11-02
- Roadmap step: 6.5d (decided and built)

## Context

Every page of a form is on the page already, hidden but for the current one, and every post carries every answer (ADR-0100). Next
posts and the server shows the next page, which loses a file's token and a drawing, and the headless element has no post to go to at
all. The server writes the buttons and the progress for the current page alone, and the summary's rows only when it is reached.

## Options

**What the runtime shows on a move:** ask the render endpoint for the page, with the visitor's answers, which it does not take and a
cached page cannot hold; or have the server write what a move needs up front. Up front: every button in the form's templates, once,
in the theme's views, with the form's own labels, the words of the progress with their numbers to fill in, and the step of a page the rules hide
in a template, so that the themes' counting of steps and the folding of long names see only the steps that show. A progress with one
page to show is written hidden, for a rule to show. A view that writes no buttons there goes on posting.

**Checking a page:** the browser's own checks alone, which leave out what only the server decides; or `validate` after them, which
holds the challenge and the same rate limit as a send. The endpoint: nothing moves on until the server passed the page. Its errors
show at their fields; a refusal it cannot place, and any answer the page has no words for, goes to the post, as a send's does.

**A summary page:** write its rows in the browser, which repeats how the server words every field type; give the endpoint a way to
render them from posted answers; or let the step onto it post. The post: the server has those rows, and that one step costs a page
load. So a form with a summary page and files posts its files as it did, as a post cannot claim a token (ADR-0152). Its other pages
move on the page, and Back too. The headless element has nowhere to post, so it moves there and takes its files on the page, and the page shows no rows.

**Where the focus goes:** stay on the button, which is gone or means something else now; the first field, which skips the page's title
and description; or the page's title, or the page itself where it has none. The title: a screen reader reads where the visitor is, and
Tab goes on from there. The live region says the step, and the form's top comes into view with its progress.

**The copy of the answers after a send:** carry it in the entries answer, which also serves retries and has no theme, while a receipt
never carries answers (ADR-0123); or use the token a post's redirect uses. The token: the page names its form's id in its send, the
answer holds a token for it, and the render endpoint takes it as `jfb-sent` and writes the outcome and the copy in the page's theme. The
first request takes the entry's mark, as a post's first view does, so the answers show once.

**`next()` and `back()`:** press the button as `submit()` does, with what a visitor's press does; or move directly, which would skip
the checks. They press. False says the form shows no such button now, and `jfb:page` tells of the move.

## Decision

A chunk loaded for a form of several pages that sends on the page takes the moves over. `jfb:page` tells a site the form, the page's
key, its number and how many the rules show, and never an answer. A send whose errors lie on another page shows the first page that
holds one, with that page's errors. The rules follow the page that shows, and one that hides the page itself takes the visitor to the next page
it leaves, or the one before, as a post would. While a check is out nothing moves: its answer goes by the rules as they are then. A
page's check holds only to a file that failed on that page, and waits for all that is on its way. A form published again meanwhile
comes in on the page the visitor was on, if it still has it, and its rules decide at the wake, as its render knows no answers; one with
a summary page, a view without the buttons, a repeater or a ranking still goes by post (ADR-0154).

## Consequences

- A form of several pages takes its files on the page, and a signature drawn: a page's check waits for what is on its way, and makes
  the drawing's image, as a send does. One made and changed later is made again.
- The markup grows by one navigation per form of several pages, a hidden step per hidden page and three attributes.
- A rule that changes which pages show changes the buttons and the progress as the visitor answers.
- Each move forward counts against the same rate limit as a send.
- A check that fails by the network or a limit stays on the page with a send's notice, rather than moving on unchecked.
- "Fill in the form again" after a send on the page is the address the visitor is on, which a browser only scrolls to, so the runtime
  takes the page anew.
- A send names its form's id only as the server writes one (`jfb-` and a number), so a view's own id gets no copy but still sends.
