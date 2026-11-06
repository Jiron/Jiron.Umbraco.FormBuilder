# 0190. Test an action as the sidebar holds it

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.2c; builds on [ADR-0158](0158-the-action-framework.md) (test runs),
  [ADR-0187](0187-the-outcome-preview.md) (the sample entry and a draft's preview),
  [ADR-0188](0188-the-actions-view.md) (the Actions view) and [ADR-0189](0189-the-email-editor.md) (the email editor)

## Context

Test runs exist since 8.1 (`POST form/{key}/action/{actionKey}/test`, `manageActions`): a type declares whether its test
reaches nobody (`DryRun`), only the asking user (`ToCurrentUser`) or what the action is set up to reach after a
confirmation (`AfterConfirmation`), and the run takes the sample entry. What the survey found:

1. **It tests the stored draft.** The Actions view holds the draft in the workspace, saved or not, and the sidebar holds
   an action as Add or Update would make it; a test of the stored draft tests something else, or nothing for a new
   action.
2. **It answers in English sentences** ("Sent to …", "Your account has no address a test mail can go to.", "The host
   answered 200."), which a German backoffice cannot say in its words.
3. **The test mail does not say it is one.** It goes to the tester, but nothing in it names who the real mail goes to
   (IA 5.3: its first line does), and it is queued even where the site cannot send mail, so it never arrives and the
   test says it was sent.
4. **Settings the type cannot read** fail the test as "The test could not finish.", where the form check says what is
   wrong and the preview answers `action_settings_invalid` (ADR-0189).
5. **Where Test goes:** IA 5.3 and the wireframes put Test on each card, beside the switch, and in the sidebar's footer,
   for who may manage the actions; copy.md has "Test" and "Test email sent to you (ana@example.com).".

## Options

For what is tested:

1. **The stored draft only:** save first, then test.
2. **The draft the request carries,** as the previews take it, the stored draft without one.

For what a test says:

1. **The type's sentence,** as now.
2. **A code the backoffice words,** with the type's sentence for a code it does not know.

## Decision

**Option 2 in each.**

- **The request** of the test endpoint may carry the definition as the designer holds it (`definition`), and the action
  is the one of that key in it; without one, the stored draft, as before. The card tests the action as the workspace's
  draft holds it, and the sidebar as Add or Update would make it, saved or not. The endpoint stays, with its permission
  (`manageActions`): a test sends mail or calls a host.
- **Settings the type cannot read** answer `action_settings_invalid` (409), as the preview does, the schema checked
  first.
- **A result has a code** (`ActionTestResult.Code`, optional, so a site's own type need not give one): `sent_to_you`,
  `sent`, `dry_run`, and for a failure `no_address`, `mail_off`, `paused`, `view_missing`, `failed`, or the type's own.
  The response adds the code, and `recipient`, the address a test that reaches the asking user went to. The backoffice
  says a code it knows in its words and the type's sentence for one it does not.
- **While actions and email are paused** (section 4.4), a test that would reach anything answers `paused` and nothing
  runs or waits in the queue, so no test goes out later unasked; this comes before the confirmation, so nobody confirms
  a test that cannot happen. A dry run reaches nothing and still runs.
- **The test mail** goes to the tester alone, as before, and its first line, in the tester's backoffice language (English
  or German, as the setup test mail), says it is a test and names who the real mail goes to for the sample entry,
  routed as a run routes it and as the outbox keeps addresses (trimmed, each one address, once): "This is a test. The
  real email goes to sales@example.com, and to alex.berger@example.com in a mail of its own." The rest of the mail is
  the real one, in the form's language: where only the visitor gets a mail, it is the visitor's, its answers' links as
  the visitor gets them, and its line names the visitor's address alone. Where the site cannot send mail, the test says
  so (`mail_off`) and queues nothing.
- **In the backoffice,** Test is on each card beside the switch and in the sidebar's footer, for who may manage the
  actions; a card too narrow to keep its name readable beside Test offers it in its "…" instead, once either way. Its
  title says what its type's test does before it runs ("Sends a test to you alone."):
  - a type that tests after a confirmation asks first, in Umbraco's confirm dialog, naming where it goes in its card's
    own line ("Webhook to hooks.example.com. A sample entry goes there as a real one would, marked as a test.");
  - what a test came to is a toast whose message says it on its own, led by the action's name so that several tests'
    toasts tell their actions apart, since Umbraco's toast reads its message aloud and not a headline: "Email the team:
    Test email sent to you (ana@example.com).", a failure's reason with what to do about it, or a dry run's "Nothing was
    sent or changed." with what a run would do, one line each;
  - once it runs, after a yes where it asks first and never before, its button is busy and the list or the sidebar
    says "Testing …" in its live region; a second press waits for the first.

## Consequences

- `ActionTestResult` gains `Code` and `ActionTestCodes` names the shared ones; the email type and a test that throws
  set theirs here. So that no toast contradicts itself, the content and state types, which reach nobody, answer as dry
  runs here (`dry_run`, their sentence as the preview), and the webhook `sent` or its run's code (`http_<status>`,
  `not_allowed`, …) under a sentence naming it. 10.2d says those codes and lines in the backoffice's words, with the
  editors.
- The test endpoint's request gains `definition`, which the OpenAPI snapshot shows, and its answer `code` and
  `recipient`.
- The mail of a test carries a line no real mail has; its recipients are the run's own, from the rule a run and the
  preview share (ADR-0160, amended in ADR-0189).
- 10.2d test-runs the other types through the same button and dialogs; 10.2e's Details offers the same Test.
