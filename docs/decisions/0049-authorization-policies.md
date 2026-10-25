# 0049. Authorization policies of the management API

- Status: accepted; amended by [ADR-0050](0050-management-api-base.md) (a policy for what a user sees,
  `Jiron.FormBuilder.Policy.Visible`)
- Date: 2026-10-25
- Roadmap step: 4.6b (decided and built); 4.7 maps the denials to answers and puts the policies on its endpoints

## Context

ADR-0013 decided one authorization policy per permission with a singleton requirement handler, endpoints that
evaluate a permission against the form or folder they act on, and answers that keep keys secret: what a user may not
see answers `404`, what they may see but not do `403` with the permission's name, and a call without the Forms section
`403`. ADR-0048 built the evaluation and the lists. Umbraco's own section requirement is internal and the
allowed-applications claim is obsolete in 17, so a handler reads the user's sections through `IAuthorizationHelper`;
authorization handlers must be singletons, because Umbraco's singleton event authorizers consume
`IAuthorizationService` (§3.4, 1.4b).

ASP.NET Core's authorization answers only yes or no. A policy cannot set a status code, and an endpoint's
`[Authorize]` attribute has no resource to check against. Something has to carry *why* a check failed to the place
that writes the answer, and the endpoints that act on a form or folder need a resource the handler understands.

## Options

1. **Filters per endpoint** (as Tandem's role filter does): a filter reads the route, loads the form and answers.
   It decides status codes in one place, but bypasses ASP.NET's policies, so `IAuthorizationService`, which
   Umbraco's own resource checks use, and the permission matrix of 4.7 cannot ask it.
2. **Policies with resources and denial reasons:** a policy per permission and one for the section, evaluated by
   the attribute globally or by `IAuthorizationService` against a resource; a failing handler names its reason, which
   4.7's answers read.

## Decision

**Option 2**, in `Web/ManagementApi/Authorization`:

- **The policies** (`FormBuilderPolicies`): `Jiron.FormBuilder.Policy.Section`, and `Jiron.FormBuilder.Policy.<name>`
  for each permission (`…design`, `…viewEntries`, …), with the permission's stable name (ADR-0048). Each takes the
  backoffice's token scheme and an authenticated user, so an anonymous call is challenged (`401`), and one
  requirement: `FormBuilderSectionRequirement`, or `FormBuilderPermissionRequirement` with its permission.
- **The resources** (`FormBuilderAccessResource`): globally (also when the attribute checks without a resource), a
  folder by key or the root, and a form by its row (`FormAccessTarget`: key, folder and restriction, which the
  endpoint has read anyway, ADR-0048).
- **The handlers** are singletons. They take the user through `IAuthorizationHelper.TryGetUmbracoUser`; a principal
  that is no backoffice user gets no decision, so the policy fails as it would for anyone unknown. The permission
  handler holds the section requirement itself, so every permission policy refuses a user without the Forms section
  first, and then evaluates the resource through the access service.
- **Denials** (`FormBuilderAccessDenial`, an `AuthorizationFailureReason` with a kind and the permission):
  - `noSection`: the user lacks the Forms section (`403`);
  - `notFound`: the user cannot see the folder or form (`404`, as for a key that does not exist);
  - `missingPermission`: the user sees it but lacks the permission (`403` naming it).

  **Seeing** is what the user's lists show (ADR-0048): a form the user holds any permission on, and a folder the user
  holds something on or that leads to such a folder or form, so a folder on the way to a form granted on its own
  answers `403`, never `404`, while it shows in the tree. The root and the global checks are always seen.
- **Tests sign in through Umbraco:** the integration tests create API users in the personas' groups and sign in with
  Umbraco's own client-credentials flow at its token endpoint, so the principal is the one the backoffice's scheme
  validates; a probe controller in the test project asks every policy against each resource, and two attribute
  endpoints show the scheme. The browser tests read the personas' effective grants from the test host.
- **The test host's seed** (ADR-0029): the Testing project's user groups take their global grants by name, and
  `JironFormBuilderTesting:RestrictedFolders` seeds root folders that are restricted, each with its own grant to one
  group, every start bringing the grants to what the settings say. The E2E host seeds the folder `HR` for a separate
  group, `jfbHrTeam`, with its own persona, `hr`.

## Consequences

- 4.7 writes every refusal from the denial of the failing check, so status codes are decided in one place and the
  permission matrix asks the same policies the endpoints use.
- Endpoints that act on a form or folder carry the section policy on the attribute and check their permission
  against the resource they loaded; global endpoints carry their permission's policy.
- A global-only permission checked against a folder or form is never held there, administrators included; the
  endpoints check those globally.
- Each check reads the user once more through Umbraco; the evaluation itself comes from the cached snapshot.
