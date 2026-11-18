// Activity > Deliveries and Activity > Audit log (ROADMAP 12.4b, IA 9, ADR-0219): Deliveries lists problems first with
// their state, says once that sending is paused, filters through the server, and its details retry a failed delivery at
// once, ask first for an uncertain one, and offer none for a limited mail, naming its limit; the audit log lists who did
// what where, narrows to one person from an action's details, and exports what it shows.
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import { UmbObjectState } from '@umbraco-cms/backoffice/observable-api';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import type {
  AuditEntryResponseModel,
  AuditListResponseModel,
  DeliveryDetailResponseModel,
  DeliveryListResponseModel,
  DeliveryResponseModel,
} from '../api';
import { all, must, registerEnglish, settle, shadow, testHost } from '../testing';

const api = vi.hoisted(() => ({
  lists: [] as unknown[],
  details: [] as unknown[],
  retries: [] as unknown[],
  queries: [] as unknown[],
  sent: [] as unknown[],
  exports: [] as unknown[],
  saved: [] as string[],
  confirm: true,
  asked: 0,
}));

// What the user holds, which the place's tabs draw from.
vi.mock('../section/my-permissions', () => ({
  myPermissions: async () => ({ global: ['manageSettings'], anywhere: [] }),
  myPermissionsNow: () => undefined,
}));

vi.mock('@umbraco-cms/backoffice/resources', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/resources')>()),
  tryExecute: async (_host: unknown, request: Promise<unknown>) => request,
}));

vi.mock('../api', async (original) => ({
  ...(await original<typeof import('../api')>()),
  deliveryList: async (options: { query: unknown }) => {
    api.queries.push(options.query);
    return api.lists.shift();
  },
  deliveryGetEmail: async () => api.details.shift(),
  deliveryGetRun: async () => api.details.shift(),
  deliveryRetryEmail: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.retries.shift();
  },
  deliveryRetryRun: async (options: { body: unknown }) => {
    api.sent.push(options.body);
    return api.retries.shift();
  },
  auditList: async (options: { query: unknown }) => {
    api.queries.push(options.query);
    return api.lists.shift();
  },
  auditExport: async (options: { query: unknown }) => {
    api.queries.push(options.query);
    return api.exports.shift();
  },
}));

vi.mock('../ui/confirm', () => ({
  confirmText: async () => {
    api.asked++;
    return api.confirm ? undefined : Promise.reject(new Error('cancelled'));
  },
}));

vi.mock('../library/actions/export-form.action', () => ({
  saveBlob: (name: string) => api.saved.push(name),
}));

const { DeliveriesScreenElement } = await import('./deliveries.screen');
const { DeliveryModalElement } = await import('./delivery.modal.element');
const { AuditScreenElement } = await import('./audit.screen');
const { AuditEntryModalElement } = await import('./audit-entry.modal.element');

const said = (root: ParentNode): string => (root.textContent ?? '').replace(/\s+/g, ' ').trim();

function delivery(more: Partial<DeliveryResponseModel>): DeliveryResponseModel {
  return {
    kind: 'email',
    id: 1,
    state: 'failed',
    emailKind: 'action',
    recipients: ['team@example.com'],
    recipientsHidden: false,
    actionType: 'email',
    actionName: 'Email the team',
    typeName: null,
    target: null,
    formKey: 'f1',
    formName: 'Contact us',
    entryKey: 'e1',
    entryNumber: 7,
    attempts: 5,
    nextAttemptUtc: null,
    errorCode: 'exception',
    errorMessage: null,
    createdUtc: new Date().toISOString(),
    firstAttemptUtc: null,
    lastAttemptUtc: null,
    finishedUtc: null,
    ...more,
  };
}

const list = (items: DeliveryResponseModel[], paused = false): DeliveryListResponseModel => ({
  items,
  total: items.length,
  paused,
});

const detail = (
  more: Partial<DeliveryResponseModel>,
  rest: Partial<DeliveryDetailResponseModel> = {},
): DeliveryDetailResponseModel => ({
  delivery: delivery(more),
  mayRetry: true,
  retryable: true,
  limit: null,
  ...rest,
});

