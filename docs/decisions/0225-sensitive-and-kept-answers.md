# 0225. Sensitive answers and answers kept until the actions have run: where copies may live

- Status: accepted; builds on [ADR-0009](0009-privacy-defaults.md) (sensitive fields, values kept only until the actions
  have run, logs), [ADR-0014](0014-merge-tag-language.md) (sensitive fields in mail),
  [ADR-0158](0158-the-action-framework.md) (the settlement) and [ADR-0223](0223-data-subject-tools.md)
- Date: 2026-11-09
- Roadmap step: 13.1, its part 13.3 (ADR-0197)

## Context

ADR-0009 keeps a sensitive answer out of everything but the entry itself: lists, titles, search text, the timeline's old
and new values, mail unless an action names it, exports unless chosen, and logs. A value kept only until the actions have
run goes once its entry's runs are final and no trigger can fire (ADR-0158). A look at every copy for step 13.3 found
three places where that was not yet so:

- an edit's event stored the old and new texts of a sensitive answer, masked only when read, and showed them to anyone
  allowed to see sensitive data, without a reveal or an audit entry;
- when a kept value went, the copies its actions had made stayed: mail that quoted or was addressed to it, the error
  message a server answered with, and an edit's event; and the removal was not in the audit log;
- an entry whose kept value was never stored, because no action could use it, showed the field as unanswered, and entry
  exports showed a gone value as an empty cell.

## Decision

1. **An edit names the field and keeps no texts** of a sensitive answer or of one kept only until the actions have run.
   Its event stores the field, its label and that it is withheld; the timeline says the field changed, to everyone. The
   current value stays in the entry, revealed and audited one at a time (ADR-0013).
2. **A kept value goes with every copy.** When the settlement removes it, it also deletes the entry's mail, all of which
   has left or failed by then, and forgets its runs' error messages, keeping their codes. Mail is deleted rather than
   blanked, so the Deliveries screen never offers to send an empty mail again; the entry's timeline still says what its
   actions did. The removal writes the `valuesRemoved` event and the audit entry `entry.valuesRemoved`, which names the
   fields, never a value.
3. **A kept value no action could use is never stored**, and the entry's timeline gets the same `valuesRemoved` event at
   once, so the panel, the entry exports ("not kept", in the exporter's language) and the person's export (`notKept`)
   say it alike.
4. **The designer explains the setting next to Sensitive.** Keep the answer follows Sensitive in the Advanced section,
   with help that says what goes and when, and that Sensitive masks where this removes.
5. **Tests guard it.** A test searches every Jiron Form Builder table for a kept value once its entry's runs are final and
   finds nothing; another captures every log line of a submission whose actions fail and whose mail goes out, and finds
   none of the visitor's answers, address, sensitive answer, kept answer or IP address.

## Consequences

- A form with a field kept until the actions have run keeps no mail of its entries once they are through; a failed mail
  of such an entry cannot be sent again from Deliveries. That is the field's purpose.
- Who may see sensitive data no longer reads an edit's old and new sensitive values on the timeline.
