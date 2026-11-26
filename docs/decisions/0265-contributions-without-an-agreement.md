# 0265. Contributions come in without an agreement, and the history takes no personal address by accident

- Status: accepted; replaces the plan of ROADMAP §3.6 that outside contributions wait for a contributor licence
  agreement, and the commit check of ROADMAP 0.2 that accepted the maintainer's identity only
- Date: 2026-11-26
- Roadmap step: none; the owner's messages of 2026-11-25 and 2026-11-26, after the last step (the message beats the
  plan)

## Context

The roadmap kept pull requests from others closed until a contributor licence agreement existed (§3.6): GitHub's terms
put a contribution under the repository's licence, so without an agreement the owner holds no more rights in
contributed code than anyone else and cannot relicense it. Until then `CONTRIBUTING.md` said that contributions were
not open, and the `pre-commit` hook refused every commit identity but the maintainer's, which kept the owner's personal
address, the machine's global one, out of a history that was going to be public.

The repository is public since 2026-11-25. The owner wants to invite people to collaborate, and paid add-ons, if they
ever exist, would live in a repository of their own. On 2026-11-26, having heard what an agreement gives and costs, the
owner decided: no agreement.

Three things then stood in a contributor's way: the guide, the commit check, and that a pull request ran no checks at
all, since the workflow ran for pushes, tags and by hand only. The last one matters twice: without it the only way to
test a stranger's change is to run it on the maintainer's machine.

## Options

For the commit check:

1. **No check.** Simple, and the maintainer's protection is gone: one commit made in a fresh clone on this machine
   carries the personal address, and a pushed commit is not taken back (no force push, CLAUDE.md rule 1).
2. **The maintainer's identity where a clone asks for it, anything elsewhere.** It protects only a clone that was set
   up, so a fresh clone fails open.
3. **GitHub's private commit addresses by default, for everyone; a clone may allow its own address.** A fresh clone
   fails closed, the maintainer needs no setting, and a contributor gets the same protection the owner wanted: nobody
   publishes an address by accident in a history that cannot be rewritten. It costs a contributor one refused commit
   and one command.

## Decision

- **No agreement.** A contribution stays its author's and comes under the project's licence, as GitHub's terms say and
  as `CONTRIBUTING.md` and the pull request template now say in the project's own words: by opening a pull request the
  author confirms that they wrote it and may contribute it, and agrees that it is licensed under the PolyForm Perimeter
  License 1.0.1 and distributed as part of Jiron Form Builder. The original-work rules (CLAUDE.md rule 2) apply to
  every contribution as they do to the build.
- **The commit check is option 3** (`scripts/lib/identity.mjs`): the author and the committer need a name and an
  address at `users.noreply.github.com`; `git config jfb.ownEmail true` allows any address in that clone. The refusal
  says both ways and never repeats the address. The maintainer's commits keep the repository's identity (CLAUDE.md
  rule 9), which the check accepts like any other private commit address.
- **A pull request against `main` or `v18/main` runs the whole workflow** (`ci.yml`, the trigger `pull_request`): on a
  hosted runner, with a read-only token and no secrets, which the workflow never needed. For a first contribution
  GitHub asks the maintainer to approve the run. Pushes to `main` stay gated by `JFB_CI`.
- **The hooks stay a contributor's own choice** (`node scripts/install-hooks.mjs`); nothing on GitHub checks the
  address of a pull request's commits. Whoever skips the hooks publishes the address they configured, as on any
  repository.

## Consequences

- **What the owner gives up:** changing the licence of contributed code, or putting it into a product under another
  licence, needs the consent of everyone who wrote it. An add-on that only builds on the packages is not affected; one
  that takes code out of this repository is, for the parts others wrote. `docs/OWNER-TODO.md` says so where it asks
  about paid add-ons.
- **Not seen running:** the `pull_request` trigger passes the workflow checks (`node scripts/check-workflows.mjs`),
  but no pull request existed when it was written. The first one shows whether every job runs from a fork.
- A commit made with the machine's global identity is still refused in every clone, so the reason for the first check
  holds. A merge made on GitHub is GitHub's own commit and passes no hook.
- The check in ROADMAP 0.2 that every commit carries the maintainer's address described the build; from the first
  merged contribution the history holds other authors.