/** A modal context as Umbraco's holds the value: the sidebar writes it, then submits. */
function modalContext(submit: () => void) {
  const state = new UmbObjectState<object>({});
  return {
    value: state.asObservable(),
    setValue: (next: object) => state.setValue(next),
    updateValue: (partial: object) => state.update(partial),
    getValue: () => state.getValue(),
    submit,
    reject: vi.fn(),
  };
}

async function mount<T extends HTMLElement>(element: T, ready: string) {
  const host = testHost();
  const peek = vi.fn();
  host.provideContext(UMB_NOTIFICATION_CONTEXT, { peek, getHostElement: () => host } as never);
  host.append(element);
  await vi.waitFor(() => expect(shadow(element).querySelector(ready)).not.toBeNull());
  await settle(element as unknown as { updateComplete: Promise<unknown> });
  const root = shadow(element);
  const button = (label: string) => all(element, 'uui-button').find((one) => one.getAttribute('label') === label);
  return { root, peek, button };
}

beforeAll(registerEnglish);

afterEach(() => {
  document.body.replaceChildren();
  Object.assign(api, {
    lists: [],
    details: [],
    retries: [],
    queries: [],
    sent: [],
    exports: [],
    saved: [],
    confirm: true,
    asked: 0,
  });
});

describe('Activity > Deliveries', () => {
  it('lists each delivery with its state, whom it went to, what it is and its form, the last 30 days at first', async () => {
    api.lists = [
      {
        data: list([
          delivery({ id: 1, recipients: ['team@example.com', 'hr@example.com'] }),
          delivery({
            kind: 'run',
            id: 2,
            state: 'uncertain',
            recipients: [],
            actionType: 'webhook',
            actionName: 'Send to the CRM',
            target: { kind: 'host', text: 'crm.example.com' },
          }),
          delivery({ id: 3, state: 'delivered', emailKind: 'test', actionName: null, formName: null, formKey: null }),
        ]),
      },
    ];
    const { root } = await mount(new DeliveriesScreenElement(), 'table');
    expect(api.queries[0]).toEqual({ days: 30, skip: 0, take: 50 });
    const rows = [...root.querySelectorAll<HTMLTableRowElement>('tbody tr')].map((row) =>
      [...row.cells].map((cell) => said(cell)),
    );
    expect(rows[0]?.slice(0, 4)).toEqual(['Failed', 'team@example.com and 1 more', 'Email the team', 'Contact us']);
    expect(rows[1]?.slice(0, 4)).toEqual(['Uncertain', 'crm.example.com', 'Send to the CRM', 'Contact us']);
    expect(rows[2]?.slice(0, 3)).toEqual(['Delivered', 'team@example.com', 'A test email']);
    expect(root.querySelector('.paused')).toBeNull();
  });

  it('puts what each delivery is under whom it went to on a narrow screen, so a long address leaves it its line', async () => {
    const before = globalThis.ResizeObserver;
    globalThis.ResizeObserver = class {
      constructor(private readonly report: ResizeObserverCallback) {}
      observe = (target: Element) => {
        if (target.localName === 'jfb-deliveries-screen')
          this.report([{ contentRect: { width: 620 } } as ResizeObserverEntry], this as never);
      };
      unobserve = () => undefined;
      disconnect = () => undefined;
    } as never;
    try {
      api.lists = [{ data: list([delivery({ id: 1, recipients: ['team@example.com'] })]) }];
      const { root } = await mount(new DeliveriesScreenElement(), 'table');
      // At 620 px, as Umbraco's workspace is on a phone and at 768 px, what it is has no column of its own.
      expect([...root.querySelectorAll('thead th')].map((cell) => said(cell))).toEqual(['State', 'To', 'Form', 'When']);
      const cells = [...must(root.querySelector<HTMLTableRowElement>('tbody tr'), 'the row').cells];
      expect(cells.map((cell) => said(cell)).slice(0, 2)).toEqual(['Failed', 'team@example.com Email the team']);
      expect(said(must(cells[1]?.querySelector('.what-below'), 'what it is'))).toBe('Email the team');
      expect(root.querySelector('td.what')).toBeNull();
    } finally {
      globalThis.ResizeObserver = before;
    }
  });

  it('says once that sending is paused, with the key that pauses it', async () => {
    api.lists = [{ data: list([delivery({})], true) }];
    const { root } = await mount(new DeliveriesScreenElement(), 'table');
    const paused = [...root.querySelectorAll<HTMLElement>('.paused')];
    expect(paused).toHaveLength(1);
    expect(said(must(paused[0], 'paused'))).toContain(
      'Sending is paused by the host setting JironFormBuilder:Actions:Paused.',
    );
    expect(paused[0]?.querySelector('code')?.textContent).toBe('JironFormBuilder:Actions:Paused');
  });

  it('asks the server again for a filter, and says when nothing matches', async () => {
    api.lists = [{ data: list([delivery({})]) }, { data: list([]) }];
    const { root } = await mount(new DeliveriesScreenElement(), 'table');
    const states = must(root.querySelector<HTMLSelectElement>('#states'), 'states');
    states.value = 'problems';
    states.dispatchEvent(new Event('change'));
    await vi.waitFor(() => expect(said(root)).toContain('No deliveries match the search and the filters.'));
    expect(api.queries[1]).toEqual({ states: 'problems', days: 30, skip: 0, take: 50 });
  });
});

