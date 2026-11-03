// The library's workspaces (ADR-0166): each routes its screen by Umbraco's router, the item's key from `edit/<key>`, so that going
// from one folder to another in the tree gives the screen its new key.
import { html } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { UmbRoute } from '@umbraco-cms/backoffice/router';

/** A screen of the library: told which item it shows, or none for one without a key. */
export interface LibraryScreen extends HTMLElement {
  unique: string | null;
  entityType: string;
}

/**
 * A workspace element that shows one screen, for an entity type whose items have keys or not, registered under its tag: Umbraco
 * constructs a workspace's element from its class, which only a defined custom element allows.
 */
export function libraryWorkspace(tag: string, entityType: string, screen: () => Promise<unknown>, keyed: boolean) {
  const setup = (unique: string | null) => (component: HTMLElement) => {
    const target = component as LibraryScreen;
    target.entityType = entityType;
    target.unique = unique;
  };
  const routes: UmbRoute[] = keyed
    ? [
        {
          path: 'edit/:unique',
          component: screen as never,
          setup: (component, info) => setup((info.match.params as { unique: string }).unique)(component as HTMLElement),
        },
      ]
    : [{ path: '', component: screen as never, setup: (component) => setup(null)(component as HTMLElement) }];

  class LibraryWorkspaceElement extends UmbLitElement {
    override render() {
      return html`<umb-router-slot .routes=${routes}></umb-router-slot>`;
    }
  }

  if (!customElements.get(tag)) customElements.define(tag, LibraryWorkspaceElement);
  return customElements.get(tag) as typeof LibraryWorkspaceElement;
}
