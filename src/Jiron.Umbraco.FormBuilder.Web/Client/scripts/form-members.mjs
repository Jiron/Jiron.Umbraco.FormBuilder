// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The website code's own lint rule against reading a form's members plainly. A browser answers a read on a form with
// the form's control of that name in front of every built-in member, and a field's control is named by its alias,
// which an editor chooses: on a form with a field called `dataset`, `form.dataset` is that field's control. So the
// website's script reads and writes every member of a form through `proto(form)` (@jfb/logic/proto), which asks
// the prototype chain. The rule reads the types: a member of an expression whose type is or includes HTMLFormElement,
// read, written, called, taken apart or asked for with `in`, is an error. What the types call an Element or a
// ParentNode but holds a form at run time (a form root's ancestor walk, a `closest('.jfb-form')`, an event's target)
// it cannot see.

const MESSAGE =
  'A form answers a read with its control of that name, and a control is named by its field alias: ' +
  "read and write the form's members through proto(form) from @jfb/logic/proto.";

/**
 * Whether a type is, includes or extends HTMLFormElement.
 * @param {import('typescript').TypeChecker} checker
 * @param {import('typescript').Type} type
 * @param {Set<import('typescript').Type>} seen
 * @returns {boolean}
 */
function isForm(checker, type, seen = new Set()) {
  if (seen.has(type)) return false;
  seen.add(type);
  if (type.isUnion() || type.isIntersection()) return type.types.some((each) => isForm(checker, each, seen));
  // A type parameter counts by its constraint.
  const constraint = type.isTypeParameter() ? checker.getBaseConstraintOfType(type) : undefined;
  if (constraint !== undefined) return isForm(checker, constraint, seen);
  if (type.getSymbol()?.getName() === 'HTMLFormElement') return true;
  return type.isClassOrInterface() && checker.getBaseTypes(type).some((base) => isForm(checker, base, seen));
}

/** @type {import('eslint').Rule.RuleModule} */
export const formMembers = {
  meta: {
    type: 'problem',
    docs: { description: "The website code reads a form's members through proto(form), never plainly." },
    messages: { plain: MESSAGE },
    schema: [],
  },
  create(context) {
    const services = context.sourceCode.parserServices;
    if (services?.program == null) throw new Error('jfb/form-members reads types: give these files a project.');
    const checker = services.program.getTypeChecker();
    /**
     * Whether the expression may hold a form. Each branch of `??`, `||`, `&&` and `?:` counts on its own, as the
     * type of `row ?? form` is Element alone once HTMLFormElement is folded into it.
     * @param {import('estree').Node} node
     * @returns {boolean}
     */
    const holdsForm = (node) => {
      if (node.type === 'LogicalExpression') return holdsForm(node.left) || holdsForm(node.right);
      if (node.type === 'ConditionalExpression') return holdsForm(node.consequent) || holdsForm(node.alternate);
      return isForm(checker, checker.getTypeAtLocation(services.esTreeNodeToTSNodeMap.get(node)));
    };
    /** @param {import('estree').Node} node */
    const throughView = (node) =>
      node.type === 'CallExpression' && node.callee.type === 'Identifier' && node.callee.name === 'proto';
    return {
      MemberExpression(node) {
        if (node.object.type === 'Super' || throughView(node.object)) return;
        if (holdsForm(node.object)) context.report({ node, messageId: 'plain' });
      },
      /** @param {import('estree').BinaryExpression} node */
      'BinaryExpression[operator="in"]'(node) {
        if (!throughView(node.right) && holdsForm(node.right)) context.report({ node, messageId: 'plain' });
      },
      /** @param {import('estree').VariableDeclarator} node */
      'VariableDeclarator[id.type="ObjectPattern"]'(node) {
        if (node.init !== null && node.init !== undefined && !throughView(node.init) && holdsForm(node.init))
          context.report({ node, messageId: 'plain' });
      },
      /** @param {import('estree').AssignmentExpression} node */
      'AssignmentExpression[left.type="ObjectPattern"]'(node) {
        if (!throughView(node.right) && holdsForm(node.right)) context.report({ node, messageId: 'plain' });
      },
    };
  },
};

/** The plugin that holds it, as the flat config names it: `jfb/form-members`. */
export default { rules: { 'form-members': formMembers } };