describe("a delivery's details", () => {
  async function open(data: unknown, kind: 'email' | 'run' = 'email') {
    api.details = [{ data }];
    const modal = new DeliveryModalElement();
    modal.data = { kind, id: 1 };
    const done = vi.fn();
    modal.modalContext = modalContext(done) as never;
    const mounted = await mount(modal, '.body');
    return { ...mounted, modal, done };
  }

  it("retries a failed one at once and closes, saying it's queued", async () => {
    const { root, button, peek } = await open(detail({}));
    expect(
      [...root.querySelectorAll('dt')].map((term) => said(term)),
      'one recipient, in the headline',
    ).toEqual(['Action', 'Entry', 'Queued']);
    api.retries = [{ data: detail({ state: 'waiting' }) }];
    must(button('Retry'), 'Retry').click();
    await vi.waitFor(() => expect(api.sent).toEqual([{ confirmed: false }]));
    expect(api.asked).toBe(0);
    await vi.waitFor(() =>
      expect(peek).toHaveBeenCalledWith('positive', { data: { message: 'The delivery is queued again.' } }),
    );
  });

  it('asks before retrying an uncertain one, as it may deliver twice, and sends nothing when cancelled', async () => {
    const { button } = await open(detail({ state: 'uncertain', errorCode: 'lease_expired' }));
    api.confirm = false;
    must(button('Retry\u2026'), 'Retry...').click();
    await vi.waitFor(() => expect(api.asked).toBe(1));
    await settle();
    expect(api.sent).toEqual([]);
    api.confirm = true;
    api.retries = [{ data: detail({ state: 'waiting' }) }];
    must(button('Retry\u2026'), 'Retry...').click();
    await vi.waitFor(() => expect(api.sent).toEqual([{ confirmed: true }]));
  });

  it('offers no retry for a limited mail and names the limit with its key', async () => {
    const { root, button } = await open(
      detail(
        { state: 'limited', recipients: ['anna@example.org'], errorCode: 'recipient_limit' },
        {
          retryable: false,
          mayRetry: false,
          limit: { kind: 'recipient', count: 3, key: 'JironFormBuilder:Limits:Mail:PerRecipientPerDay' },
        },
      ),
    );
    expect(button('Retry')).toBeUndefined();
    expect(said(root)).toContain('3 emails to anna@example.org that day');
    expect(root.querySelector('code')?.textContent).toBe('JironFormBuilder:Limits:Mail:PerRecipientPerDay');
    expect([...root.querySelectorAll('jfb-learn-more')].map((link) => link.getAttribute('topic'))).toEqual([
      'mailLimits',
    ]);
  });

  it('says who can retry when the reader may not', async () => {
    const { root, button } = await open(detail({}, { mayRetry: false }));
    expect(button('Retry')).toBeUndefined();
    expect(said(root)).toContain('Only someone who may manage the actions of this form can retry it.');
  });
});

