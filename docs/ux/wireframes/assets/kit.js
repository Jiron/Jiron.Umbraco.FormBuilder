/*
 * The wireframe kit's frame (step 2.2; design documents only, never shipped). Each page holds only its workspace
 * and overlays; this script adds Umbraco's top bar and the Forms section's sidebar for the role the page shows,
 * draws the icons, and switches states.
 *
 * On <body>:
 *   data-role       designer | handler | viewer | admin | privacy | none   (the personas of ADR-0013)
 *   data-active     the sidebar item that is open: an item id (inbox, spam, option-lists, ...), form:<id>,
 *                   folder:<id> or bin
 *   data-role-<state>, data-active-<state>   the same, for one state only
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

  // Roles: which sidebar groups and items each persona sees (docs/ux/information-architecture.md, section 11).
  const ROLES = {
    designer: { name: 'Lea Hoffmann', create: true, entries: false, groups: ['library'] },
    handler: { name: 'Jonas Berger', create: false, entries: true, groups: ['entries'] },
    viewer: { name: 'Paul Okafor', create: false, entries: true, groups: ['entries'] },
    admin: { name: 'Ana Novak', create: true, entries: true, groups: ['entries', 'library', 'settings', 'privacy', 'activity'], sections: 'all' },
    privacy: { name: 'Mira Sandberg', create: false, entries: true, groups: ['entries', 'privacy-officer'] },
    none: { name: 'Sam Weller', create: false, entries: false, groups: [], noAccess: true },
  };
  const GROUPS = {
    entries: ['Entries', [['inbox', 'inbox', 'Inbox', 18], ['spam', 'spam', 'Spam']]],
    library: ['Library', [['option-lists', 'list', 'Option lists'], ['themes', 'palette', 'Themes'], ['email-layouts', 'mail', 'Email layouts'], ['templates', 'layers', 'Templates']]],
    settings: ['Settings', [['general', 'settings', 'General'], ['email', 'mail', 'Email'], ['spam-protection', 'shield', 'Spam protection'], ['files', 'paperclip', 'Files'], ['access', 'lock', 'Access'], ['integrations', 'plug', 'Integrations']]],
    privacy: ['Privacy', [['people', 'users', 'People'], ['retention', 'timer', 'Retention'], ['data-collection', 'eye', 'Data collection']]],
    'privacy-officer': ['Privacy', [['people', 'users', 'People'], ['retention', 'timer', 'Retention']]],
    activity: ['Activity', [['deliveries', 'send', 'Deliveries'], ['audit-log', 'activity', 'Audit log']]],
  };
  // The forms tree every page shares: folders first, then forms, each by name; the Recycle Bin last.
  const TREE = [
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
    if (node.unseen && r.entries) marks.push('<span class="wf-dot"></span><span class="wf-sr">unseen entries</span>');
    const muted = node.unpublished ? ' wf-item--muted' : '';
    const sr = node.unpublished ? '<span class="wf-sr"> (not published)</span>' : '';
    let html = `<a class="wf-item wf-item--tree${muted}" style="--depth:${depth}" href="#"${current ? ' aria-current="page"' : ''}>${chevron}${icon(node.type === 'folder' ? 'folder' : 'form')}<span>${esc(node.name)}${sr}</span>${marks.length ? `<span class="wf-item__end">${marks.join('')}</span>` : ''}</a>`;
    if (open) html += node.children.map((c) => treeItem(c, depth + 1)).join('');
    return html;
  }

  function sidebar() {
    const head = `<div class="wf-side__head"><span>Forms</span>${r.noAccess ? '' : `<span class="wf-side__head-actions"><button class="wf-btn wf-btn--icon wf-btn--small wf-btn--text" aria-label="More actions for Forms">${icon('more')}</button>${r.create ? `<button class="wf-btn wf-btn--icon wf-btn--small wf-btn--text" aria-label="Create in Forms">${icon('plus')}</button>` : ''}</span>`}</div>`;
    if (r.noAccess) {
      return `${head}<p class="wf-muted" style="padding: 0 var(--wf-space-6)">You can't see any forms yet. An administrator can give your user group access in Settings › Access.</p>`;
    }
    const tree = TREE.map((n) => treeItem(n, 0)).join('')
      + `<a class="wf-item wf-item--tree" style="--depth:0" href="#"${active === 'bin' ? ' aria-current="page"' : ''}>${icon('chevron-right', 'wf-item__chevron')}${icon('trash')}<span>Recycle Bin</span></a>`;
    const groups = r.groups.map((g) => {
      const [title, items] = GROUPS[g];
      const links = items.map(([id, ic, label, badge]) => {
        const b = badge && r.entries ? `<span class="wf-item__end"><span class="wf-badge">${badge}</span><span class="wf-sr">unseen</span></span>` : '';
        return `<a class="wf-item" href="#"${active === id ? ' aria-current="page"' : ''}>${icon(ic)}<span>${label}</span>${b}</a>`;
      }).join('');
      return `<div class="wf-side__group">${title}</div>${links}`;
    }).join('');
    return `${head}<nav aria-label="Forms">${tree}${groups}</nav>`;
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

  // Umbraco folds a workspace's view tabs into "…" when the header runs out of room: the last views first, never
  // the open one. If that isn't enough, header links ("Used on 3 pages") move into the header's own "…" menu.
  for (const head of document.querySelectorAll('.wf-head')) {
    const main = head.querySelector('.wf-head__main');
    if (!main || !head.offsetParent) continue;
    const fits = () => main.scrollWidth <= main.clientWidth + 1;
    if (fits()) continue;
    const tabs = [...head.querySelectorAll('.wf-tabs')].find((n) => n.offsetParent);
    if (tabs) {
      tabs.insertAdjacentHTML('beforeend', `<a class="wf-tab" href="#" aria-label="More views" data-wf-more>${icon('more', 'wf-i--20')}</a>`);
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
  document.documentElement.dataset.wfReady = 'true';
})();
