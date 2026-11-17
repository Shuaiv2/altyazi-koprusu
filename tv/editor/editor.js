// SPDX-License-Identifier: AGPL-3.0-only
// Mac altyazı düzenleyicisi: TV ekranını 1920x1080 sayfa pikselinde çizer ve yerleşimi TV'dekiyle aynı
// GT_SUBLAYOUT.apply işlevinden geçirir. Swift köprüsü: docs/uzaktan-duzen-protokolu.md
(function () {
  'use strict';
  var SUB = window.GT_SUBSETTINGS, LAY = window.GT_SUBLAYOUT;
  var VW = 1920, VH = 1080;
  var SAMPLE = 'Örnek altyazı: Çağla, ışığı gördüğünde şaşırdı.\nİstanbul\'da güneş doğarken öğle yemeğini düşündü. İĞÜŞÖÇ ığüşöç';
  var $ = function (id) { return document.getElementById(id); };

  var s = SUB.defaults(VW, VH);
  var loaded = null;          // TV'nin son bildirdiği ayar (Vazgeç buna döner)
  var contentHeight = 0;
  var scale = 1;
  var connected = false, lastReport = '';
  var lastSent = 0, sendTimer = null;

  // --- Köprü ---
  function post(type, settings) {
    var msg = { type: type };
    if (settings) msg.settings = settings;
    var h = window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.altyazi;
    if (h) { h.postMessage(msg); return; }
    var d = $('debug');
    d.hidden = false;
    d.textContent = JSON.stringify(msg);
  }

  function outgoing() {
    var o = SUB.sanitize(s, VW, VH);
    o.positionSource = 'tv';
    return o;
  }

  function sendPreview() {
    if (!$('live').checked) return;
    var wait = 100 - (Date.now() - lastSent);
    if (wait <= 0) { lastSent = Date.now(); post('preview', outgoing()); return; }
    if (sendTimer) return;
    sendTimer = setTimeout(function () { sendTimer = null; lastSent = Date.now(); post('preview', outgoing()); }, wait);
  }

  // --- Çizim ---
  function fit() {
    scale = $('stage-wrap').clientWidth / VW;
    $('stage').style.transform = 'scale(' + scale + ')';
  }

  function render() {
    var text = $('subtitle-text');
    text.textContent = $('sample').value;
    var r = LAY.apply($('subtitle'), text, s, VW, VH, {});
    contentHeight = r.contentHeight;
    $('readout').textContent = 'X ' + s.x + ' · Y ' + s.y + ' (sayfa pikseli)';
  }

  function syncControls(skip) {
    function set(id, v) { var el = $(id); if (el !== skip) el.value = v; }
    set('x', s.x); set('y', s.y);
    ['fontSize', 'width', 'margin', 'bgOpacity'].forEach(function (k) {
      set(k, s[k]);
      $(k + '-out').textContent = s[k] + (k === 'bgOpacity' ? ' %' : ' px');
    });
    set('textColor', s.textColor); set('bgColor', s.bgColor);
    set('outline', s.outline); set('align', s.align);
    $('bold').checked = s.bold;
    markSwatches('textColor'); markSwatches('bgColor');
    $('preset').value = presetIndex();
  }

  function presetIndex() {
    var list = SUB.presets(VW, VH, s);
    for (var i = 0; i < list.length; i++) if (list[i].x === s.x && list[i].y === s.y && list[i].align === s.align) return String(i);
    return '';
  }

  function markSwatches(key) {
    var nodes = $(key + '-sw').children;
    for (var i = 0; i < nodes.length; i++) nodes[i].classList.toggle('on', nodes[i].dataset.value.toUpperCase() === s[key]);
  }

  // Değişiklik: sanitize, konumu ekrana sığacak aralığa çek, çiz, TV'ye gönder.
  function update(patch, skip, noSend) {
    for (var k in patch) s[k] = patch[k];
    s = SUB.sanitize(s, VW, VH);
    render();
    var cp = SUB.clampPosition(s, VW, VH, contentHeight);
    if (cp.x !== s.x || cp.y !== s.y) { s.x = cp.x; s.y = cp.y; render(); }
    syncControls(skip);
    if (!noSend) sendPreview();
  }

  // --- Denetimler ---
  function build() {
    var R = SUB.RANGES;
    ['fontSize', 'width', 'margin', 'bgOpacity'].forEach(function (k) {
      var el = $(k);
      el.min = R[k].min; el.max = R[k].max; el.step = R[k].step === undefined ? 1 : (k === 'width' ? 10 : R[k].step);
      el.addEventListener('input', function () { var p = {}; p[k] = Number(el.value); update(p, el); });
    });
    ['x', 'y'].forEach(function (k) {
      var el = $(k);
      el.addEventListener('input', function () { if (el.value === '') return; var p = {}; p[k] = Number(el.value); update(p, el); });
      el.addEventListener('change', function () { syncControls(null); });
    });
    var list = SUB.presets(VW, VH, s), sel = $('preset');
    sel.appendChild(new Option('Özel', ''));
    list.forEach(function (p, i) { sel.appendChild(new Option(p.name, String(i))); });
    sel.addEventListener('change', function () {
      if (sel.value === '') return;
      var p = SUB.presets(VW, VH, s)[Number(sel.value)];
      update({ x: p.x, y: p.y, align: p.align });
    });
    [['textColor', SUB.TEXT_COLORS], ['bgColor', SUB.BG_COLORS]].forEach(function (pair) {
      var key = pair[0], box = $(key + '-sw');
      pair[1].forEach(function (c) {
        var b = document.createElement('button');
        b.type = 'button'; b.className = 'swatch'; b.title = c.name; b.dataset.value = c.value; b.style.background = c.value;
        b.addEventListener('click', function () { var p = {}; p[key] = c.value; update(p); });
        box.appendChild(b);
      });
      $(key).addEventListener('input', function () { var p = {}; p[key] = $(key).value; update(p); });
    });
    $('outline').addEventListener('change', function () { update({ outline: $('outline').value }); });
    $('align').addEventListener('change', function () { update({ align: $('align').value }); });
    $('bold').addEventListener('change', function () { update({ bold: $('bold').checked }); });
    $('sample').addEventListener('input', function () { render(); var cp = SUB.clampPosition(s, VW, VH, contentHeight); if (cp.y !== s.y) update({ y: cp.y }); else sendPreview(); });
    $('live').addEventListener('change', function () {
      if ($('live').checked) { lastSent = 0; sendPreview(); } else post('cancel');
    });
    $('save').addEventListener('click', function () { post('save', outgoing()); });
    $('cancel').addEventListener('click', function () {
      post('cancel');
      s = SUB.sanitize(loaded || SUB.defaults(VW, VH), VW, VH);
      update({}, null, true);
    });
    $('reset').addEventListener('click', function () { s = SUB.defaults(VW, VH); update({}); });
  }

  // --- Fareyle sürükleme ve ok tuşları ---
  function drag() {
    var box = $('subtitle'), st = null;
    box.addEventListener('pointerdown', function (e) {
      st = { px: e.clientX, py: e.clientY, x: s.x, y: s.y };
      box.setPointerCapture(e.pointerId);
      box.classList.add('dragging');
      $('stage').focus();
      e.preventDefault();
    });
    box.addEventListener('pointermove', function (e) {
      if (!st) return;
      var nx = st.x + (e.clientX - st.px) / scale, ny = st.y + (e.clientY - st.py) / scale;
      s.x = Math.round(nx); s.y = Math.round(ny);
      update({});
    });
    function end() { st = null; box.classList.remove('dragging'); }
    box.addEventListener('pointerup', end);
    box.addEventListener('pointercancel', end);
    $('stage').addEventListener('keydown', function (e) {
      var d = e.shiftKey ? 40 : 2, dx = 0, dy = 0;
      if (e.key === 'ArrowLeft') dx = -d; else if (e.key === 'ArrowRight') dx = d;
      else if (e.key === 'ArrowUp') dy = -d; else if (e.key === 'ArrowDown') dy = d;
      else return;
      e.preventDefault();
      update({ x: s.x + dx, y: s.y + dy });
    });
  }

  // --- Durum satırı ---
  function renderStatus() {
    var st = $('status');
    st.textContent = '';
    var a = document.createElement('div');
    a.className = connected ? 'ok' : 'off';
    a.textContent = connected ? 'TV bağlı' : 'TV bağlı değil — ayarlar TV\'ye bağlanınca gönderilebilir';
    st.appendChild(a);
    if (lastReport) {
      var d = new Date(lastReport), b = document.createElement('div');
      b.className = 'last';
      b.textContent = 'TV\'den son ayar: ' + (isNaN(d.getTime()) ? lastReport : d.toLocaleString('tr-TR'));
      st.appendChild(b);
    }
  }

  window.__editorLoad = function (settings, tvConnected) {
    loaded = settings ? SUB.sanitize(settings, VW, VH) : null;
    s = SUB.sanitize(loaded || SUB.defaults(VW, VH), VW, VH);
    connected = !!tvConnected;
    update({}, null, true);
    renderStatus();
  };

  window.__editorStatus = function (tvConnected, lastReportIso) {
    connected = !!tvConnected;
    if (lastReportIso) lastReport = lastReportIso;
    renderStatus();
  };

  $('sample').value = SAMPLE;
  build();
  drag();
  if (window.ResizeObserver) new ResizeObserver(fit).observe($('stage-wrap'));
  window.addEventListener('resize', fit);
  fit();
  update({}, null, true);
  renderStatus();
  post('ready');
})();
