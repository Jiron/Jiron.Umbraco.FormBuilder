/*
 * The wireframe kit's frame (step 2.2; design documents only, never shipped). Each page holds only its workspace
 * and overlays; this script adds Umbraco's top bar and the Forms section's sidebar for the role the page shows (the
 * five places first, then the tree of forms; ADR-0257), draws the place's tabs in the header of a tab's page,
 * draws the icons, and switches states.
 *
 * On <body>:
 *   data-role       designer | handler | viewer | admin | privacy | none   (the personas of ADR-0013)
 *   data-active     what is open: a place's tab (inbox, spam, option-lists, ...), whose place the sidebar marks and
 *                   whose tabs a header with a title shows in its place, form:<id>, folder:<id> or bin
 *   data-tree       full (default) | empty (a fresh install: no forms, no entries) | seen (no unseen entries)
 *   data-role-<state>, data-active-<state>, data-tree-<state>   the same, for one state only
 * Parts with data-state="a b" show only in those states; ?state=<name> picks the state (default: "default").
 * <i data-i="name"></i> becomes that icon from icons.js. A workspace header that runs out of room folds its view tabs
 * into "…", as Umbraco does, and then its links into the header's "…". The script sets <html data-wf-ready="true">
 * when done.
 */
(() => {
  const state = new URLSearchParams(location.search).get('state') || 'default';
  const body = document.body;
  const role = body.getAttribute(`data-role-${state}`) || body.dataset.role || 'admin';
  const active = body.getAttribute(`data-active-${state}`) ?? body.dataset.active ?? '';
  const treeMode = body.getAttribute(`data-tree-${state}`) || body.dataset.tree || 'full';

  // The five places and their tabs (ADR-0257), each place shown to a role that may open any of its tabs.
  const PLACES = [
    { id: 'inbox', icon: 'inbox', name: 'Inbox', badge: 18, tabs: [['inbox', 'Inbox', 'inbox'], ['spam', 'Spam', 'spam']] },
    { id: 'library', icon: 'book', name: 'Library', tabs: [['option-lists', 'Option lists', 'list'], ['themes', 'Themes', 'palette'], ['email-layouts', 'Email layouts', 'layout-list'], ['templates', 'Templates', 'layers']] },
    { id: 'settings', icon: 'settings', name: 'Settings', tabs: [['general', 'General', 'settings'], ['email', 'Email', 'mail'], ['spam-protection', 'Spam protection', 'shield'], ['files', 'Files', 'paperclip'], ['access', 'Access', 'lock'], ['integrations', 'Integrations', 'plug']] },
    { id: 'privacy', icon: 'key', name: 'Privacy', tabs: [['people', 'People', 'users'], ['retention', 'Retention', 'timer'], ['data-collection', 'Data collection', 'eye']] },
    { id: 'activity', icon: 'activity', name: 'Activity', tabs: [['deliveries', 'Deliveries', 'send'], ['audit-log', 'Audit log', 'list']] },
  ];
  const ALL_TABS = PLACES.flatMap((place) => place.tabs.map(([id]) => id));
  // Roles: which tabs each persona may open (docs/ux/information-architecture.md, section 11).
  const ROLES = {
    designer: { name: 'Lea Hoffmann', create: true, entries: false, tabs: ['option-lists', 'themes', 'email-layouts', 'templates'] },
    handler: { name: 'Jonas Berger', create: false, entries: true, tabs: ['inbox', 'spam'] },
    viewer: { name: 'Paul Okafor', create: false, entries: true, tabs: ['inbox', 'spam'] },
    admin: { name: 'Ana Novak', create: true, entries: true, tabs: ALL_TABS, sections: 'all' },
    privacy: { name: 'Mira Sandberg', create: false, entries: true, tabs: ['inbox', 'spam', 'people', 'retention'] },
    none: { name: 'Sam Weller', create: false, entries: false, tabs: [], noAccess: true },
  };
  // The forms tree every page shares: folders first, then forms, each by name; the Recycle Bin last.
  const FULL_TREE = [
    { id: 'campaigns', type: 'folder', name: 'Campaigns', children: [] },
    { id: 'events', type: 'folder', name: 'Events', children: [
      { id: 'summer-fair', name: 'Summer fair sign-up', unseen: true },
      { id: 'volunteer', name: 'Volunteer sign-up', unpublished: true },
    ] },
    { id: 'hr', type: 'folder', name: 'HR', locked: true, children: [{ id: 'job-application', name: 'Job application', unseen: true }] },
    { id: 'callback', name: 'Callback request' },
    { id: 'contact', name: 'Contact us', unseen: true },
    { id: 'newsletter', name: 'Newsletter sign-up' },
    { id: 'quote', name: 'Quote request', unpublished: true },
  ];
  const TREE = treeMode === 'empty' ? [] : FULL_TREE;
  const unseenShown = treeMode === 'full';

  const r = ROLES[role] || ROLES.admin;
  const icon = (name, cls = '') => `<svg class="wf-i ${cls}" aria-hidden="true" focusable="false"><use href="#${name}"></use></svg>`;
  const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);

  function treeItem(node, depth) {
    const key = node.type === 'folder' ? `folder:${node.id}` : `form:${node.id}`;
    const current = active === key;
    const open = node.children?.length > 0 && (current || node.children.some((c) => active === `form:${c.id}`));
    const chevron = node.children?.length
      ? icon(open ? 'chevron-down' : 'chevron-right', 'wf-item__chevron')
      : icon('chevron-right', 'wf-item__chevron wf-item__chevron--none');
    const marks = [];
    if (node.locked) marks.push(`${icon('lock')}<span class="wf-sr">restricted</span>`);
    if (node.unseen && unseenShown && r.entries) marks.push('<span class="wf-dot"></span><span class="wf-sr">unseen entries</span>');
    const muted = node.unpublished ? ' wf-item--muted' : '';
    const sr = node.unpublished ? '<span class="wf-sr"> (not published)</span>' : '';
    let html = `<a class="wf-item wf-item--tree${muted}" style="--depth:${depth}" href="#"${current ? ' aria-current="page"' : ''}>${chevron}${icon(node.type === 'folder' ? 'folder' : 'form')}<span>${esc(node.name)}${sr}</span>${marks.length ? `<span class="wf-item__end">${marks.join('')}</span>` : ''}</a>`;
    if (open) html += node.children.map((c) => treeItem(c, depth + 1)).join('');
    return html;
  }

  // A place's tabs the role may open, and the place the open page belongs to.
  const tabsOf = (place) => place.tabs.filter(([id]) => r.tabs.includes(id));
  const openPlace = PLACES.find((place) => place.tabs.some(([id]) => id === active));

  // The places first, one list without a heading, each leading to its first tab the role may open; what grows (the
  // tree) comes after what is fixed (ADR-0254's fourth rule).
  function places() {
    const links = PLACES.filter((place) => tabsOf(place).length > 0).map((place) => {
      const b = place.badge && unseenShown && r.entries ? `<span class="wf-item__end"><span class="wf-badge">${place.badge}</span><span class="wf-sr">unseen</span></span>` : '';
      return `<a class="wf-item" href="#"${place === openPlace ? ' aria-current="page"' : ''}>${icon(place.icon)}<span>${place.name}</span>${b}</a>`;
    }).join('');
    return links ? `<nav class="wf-side__places" aria-label="Form Builder">${links}</nav>` : '';
  }

  function sidebar() {
    const head = `<div class="wf-side__head"><span>Forms</span>${r.noAccess ? '' : `<span class="wf-side__head-actions"><button class="wf-btn wf-btn--icon wf-btn--small wf-btn--text" aria-label="More actions for Forms">${icon('more')}</button>${r.create ? `<button class="wf-btn wf-btn--icon wf-btn--small wf-btn--text" aria-label="Create in Forms">${icon('plus')}</button>` : ''}</span>`}</div>`;
    if (r.noAccess) {
      return `${places()}${head}<p class="wf-muted" style="padding: 0 var(--wf-space-6)">You can't see any forms yet. An administrator can give your user group access in Settings › Access.</p>`;
    }
    const tree = TREE.map((n) => treeItem(n, 0)).join('')
      + `<a class="wf-item wf-item--tree" style="--depth:0" href="#"${active === 'bin' ? ' aria-current="page"' : ''}>${icon('chevron-right', 'wf-item__chevron')}${icon('trash')}<span>Recycle Bin</span></a>`;
    return `${places()}${head}<nav aria-label="Forms">${tree}</nav>`;
  }

  function topbar() {
    const all = ['Content', 'Media', 'Settings', 'Packages', 'Users', 'Members', 'Translation'];
    const sections = (r.sections === 'all' ? all : ['Content', 'Media']).map((s) => `<a class="wf-top__section" href="#">${s}</a>`).join('');
    const initials = r.name.split(' ').map((p) => p[0]).join('');
    return `<header class="wf-top"><span class="wf-top__logo" aria-hidden="true"></span><nav class="wf-row" aria-label="Sections">${sections}<a class="wf-top__section" href="#" aria-current="page">Forms</a></nav><div class="wf-top__tools">${icon('search', 'wf-i--20')}${icon('help', 'wf-i--20')}<span class="wf-top__avatar" title="${esc(r.name)}">${initials}</span></div></header>`;
  }

  // Icons first, so the frame and the page can use them.
  if (window.WF_ICONS) body.insertAdjacentHTML('afterbegin', window.WF_ICONS);
  for (const el of document.querySelectorAll('[data-i]')) {
    const tpl = document.createElement('template');
    tpl.innerHTML = icon(el.dataset.i, el.className);
    const svg = tpl.content.firstElementChild;
    for (const { name, value } of el.attributes) if (name !== 'class' && name !== 'data-i') svg.setAttribute(name, value);
    el.replaceWith(svg);
  }

  // States.
  for (const el of document.querySelectorAll('[data-state]')) {
    if (el.dataset.state.split(/\s+/).includes(state)) el.classList.add('wf-on');
  }
  document.documentElement.dataset.wfState = state;

  // The frame, unless the page is a full-screen modal of its own (data-frame="none").
  if (body.dataset.frame !== 'none') {
    const main = document.querySelector('.wf-main');
    const app = document.createElement('div');
    app.className = 'wf-app';
    app.innerHTML = `${topbar()}<div class="wf-body"><aside class="wf-side">${sidebar()}</aside></div>`;
    main.replaceWith(app);
    app.querySelector('.wf-body').append(main);
  }

  // Umbraco 17.7.1 folds its sections into "…" in a 920 px window, keeping the open one (research section 12).
  const sectionsNav = document.querySelector('.wf-top nav');
  if (sectionsNav && window.innerWidth < 960) {
    for (const a of sectionsNav.querySelectorAll('.wf-top__section:not([aria-current])')) a.remove();
    sectionsNav.insertAdjacentHTML('beforeend', `<a class="wf-top__section" href="#" aria-label="More sections">${icon('more', 'wf-i--20')}</a>`);
  }

  // A tab's page: its header's title gives way to the place's tabs the role may open, a heading for screen readers
  // naming the place and the tab; with one tab, the heading alone. A header without a title (an editor opened from a
  // tab, such as a theme's) keeps what it has.
  if (openPlace) {
    const tabs = tabsOf(openPlace);
    const tab = openPlace.tabs.find(([id]) => id === active);
    for (const head of document.querySelectorAll('.wf-head')) {
      const titles = head.querySelectorAll('h1.wf-head__title');
      if (titles.length === 0) continue;
      const main = head.querySelector('.wf-head__main');
      for (const title of titles) title.remove();
      const row = tabs.length > 1
        ? `<nav class="wf-tabs wf-tabs--place" aria-label="${esc(openPlace.name)}">${tabs.map(([id, name, ic]) => `<a class="wf-tab" href="#"${id === active ? ' aria-current="page"' : ''}>${icon(ic)}<span>${esc(name)}</span></a>`).join('')}</nav>`
        : '';
      // Second-level, as the build has it: Umbraco's dialogs over the screen head with third-level headings; the name
      // once where the tab has the place's.
      const heading = openPlace.name === tab[1] ? esc(tab[1]) : `${esc(openPlace.name)}: ${esc(tab[1])}`;
      main.insertAdjacentHTML('afterbegin', `<h2 class="wf-sr">${heading}</h2>${row}`);
      // As Umbraco's tab group does, the tabs that do not fit go behind a "…" at the row's end, never the open one.
      const nav = main.querySelector('.wf-tabs--place');
      if (nav && head.offsetParent && nav.scrollWidth > nav.clientWidth + 1) {
        nav.insertAdjacentHTML('beforeend', `<a class="wf-tab" href="#" aria-label="More tabs" data-wf-more>${icon('more', 'wf-i--20')}</a>`);
        for (const each of [...nav.querySelectorAll('.wf-tab:not([data-wf-more])')].reverse()) {
          if (nav.scrollWidth <= nav.clientWidth + 1) break;
          if (each.getAttribute('aria-current') !== 'page') each.style.display = 'none';
        }
      }
    }
  }

  // Umbraco folds a workspace's view tabs into "…" when the header runs out of room: the last views first, never
  // the open one. If that isn't enough, header links ("Used on 3 pages") move into the header's own "…" menu. A
  // header that already has its own "…" takes the views into it, so it never shows two (IA 5.1).
  for (const head of document.querySelectorAll('.wf-head')) {
    const main = head.querySelector('.wf-head__main');
    if (!main || !head.offsetParent) continue;
    const fits = () => main.scrollWidth <= main.clientWidth + 1;
    if (fits()) continue;
    const tabs = [...head.querySelectorAll('.wf-tabs')].find((n) => n.offsetParent);
    if (tabs) {
      if (!main.querySelector('[data-wf-more]')) {
        tabs.insertAdjacentHTML('beforeend', `<a class="wf-tab" href="#" aria-label="More views" data-wf-more>${icon('more', 'wf-i--20')}</a>`);
      }
      for (const tab of [...tabs.querySelectorAll('.wf-tab:not([data-wf-more])')].reverse()) {
        if (fits()) break;
        if (tab.getAttribute('aria-current') !== 'page') tab.style.display = 'none';
      }
    }
    for (const link of [...main.querySelectorAll(':scope > a')].reverse()) {
      if (fits()) break;
      link.style.display = 'none';
    }
  }
  // The kit draws checkboxes, switches and radios as spans; give them the place in the tab order that UUI's own
  // controls have. Radios take one stop per group: the chosen one, or the first.
  const drawn = (el) => !el.matches('input, button, select, textarea, a[href], [tabindex], [aria-disabled="true"]');
  for (const el of document.querySelectorAll('[role="checkbox"], [role="switch"]')) if (drawn(el)) el.tabIndex = 0;
  const groups = new Map();
  for (const radio of document.querySelectorAll('[role="radio"]')) {
    if (!drawn(radio)) continue;
    const group = radio.closest('[role="radiogroup"]') || radio.parentElement;
    if (!groups.has(group)) groups.set(group, []);
    groups.get(group).push(radio);
  }
  for (const radios of groups.values()) {
    const chosen = radios.find((r) => r.getAttribute('aria-checked') === 'true') || radios[0];
    for (const r of radios) r.tabIndex = r === chosen ? 0 : -1;
  }

  document.documentElement.dataset.wfReady = 'true';
})();
