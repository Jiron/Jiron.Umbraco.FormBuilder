// What the rules builder reads from Umbraco (ROADMAP 9.5a, ADR-0175): the site's member groups, whose names rules
// compare. A user who may not read them gets no list and types a group's name; no error is shown for it.
import { UMB_CURRENT_USER_CONTEXT } from '@umbraco-cms/backoffice/current-user';
import { MemberGroupService } from '@umbraco-cms/backoffice/external/backend-api';
import { firstValueFrom } from '@umbraco-cms/backoffice/external/rxjs';
import type { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { tryExecute } from '@umbraco-cms/backoffice/resources';

/** The section whose users Umbraco lets read the member groups. */
const MEMBERS_SECTION = 'Umb.Section.Members';

/**
 * The site's member groups' names in alphabetical order; undefined where they can't be read. A user without the
 * Members section isn't asked for them, so the browser logs no refused request.
 */
export async function readMemberGroups(host: UmbLitElement): Promise<string[] | undefined> {
  const user = await host.getContext(UMB_CURRENT_USER_CONTEXT).catch(() => undefined);
  const sections = user ? await firstValueFrom(user.allowedSections) : undefined;
  if (!sections?.includes(MEMBERS_SECTION)) return undefined;
  const { data } = await tryExecute(host, MemberGroupService.getMemberGroup({ query: { skip: 0, take: 1000 } }), {
    disableNotifications: true,
  });
  return data ? data.items.map((item) => item.name).sort((one, other) => one.localeCompare(other)) : undefined;
}