const audit = (items: AuditEntryResponseModel[]): AuditListResponseModel => ({
  items,
  total: items.length,
  names: { f1: { kind: 'form', name: 'Job application' }, g1: { kind: 'userGroup', name: 'HR team' } },
});

function entry(more: Partial<AuditEntryResponseModel>): AuditEntryResponseModel {
  return {
    id: 1,
    occurredUtc: new Date().toISOString(),
    userKey: 'u1',
    userName: 'Ana Novak',
    action: 'form.published',
    entityType: 'form',
    entityKey: 'f1',
    details: { version: 3 },
    ...more,
  };
}

describe('Activity > Audit log', () => {
  it('lists who did what where, the last 30 days at first', async () => {
    api.lists = [
      {
        data: audit([
          entry({}),
          entry({ id: 2, userKey: null, userName: null, action: 'form.draftsPruned', details: { versions: [1, 2] } }),
        ]),
      },
    ];
    const { root } = await mount(new AuditScreenElement(), 'table');
    expect(api.queries[0]).toEqual({ days: 30, skip: 0, take: 50 });
    const rows = [...root.querySelectorAll<HTMLTableRowElement>('tbody tr')].map((row) =>
      [...row.cells].slice(1).map((cell) => said(cell)),
    );
    expect(rows).toEqual([
      ['Ana Novak', 'Published version 3', 'Job application'],
      ['Jiron Form Builder', 'Cleared 2 old draft saves', 'Job application'],
    ]);
  });

  it('exports what it shows', async () => {
    api.lists = [{ data: audit([entry({})]) }];
    const { button } = await mount(new AuditScreenElement(), 'table');
    api.exports = [{ data: new Blob(['x']) }];
    must(button('Export'), 'Export').click();
    await vi.waitFor(() => expect(api.saved).toHaveLength(1));
    expect(api.saved[0]).toMatch(/^audit-log-\d{4}-\d{2}-\d{2}\.csv$/);
    expect(api.queries[1]).toEqual({ days: 30 });
  });
});

describe("an action's details", () => {
  async function open(data: AuditEntryResponseModel) {
    const modal = new AuditEntryModalElement();
    modal.data = { entry: data, names: audit([]).names };
    const done = vi.fn();
    modal.modalContext = modalContext(done) as never;
    const mounted = await mount(modal, '.body');
    return { ...mounted, modal, done };
  }

  it("shows a grant's permissions before and after", async () => {
    const { root } = await open(
      entry({
        action: 'access.grantChanged',
        entityType: 'access',
        entityKey: null,
        details: { group: 'g1', before: ['viewEntries'], after: ['viewEntries', 'export'] },
      }),
    );
    const rows = [...root.querySelectorAll<HTMLTableRowElement>('tbody tr')].map((row) =>
      [...row.cells].map((cell) => said(cell)),
    );
    expect(rows).toEqual([
      ['View entries', 'Granted', 'Granted'],
      ['Export entries', 'Not granted', 'Granted'],
    ]);
  });

  it("narrows the log to the person who did it, and links a form's publish to its History", async () => {
    const { root, button, modal, done } = await open(entry({}));
    expect(root.querySelector('a')?.getAttribute('href')).toContain('/edit/f1/view/history');
    expect(
      [...root.querySelectorAll('dt')].map((term) => said(term)),
      'the version once, in the sentence',
    ).toEqual(['Who', 'When', 'Where']);
    must(button("Show only Ana Novak's actions"), 'Show only').click();
    await settle();
    expect(done).toHaveBeenCalled();
    expect(modal.value).toEqual({ user: { key: 'u1', name: 'Ana Novak' } });
  });
});
