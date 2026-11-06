// The action editors' values (ROADMAP 10.2b, ADR-0189): the fields a picker of each kind offers, the addresses a box
// reads, a list with addresses added once each, and the recipient map as the setting holds it, its routes in the
// options' order.
import { describe, expect, it } from 'vitest';
import {
  fieldsOfKind,
  readAddresses,
  readMap,
  unoffered,
  withAddresses,
  withRoute,
  type PickField,
} from './action-editors';

const field = (key: string, more: Partial<PickField> = {}): PickField => ({
  key,
  label: key,
  email: false,
  choice: false,
  file: false,
  sensitive: false,
  top: true,
  options: [],
  ...more,
});

describe('the action editors', () => {
  it("offers a picker's kind of field: an email's and a choice's answer only where a field has one", () => {
    const fields = [
      field('mail', { email: true }),
      field('child', { email: true, top: false }),
      field('topic', { choice: true }),
      field('rows', { choice: true, top: false }),
      field('cv', { file: true, top: false }),
      field('secret', { sensitive: true }),
      field('name'),
    ];
    const keys = (picks: string | null) => fieldsOfKind(fields, picks).map((one) => one.key);
    expect(keys('email')).toEqual(['mail']);
    expect(keys('choice')).toEqual(['topic']);
    expect(keys('file')).toEqual(['cv']);
    expect(keys('sensitive')).toEqual(['secret']);
    expect(keys('any')).toEqual(fields.map((one) => one.key));
    expect(keys(null)).toEqual(fields.map((one) => one.key));
  });

  it('names a picked field it does not offer by what it is not, and says one that is gone', () => {
    const fields = [field('name'), field('child', { email: true, top: false }), field('mail', { email: true })];
    const words = (key: string, ...args: unknown[]) => `${key}(${args.join(',')})`;
    expect(unoffered({ fields, words }, 'name', 'email')).toBe('pickWrong_email(name)');
    expect(unoffered({ fields, words }, 'child', 'email')).toBe('pickNested(child)');
    expect(unoffered({ fields, words }, 'name', 'sensitive')).toBe('pickWrong_sensitive(name)');
    expect(unoffered({ fields, words }, 'gone', 'email')).toBe('pickGone()');
    expect(unoffered({ fields, words }, 'name', 'any')).toBe('name');
  });

  it('reads the addresses typed apart by commas, semicolons or spaces, and names the first that is none', () => {
    expect(readAddresses(' a@example.com, b@example.com;c@example.com  d@example.com ')).toEqual({
      addresses: ['a@example.com', 'b@example.com', 'c@example.com', 'd@example.com'],
      wrong: null,
    });
    expect(readAddresses('a@example.com, sales, b@')).toEqual({
      addresses: ['a@example.com', 'sales', 'b@'],
      wrong: 'sales',
    });
    expect(readAddresses(' , ')).toEqual({ addresses: [], wrong: null });
  });

  it('adds addresses after the others, each once whatever its letter case', () => {
    expect(withAddresses(['Sales@example.com'], ['sales@EXAMPLE.com', 'lead@example.com', 'lead@example.com'])).toEqual(
      ['Sales@example.com', 'lead@example.com'],
    );
  });

  it('reads a recipient map as the setting holds it, and nothing that is no map', () => {
    expect(
      readMap({ field: 'topic', routes: [{ value: 'sales', to: ['s@example.com', 3] }, { to: [] }, 'x'] }),
    ).toEqual({
      field: 'topic',
      routes: [{ value: 'sales', to: ['s@example.com'] }],
    });
    expect(readMap({ field: 'topic' })).toEqual({ field: 'topic', routes: [] });
    for (const none of [undefined, null, 'topic', [], { routes: [] }, { field: 3 }, { field: null }])
      expect(readMap(none)).toBeNull();
  });

  it("puts an option's addresses in its route, in the options' order, and drops a route without addresses", () => {
    const order = ['sales', 'press', 'jobs'];
    let map = withRoute({ field: 'topic', routes: [] }, 'jobs', ['j@example.com'], order);
    map = withRoute(map, 'sales', ['s@example.com'], order);
    map = withRoute(
      { ...map, routes: [...map.routes, { value: 'gone', to: ['g@example.com'] }] },
      'press',
      ['p@example.com'],
      order,
    );
    expect(map.routes.map((route) => route.value)).toEqual(['sales', 'press', 'jobs', 'gone']);
    map = withRoute(map, 'press', [], order);
    expect(map).toEqual({
      field: 'topic',
      routes: [
        { value: 'sales', to: ['s@example.com'] },
        { value: 'jobs', to: ['j@example.com'] },
        { value: 'gone', to: ['g@example.com'] },
      ],
    });
  });
});
