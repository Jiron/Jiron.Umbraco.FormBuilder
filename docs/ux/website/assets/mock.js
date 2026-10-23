/*
 * Mock-only behaviour for the website form designs in docs/ux/website (ROADMAP 2.3; never shipped). The real
 * runtime is built in step 6.4. This script only sets up what a picture needs:
 * - ?scheme=dark gives the host site its dark design and the form the jfb-scheme-dark class;
 * - ?dir=rtl lays the page out right to left;
 * - ?state=<name> shows the elements marked data-mock-state="<name> ..." and hides those marked
 *   data-mock-not="<name> ..."; in the "errors" state, each control marked data-mock-invalid="<error id>" gets
 *   aria-invalid and the error in its aria-describedby, as the runtime and the server do;
 * - outside the default state, a control marked data-mock-fill="<value>" holds that answer and one marked
 *   data-mock-check is ticked, so the other states show a form part-way through;
 * - in the "answered" state, a control marked data-mock-answer holds that value (a choice is chosen), and an
 *   element marked data-mock-reveal="<state> ..." is shown and enabled, as a rule would show it;
 * - the element marked data-mock-focus="<state>" takes the focus in that state;
 * - a canvas marked data-mock-signature shows a sample signature;
 * then it marks the page ready for scripts/website-mocks.mjs.
 */
(function () {
  var query = new URLSearchParams(location.search);
  var state = query.get('state') || 'default';
  var scheme = query.get('scheme') === 'dark' ? 'dark' : 'light';
  var dir = query.get('dir') === 'rtl' ? 'rtl' : null;
  var root = document.documentElement;
  var each = function (selector, fn) { Array.prototype.forEach.call(document.querySelectorAll(selector), fn); };
  var words = function (value) { return (value || '').split(/\s+/).filter(Boolean); };

  root.dataset.site = scheme;
  root.dataset.state = state;
  if (dir) root.dir = dir;
  each('.jfb-form', function (form) {
    form.classList.toggle('jfb-scheme-dark', scheme === 'dark');
    if (dir) form.dir = dir;
  });
  each('[data-mock-state]', function (el) { el.hidden = words(el.dataset.mockState).indexOf(state) < 0; });
  each('[data-mock-not]', function (el) { if (words(el.dataset.mockNot).indexOf(state) >= 0) el.hidden = true; });
  if (state !== 'default') {
    each('[data-mock-fill]', function (el) { el.value = el.dataset.mockFill; });
    each('[data-mock-check]', function (el) { el.checked = true; });
  }
  if (state === 'answered') {
    each('[data-mock-answer]', function (el) {
      if (el.type === 'checkbox' || el.type === 'radio') el.checked = true;
      else el.value = el.dataset.mockAnswer;
    });
  }
  each('[data-mock-reveal]', function (el) {
    if (words(el.dataset.mockReveal).indexOf(state) < 0) return;
    el.hidden = false;
    el.removeAttribute('data-jfb-hidden');
    Array.prototype.forEach.call(el.querySelectorAll('input, select, textarea'), function (c) { c.disabled = false; });
  });
  if (state === 'errors') {
    each('[data-mock-invalid]', function (el) {
      el.setAttribute('aria-invalid', 'true');
      var described = words(el.getAttribute('aria-describedby'));
      described.push(el.dataset.mockInvalid);
      el.setAttribute('aria-describedby', described.join(' '));
    });
  }
  each('canvas[data-mock-signature]', drawSignature);
  var focus = document.querySelector('[data-mock-focus~="' + state + '"]');
  if (focus) focus.focus();
  root.dataset.mockReady = 'true';

  /* A sample signature in the form's text colour, the way the pad draws one. */
  function drawSignature(canvas) {
    var ctx = canvas.getContext('2d');
    var form = canvas.closest('.jfb-form');
    ctx.strokeStyle = getComputedStyle(form).getPropertyValue('--_ink').trim() || '#1d2433';
    ctx.lineWidth = 4;
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    var strokes = [
      [[96, 196], [118, 92], [140, 182], [168, 96], [186, 196]],
      [[214, 150], [232, 132], [252, 156], [236, 182], [214, 168], [262, 152], [292, 140]],
      [[300, 186], [320, 120], [338, 168], [366, 132], [392, 170], [420, 146]],
      [[452, 160], [486, 120], [512, 168], [474, 186], [458, 150], [530, 132], [566, 156], [598, 128]],
      [[612, 176], [640, 112], [660, 172], [690, 140], [724, 162], [770, 138], [812, 150]],
    ];
    strokes.forEach(function (points) {
      ctx.beginPath();
      ctx.moveTo(points[0][0], points[0][1]);
      for (var i = 1; i < points.length - 1; i++) {
        var midX = (points[i][0] + points[i + 1][0]) / 2;
        var midY = (points[i][1] + points[i + 1][1]) / 2;
        ctx.quadraticCurveTo(points[i][0], points[i][1], midX, midY);
      }
      var last = points[points.length - 1];
      ctx.lineTo(last[0], last[1]);
      ctx.stroke();
    });
  }
})();
