/*
 * Mock-only behaviour for the website form designs in docs/ux/website (ROADMAP 2.3; never shipped). The real
 * runtime is built in step 6.4. This script only sets up what a picture needs:
 * - ?scheme=dark gives the host site its dark design and the form the jfb-scheme-dark class; ?scheme=dark-site gives
 *   the site its dark design only, as on a site that did not opt in, and the ground check below picks the scheme;
 * - ?dir=rtl lays the page out right to left;
 * - ?state=<name> shows the elements marked data-mock-state="<name> ..." and hides those marked
 *   data-mock-not="<name> ..."; in the "errors" state, each control marked data-mock-invalid="<error id>" gets
 *   aria-invalid and the error in its aria-describedby, as the runtime and the server do;
 * - outside the default state, a control marked data-mock-fill="<value>" holds that answer and one marked
 *   data-mock-check is ticked, so the other states show a form part-way through;
 * - in the "answered" state, a control marked data-mock-answer holds that value (a choice is chosen), and an
 *   element marked data-mock-reveal="<state> ..." is shown and enabled, as a rule would show it;
 * - a control marked data-mock-clear="<state> ..." is emptied or unticked in those states;
 * - a button marked data-mock-busy="<state> ..." is busy in those states (data-jfb-busy and aria-disabled, as the
 *   runtime marks Send while the answers are on their way);
 * - a drop zone marked data-mock-drop="<state> ..." has a file dragged over it (data-jfb-drop) in those states;
 * - the element marked data-mock-focus="<state>" takes the focus in that state;
 * - a canvas marked data-mock-signature shows a sample signature, except in the states its data-mock-blank lists;
 * - steps whose names would break inside a word get data-jfb-compact, as the runtime marks them after layout;
 * then it marks the page ready for scripts/website-mocks.mjs.
 */
(function () {
  var query = new URLSearchParams(location.search);
  var state = query.get('state') || 'default';
  var scheme = query.get('scheme') === 'dark' || query.get('scheme') === 'dark-site' ? 'dark' : 'light';
  var optIn = query.get('scheme') !== 'dark-site';
  var dir = query.get('dir') === 'rtl' ? 'rtl' : null;
  var root = document.documentElement;
  var each = function (selector, fn) { Array.prototype.forEach.call(document.querySelectorAll(selector), fn); };
  var words = function (value) { return (value || '').split(/\s+/).filter(Boolean); };

  root.dataset.site = scheme;
  root.dataset.state = state;
  if (dir) root.dir = dir;
  each('.jfb-form', function (form) {
    if (optIn) form.classList.toggle('jfb-scheme-dark', scheme === 'dark');
    if (dir) form.dir = dir;
  });
  each('[data-mock-state]', function (el) { el.hidden = words(el.dataset.mockState).indexOf(state) < 0; });
  each('[data-mock-not]', function (el) { if (words(el.dataset.mockNot).indexOf(state) >= 0) el.hidden = true; });
  if (state !== 'default') {
    each('[data-mock-fill]', function (el) { el.value = el.dataset.mockFill; });
    each('[data-mock-check]', function (el) { el.checked = true; });
  }
  each('[data-mock-clear]', function (el) {
    if (words(el.dataset.mockClear).indexOf(state) < 0) return;
    if (el.type === 'checkbox' || el.type === 'radio') el.checked = false;
    else el.value = '';
  });
  each('[data-mock-busy]', function (el) {
    if (words(el.dataset.mockBusy).indexOf(state) < 0) return;
    el.setAttribute('data-jfb-busy', '');
    el.setAttribute('aria-disabled', 'true');
  });
  each('[data-mock-drop]', function (el) {
    if (words(el.dataset.mockDrop).indexOf(state) >= 0) el.setAttribute('data-jfb-drop', '');
  });
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
  each('.jfb-form', groundCheck);
  each('.jfb-progress-steps', foldSteps);
  each('canvas[data-mock-signature]', function (canvas) {
    if (words(canvas.dataset.mockBlank).indexOf(state) < 0) drawSignature(canvas);
  });
  var focus = document.querySelector('[data-mock-focus~="' + state + '"]');
  if (focus) focus.focus();
  root.dataset.mockReady = 'true';

  /*
   * What the runtime does after layout and on every resize: when a word of a step name is wider than its step, the
   * steps fold into "Step 2 of 6", so no name breaks inside a word where the browser cannot hyphenate.
   */
  /*
   * What the runtime does after layout (ADR-0022, amended in 2.4): a form without a scheme class takes the dark
   * tokens when the first opaque background behind it is dark, so its text never sits dark on dark. A class the
   * site set is left alone.
   */
  function groundCheck(form) {
    if (/(^|\s)jfb-scheme-/.test(form.className)) return;
    var rgb = [255, 255, 255];
    for (var el = form.parentElement; el; el = el.parentElement) {
      var m = getComputedStyle(el).backgroundColor.match(/[\d.]+/g);
      if (m && (m.length < 4 || Number(m[3]) === 1)) { rgb = m.slice(0, 3).map(Number); break; }
    }
    var lum = rgb.map(function (v) {
      v /= 255;
      return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    });
    // Below this luminance white text has more contrast than black text, so the ground counts as dark.
    if (0.2126 * lum[0] + 0.7152 * lum[1] + 0.0722 * lum[2] < 0.179) form.classList.add('jfb-scheme-dark');
  }

  function foldSteps(steps) {
    steps.removeAttribute('data-jfb-compact');
    var names = steps.querySelectorAll('ol > li > span');
    if (getComputedStyle(names[0] || steps).position === 'absolute') return; // already folded by width or count
    var probe = document.createElement('span');
    probe.style.cssText = 'position:absolute;visibility:hidden;white-space:nowrap';
    var tooLong = Array.prototype.some.call(names, function (name) {
      name.parentElement.appendChild(probe);
      var room = name.getBoundingClientRect().width;
      var text = Array.prototype.filter.call(name.childNodes, function (n) { return n.nodeType === 3; })
        .map(function (n) { return n.textContent; }).join(' ');
      return text.split(/\s+/).some(function (word) {
        probe.textContent = word;
        return probe.getBoundingClientRect().width > room + 0.5;
      });
    });
    probe.remove();
    if (tooLong) steps.setAttribute('data-jfb-compact', '');
  }

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
