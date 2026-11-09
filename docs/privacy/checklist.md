# Privacy checklist for site owners

Jiron Form Builder ships privacy-friendly defaults and the tools to answer people, but the decisions are yours: you are
the controller of what your forms collect. Work through this list with your data protection adviser before a form goes
live, and again when a form's purpose changes. The [facts sheet](facts.md) says what the package does by itself.

## For each form

- [ ] **Purpose.** Write down why the form exists and which answers it really needs; leave out every field you could do
      without.
- [ ] **Legal basis.** Decide what allows the processing: a contract or steps before one, a legal obligation, a
      legitimate interest you have weighed, or consent. Only consent needs a consent field.
- [ ] **Consent fields.** Where you rely on consent, use a consent field, never ticked in advance, with wording that says
      what the person agrees to and links to your privacy policy. Jiron Form Builder stores the exact wording shown.
- [ ] **Newsletters and other advertising mail.** Turn on "Email verification" in the form's Submission settings, so a
      subscription counts only once the person confirmed it from their mailbox (double opt-in, the usual way to prove
      such consent under UWG § 7). The entry's timeline keeps when it was confirmed: give the form's entries a retention
      as long as you send the person mail, since that record is your proof. Hand only confirmed entries to your mailing
      tool; an export that chooses no state leaves the unconfirmed ones out.
- [ ] **Sensitive answers.** Mark health, financial and other special data as sensitive, and give "View sensitive data"
      only to the people who need it.
- [ ] **Answers only the actions need.** Set "Keep the answer" to "Until the entry's actions have run" for what only
      the mail or the webhook needs, such as a reference passed on to another system.
- [ ] **How long.** Check the form's retention days under its Settings view's Privacy group, or the site's under
      Privacy › Retention. Set "Store entries" to "Never" where you need nothing kept.
- [ ] **IP addresses.** Keep none unless you have a reason; if you do, prefer the address with its end removed, and say
      so in your privacy policy.
- [ ] **Captcha.** Prefer the built-in protection, which talks to no one. If you choose a captcha, name its provider in
      your privacy policy, sign its data processing terms where it offers them, and pick a consent mode that asks first.
      A consent field that loads the captcha should be one of its own, naming the provider, not the field that agrees to
      the form's purpose. List the cookies the provider's script sets in your consent tool.
- [ ] **Members.** An entry keeps which member sent it when the visitor is signed in. Until a form can leave that out
      (roadmap step 14.3), keep a form meant to be anonymous off the pages members use signed in.
- [ ] **Actions that send data elsewhere.** Every webhook, content action or mail to a third party makes its receiver a
      recipient you must name, and often a processor you need a contract with; check transfers outside the EU and
      Switzerland.
- [ ] **Accessibility.** The shipped themes meet WCAG 2.2 AA. If you style forms yourself, check contrast, focus and
      error messages again, and mention forms in your accessibility statement (European Accessibility Act).

## For the site

- [ ] **Privacy policy.** Add a section about your forms; the [English](policy-template.en.md) and
      [German](policy-template.de.md) templates are a start, never the finished text.
- [ ] **Record of processing.** List each form's purpose and processing in your record of processing activities (GDPR
      Art. 30; revDSG Art. 12). A Swiss
      company with fewer than 250 employees whose processing carries little risk may be exempt (DSV Art. 24): check it.
- [ ] **HTTPS.** Serve every page with a form over HTTPS only (GDPR Art. 32; TDDDG § 19(4)).
- [ ] **Access logs.** The confirmation link carries its code in the page's address; keep your web server's access logs
      short, or leave query strings out of them.
- [ ] **Who sees entries.** Review Settings › Access: give each group only the forms and permissions it needs, and
      restrict folders that hold HR, health or other confidential forms.
- [ ] **Mail.** Your mail provider handles the addresses and contents of every mail; make sure you have a data
      processing agreement with it, and that confirmation mails quote only what the person needs.
- [ ] **Proxies.** Behind a proxy or load balancer, set up ASP.NET Core's forwarded headers, so rate limits and any IP
      address you keep are the visitor's.
- [ ] **Backups.** Backups of your database and file store hold entries too; give them a retention of their own.
- [ ] **Exports.** Files your users export or download leave Jiron Form Builder's care; agree how long they keep them.
- [ ] **Health.** The Privacy health check on the Overview passes, or you know why it warns.

## When a person asks

- [ ] **Know the deadline.** Answer within one month (GDPR Art. 12), or 30 days in Switzerland (revDSG Art. 25 with
      DSV Art. 18).
- [ ] **Find their data** under Privacy › People by their email address or as a member. People looks in email fields;
      search the entries lists too for an address someone typed into a text field.
- [ ] **Access and portability:** Export everything gives them their answers, consent wordings, timeline, notes and
      files as a ZIP.
- [ ] **Erasure:** Erase… shows what goes first; delete the entries, or anonymise them where you must keep the counts.
      Check whether a law obliges you to keep some entries, and put those on legal hold instead.
- [ ] **Copies outside:** delivered mail, downloads, exports, Umbraco content a content action created (and its
      versions), what a webhook's receiver keeps, and what your own code stored from Jiron Form Builder's notifications
      are not reached by Erase…; deal with them separately.
- [ ] **Keep a note** of the request and your answer; the audit log records each step without naming the person.
