/**
 * An object's members as its prototype chain defines them, never as the object itself answers. A browser answers a
 * read on a form with the form's control of that name in front of every built-in member, and a field's control is
 * named by its alias, which an editor chooses: a form with a field called `id`, `dataset` or `querySelector` hides the
 * form's own `id`, `dataset` or `querySelector` from a plain read. The website's script reads every member of a form
 * through `proto(form)`, which asks the prototype chain with the form as the receiver, so getters and setters run on
 * the form and methods come back bound to it. Whatever can be the form counts as one: a root, an element's parent, an
 * event's target. The view is for reading and writing members only, written where it is read and never held: what
 * goes to the browser's own methods, to a WeakMap or to a comparison is the form itself (ADR-0260).
 */
const views = new WeakMap<object, object>();

/** The view of the target's members through its prototype chain, the same view each time for the same target. */
export function proto<T extends object>(target: T): T {
  let view = views.get(target);
  if (view === undefined) {
    // Asked at each read, not once: a prototype changed later answers as it does for the target itself.
    const chain = (): object => Object.getPrototypeOf(target) as object;
    view = new Proxy(target, {
      get(_, key) {
        const value: unknown = Reflect.get(chain(), key, target);
        // The browser's methods run on the object they belong to, never on the view.
        return typeof value === 'function' ? (value as (...args: unknown[]) => unknown).bind(target) : value;
      },
      set: (_, key, value) => Reflect.set(chain(), key, value, target),
      has: (_, key) => Reflect.has(chain(), key),
    });
    views.set(target, view);
  }
  return view as T;
}
