// SPDX-License-Identifier: AGPL-3.0-only
// Ludora TV uygulaması (webOS ve Tizen ortak).
// HDMI girişini tam ekran gösterir, üstüne GameTranslator uygulamasından gelen altyazıyı basar.
// TV'ye özgü işler platform.js'tedir. Eski TV tarayıcıları için ES5 ile yazıldı.
(function () {
  'use strict';

  // TV uygulamasının sürümü: appinfo.json, tv-tizen/config.xml ve tv-android/app/build.gradle ile aynı tutulur.
  var APP_VERSION = '1.7.3';
  var t = window.t;
  var config = window.GAMETRANSLATOR_CONFIG || window.GAMETRANSLATOR_CONFIG || {};
  var platform = window.GAMETRANSLATOR_PLATFORM || window.GAMETRANSLATOR_PLATFORM;
  var KEY = { RED: 403, GREEN: 404, BLUE: 406, LEFT: 37, UP: 38, RIGHT: 39, DOWN: 40, OK: 13, BACK: platform.backKey };

  var SUB = window.GT_SUBSETTINGS;
  var server = null;
  var eventSource = null;
  // Altyazıyı kullanıcı durdurduysa (menüden) bağlantı da tarama da kapalı kalır. Kaydedilmez: uygulama hep çalışır açılır.
  var stopped = false;
  var connState = 'connecting';
  // Kumandayla seçilen port kayıtlıysa o kullanılır, yoksa PS5 otomatik aranır.
  var savedPort = Number(load('hdmi'));
  var hdmiPort = savedPort || 1;

  var bottomPercent = 7;
  // Çevirinin yeri: oyundaki İngilizce altyazının üstünü örterek (cover), üstünde (above), altında (below)
  // ya da sabit yükseklikte (fixed). Yeri bildirmeyen eski GameTranslator sürümlerinde sabit kalır.
  var PLACEMENTS = { cover: 1, above: 1, below: 1, fixed: 1 };
  var placement = 'fixed';
  // Gösterilen satırın oyundaki İngilizce altyazının yeri (ekrana oranla), bilinmiyorsa null.
  var currentBox = null;
  // Oyunun altyazısının gerçek yeri (örtme yerleşiminde çeviri buraya sığdırılır), ayarlardaki yazı boyu ve
  // yazının şu an sığdırılmış olup olmadığı.
  var currentSource = null;
  var baseFontSize = 0;
  var fitted = false;
  // Oyunun altyazısının satır yüksekliği (ekran yüksekliğine oranla); bilinmiyorsa 0.
  var currentLine = 0;
  var coverBackground = '';
  // Konuşmacı adının rengi; boşsa ad ayrı renkte yazılmaz.
  var speakerColor = '';

  // Önceki satır bu süreden kısa göründüyse okunamamış olabilir; yeni satırın üstünde bir süre daha kalır.
  var PREVIOUS_HOLD_MS = 2500;
  var shownAt = 0;
  var previousTimer = null;

  var hideTimer = null;
  var panelTimer = null;

  // TV altyazı ayarları (sayfa pikseli, bkz. subtitle-settings.js). Kayıtlı ayar yoksa eski "bottom" anahtarı taşınır.
  var subSettings = SUB.defaults(1920, 1080);
  var macDraft = null; // Mac'ten gelen geçici önizleme taslağı (kalıcı değildir)
  var macTimer = null;
  var macSampleTimer = null;
  var macBadge = null;
  var layoutRev = -1;
  var draft = null; // ayar ekranında düzenlenen taslak; Kaydet'e kadar kalıcı değildir
  var tvApplied = false;
  var pcStyle = {}; // Mac'in son gönderdiği "style" (konum kaynağı Mac olunca yeniden uygulanır)
  var subState = { lastAt: 0, shownText: null };
  var currentParagraphs = false;
  var contentHeight = 0;
  var sampleShown = false;
  var testOn = false;
  var provider = '';
  var lastSubText = '';
  var lastSubAt = 0;

  // Bilgisayarın "temizle" olayı satırı bundan erken kaldırmaz: oyundaki satır çeviri gelirken kalkmışsa yazı TV'de
  // bir an görünüp kayboluyordu. Süre, bilgisayarın okuma süresi kuralıyla aynıdır; üst sınırı daha kısadır.
  var HOLD_MAX_MS = 5000;
  var holdUntil = 0;
  // Bağlantı bir an koptuğunda yazı silinmez; ancak bu süre içinde geri gelmezse silinir.
  var DROP_HIDE_MS = 6000;
  var dropTimer = null;

  // Panelde görünen tanı bilgisi: "yazı TV'ye gelmiyor" bildirimlerinde sebebi buradan okuruz.
  var diag = { subtitles: 0, clears: 0, drops: 0, stale: 0, backgrounded: 0, lastHide: '-', place: '-', error: '' };

  function $(id) { return document.getElementById(id); }

  function renderDiag() {
    var el = $('st-diag');
    if (!el) return;
    el.textContent = 'sub ' + diag.subtitles + ' · clear ' + diag.clears + ' · drop ' + diag.drops + ' · stale ' + diag.stale +
      ' · bg ' + diag.backgrounded + ' · hide ' + diag.lastHide + ' · ' + diag.place +
      (window.__screenSaverStatus ? ' · saver ' + window.__screenSaverStatus : '') +
      (diag.error ? ' · err ' + diag.error : '');
  }

  function noteError(message) {
    diag.error = String(message).slice(0, 120);
    renderDiag();
  }

  window.onerror = function (message, source, line) {
    noteError(message + ' @' + line);
    return false;
  };

  document.addEventListener('visibilitychange', function () {
    if (document.hidden) diag.backgrounded++;
    renderDiag();
  });

  function load(key) {
    try { return window.localStorage.getItem('gametranslator.' + key) || window.localStorage.getItem('gametranslator.' + key); } catch (e) { return null; }
  }

  function save(key, value) {
    try { window.localStorage.setItem('gametranslator.' + key, String(value)); } catch (e) {}
  }

  function setStatus(id, text, cls) {
    var el = $(id);
    el.textContent = text;
    el.className = cls || '';
  }

  // --- Başlatma parametreleri ---

  function parseParams(raw) {
    if (!raw) return {};
    if (typeof raw === 'object') return raw;
    try {
      return JSON.parse(raw) || {};
    } catch (e) {
      try {
        var unescaped = raw.replace(/\\"/g, '"');
        return JSON.parse(unescaped) || {};
      } catch (e2) {
        var m = /"server"\s*:\s*"([^"]+)"/.exec(raw) || /server[:=]\s*(\S+)/.exec(raw);
        if (m) return { server: m[1] };
        return {};
      }
    }
  }

  // Parametrelerdeki sunucu ve HDMI tercihini uygular; sunucu değiştiyse yeniden bağlanır.
  function applyParams(params) {
    if (params.hdmi >= 1 && params.hdmi <= 4) {
      hdmiPort = Number(params.hdmi);
      save('hdmi', hdmiPort);
      showHdmi(hdmiPort);
    }
    if (params.server && params.server !== server) {
      save('server', params.server);
      connect(params.server);
    }
  }

  // --- HDMI görüntüsü ---

  function showHdmi(port) {
    setStatus('st-hdmi', String(port));
    var statuses = {
      loading: ['loading', 'muted'],
      ready: ['videoReady', 'ok'],
      playing: ['playing', 'ok'],
      inputError: ['inputError', 'bad'],
      sourceError: ['sourceError', 'bad']
    };
    platform.showHdmi(port, $('video-host'), function (kind) {
      setStatus('st-video', t(statuses[kind][0]), statuses[kind][1]);
    });
  }

  function autoSelectConsole(onNotFound) {
    platform.findConsolePort(function (port, label) {
      if (!port) return onNotFound && onNotFound();
      if (port !== hdmiPort) {
        hdmiPort = port;
        showHdmi(port);
      }
      setStatus('st-hdmi', port + ' — ' + label + ' (' + t('auto') + ')');
      if (menu.open && !ed.open) renderMenu();
    });
  }

  // --- Altyazı ---

  // Kutunun sayıları geçerli mi (0–1 arası, sağ soldan, alt üstten büyük).
  function validBox(box) {
    if (!box || typeof box !== 'object') return null;
    var left = Number(box.left), top = Number(box.top), right = Number(box.right), bottom = Number(box.bottom);
    if (isNaN(left) || isNaN(top) || isNaN(right) || isNaN(bottom)) return null;
    left = Math.max(0, Math.min(0.98, left));
    right = Math.max(left + 0.01, Math.min(1.0, right));
    top = Math.max(0, Math.min(0.98, top));
    bottom = Math.max(top + 0.01, Math.min(1.0, bottom));
    return { left: left, top: top, right: right, bottom: bottom };
  }

  // paragraphs: görev paneli gibi başlık ve maddeli yazılar satır satır, sola yaslı gösterilir.
  function showSubtitle(text, duration, box, paragraphs, source, line) {
    // Test ya da ayar ekranı açıkken gerçek altyazı onların yerine geçmez.
    if (sampleShown) return;
    // source: oyunun altyazısının gerçek yeri (örtme yerleşiminde çeviri tam bu alana sığdırılır).
    currentSource = paragraphs ? null : validBox(source);
    currentLine = typeof line === 'number' && line > 0 ? line : 0;
    // Önceki satır artık ayrıca gösterilmez: iki kutu üst üste binmiş gibi görünüyor, aynı cümlenin iki okunuşu
    // da ikinci çeviri gibi duruyordu. Yeni satır eskisinin yerine geçer.
    hidePrevious();
    shownAt = Date.now();
    holdUntil = shownAt + Math.min(HOLD_MAX_MS, 2000 + text.length * 60);
    currentBox = validBox(box);
    currentParagraphs = paragraphs === true;
    subState.shownText = text;
    setSubtitleText($('subtitle-text'), text, paragraphs);
    $('subtitle-text').style.whiteSpace = paragraphs ? 'pre-line' : '';
    $('subtitle-text').style.textAlign = paragraphs ? 'left' : '';
    $('subtitle').className = '';
    layout();
    lastSubText = text.length > 40 ? text.slice(0, 40) + '…' : text;
    lastSubAt = Date.now();
    renderLast();
    notePlace();
    clearTimeout(hideTimer);
    // Geçersiz ya da çok kısa süre yazıyı gösterir göstermez kaldırmasın.
    var ms = duration > 0 ? Math.max(1000, duration) : Math.min(8000, 2000 + text.length * 60);
    hideTimer = setTimeout(function () { hideSubtitle('timer'); }, ms);
  }

  // Konuşmacı adları ("MARKOS: …", cümle ortasında başlayan ikinci konuşmacı dahil) ayarlarda açıksa ayrı renkte
  // yazılır. Ad büyük harfle başlayan en çok üç sözcüktür ve satır başında ya da bir cümlenin bitiminde durur.
  var SPEAKER = /(^|[.!?…”"]\s+|\n)([A-ZÇĞİÖŞÜÂÎÛ][^\s:.!?]*(?: [A-ZÇĞİÖŞÜÂÎÛ][^\s:.!?]*){0,2}):(?=\s)/g;
  function setSubtitleText(el, text, paragraphs) {
    if (!speakerColor || paragraphs) {
      el.textContent = text;
      return;
    }
    el.textContent = '';
    var last = 0, match;
    SPEAKER.lastIndex = 0;
    while ((match = SPEAKER.exec(text)) !== null) {
      var nameStart = match.index + match[1].length;
      if (nameStart > last) el.appendChild(document.createTextNode(text.slice(last, nameStart)));
      var name = document.createElement('span');
      name.style.color = speakerColor;
      name.textContent = match[2] + ':';
      el.appendChild(name);
      last = SPEAKER.lastIndex;
    }
    if (last < text.length) el.appendChild(document.createTextNode(text.slice(last)));
  }

  // Yazının ekrandaki gerçek yeri ve görünürlüğü (tanı için).
  function notePlace() {
    var el = $('subtitle-text');
    var rect = el.getBoundingClientRect();
    var height = document.documentElement.clientHeight || window.innerHeight || 0;
    diag.place = 'y ' + Math.round(rect.top) + '-' + Math.round(rect.bottom) + '/' + height + ' ' +
      window.getComputedStyle($('subtitle')).visibility + ' f' + window.getComputedStyle(el).fontSize;
    renderDiag();
  }

  // reason: yazıyı neyin kaldırdığı; panelde görünen süreyle birlikte sebebi gösterir.
  function hideSubtitle(reason) {
    if (sampleShown && reason !== 'sample') return;
    clearTimeout(hideTimer);
    subState.shownText = null;
    if ($('subtitle').className !== 'hidden') {
      diag.lastHide = (reason || '?') + ' ' + (Date.now() - shownAt) + 'ms';
      renderDiag();
    }
    $('subtitle').className = 'hidden';
    $('subtitle-cover').className = 'hidden';
    hidePrevious();
  }

  // Bilgisayardan gelen "temizle": yazı okunacak kadar görünmediyse kalan süre dolunca kaldırılır.
  function onClear() {
    diag.clears++;
    renderDiag();
    var left = holdUntil - Date.now();
    if (left <= 0 || $('subtitle').className === 'hidden') return hideSubtitle('clear');
    clearTimeout(hideTimer);
    hideTimer = setTimeout(function () { hideSubtitle('clear-held'); }, left);
  }

  // Aynı metin yeniden geldi: yeniden çizilmez, yalnız kaldırma süresi uzar.
  function extendSubtitle(text, duration) {
    holdUntil = Date.now() + Math.min(HOLD_MAX_MS, 2000 + text.length * 60);
    clearTimeout(hideTimer);
    var ms = duration > 0 ? Math.max(1000, duration) : Math.min(8000, 2000 + text.length * 60);
    hideTimer = setTimeout(function () { hideSubtitle('timer'); }, ms);
  }

  function showPrevious(text) {
    $('subtitle-prev-text').textContent = text;
    $('subtitle-prev').className = '';
    clearTimeout(previousTimer);
    previousTimer = setTimeout(hidePrevious, PREVIOUS_HOLD_MS);
  }

  function hidePrevious() {
    clearTimeout(previousTimer);
    $('subtitle-prev').className = 'hidden';
  }

  // --- Ekranı çevir ---
  // Bilgisayar ekranın tamamını okuyup her yazı grubunun çevirisini yeriyle gönderir. Her çeviri kendi İngilizce
  // yazısını örter; yazı boyu o yazının satır yüksekliğinden seçilir, Türkçe uzun gelirse sığana kadar küçülür.

  var SCREEN_PAD = 0.004;
  var SCREEN_MIN_FONT = 14;
  var STACK_GAP = 8;
  // Bölge (region) başına ayrı kap ve süre sayacı: 'info' (eski Mac'te bölge yoksa), 'phone' vb.
  var screenRegions = {};

  function regionName(region) {
    return typeof region === 'string' && region ? region : 'info';
  }

  function regionEnabled(name) {
    return load(SUB.toggleKeyForRegion(name)) !== '0';
  }

  function updateScreenHost() {
    var any = false;
    for (var k in screenRegions) if (screenRegions.hasOwnProperty(k)) any = true;
    $('screen').className = any ? '' : 'hidden';
  }

  function showScreen(items, duration, region) {
    var name = regionName(region);
    hideScreen(name);
    var host = document.createElement('div');
    host.className = 'screen-region';
    host.setAttribute('data-region', name);
    $('screen').appendChild(host);
    var infoPlaced = [];
    var width = window.innerWidth || 1920;
    var height = window.innerHeight || 1080;
    var anyFull = false;
    for (var i = 0; i < (items || []).length; i++) {
      var item = items[i];
      var box = validBox(item && item.box);
      if (box && SUB.isBesideItem(item)) {
        if (regionEnabled(name)) showBesideItem(host, item, box, width, height);
        continue;
      }
      if (!box || !item.text) continue;
      if (SUB.isOverItem(item)) {
        if (regionEnabled(name)) infoPlaced.push(showOverItem(host, item, box, width, height));
        continue;
      }
      if (SUB.isInfoItem(item)) {
        if (regionEnabled(name)) infoPlaced.push(showInfoItem(host, item, box, width, height));
        continue;
      }
      anyFull = true;
      var el = document.createElement('div');
      el.className = item.paragraphs === true ? 'item paragraphs' : 'item';
      el.textContent = item.text;
      var left = Math.max(0, box.left - SCREEN_PAD);
      var top = Math.max(0, box.top - SCREEN_PAD);
      var boxHeight = (box.bottom - box.top + 2 * SCREEN_PAD) * height;
      el.style.left = (left * 100) + '%';
      el.style.top = (top * 100) + '%';
      el.style.width = Math.max(80, (Math.min(1, box.right + SCREEN_PAD) - left) * width) + 'px';
      el.style.minHeight = Math.round(boxHeight) + 'px';
      var line = typeof item.line === 'number' && item.line > 0 ? item.line * height : box.bottom * height - box.top * height;
      var size = Math.max(SCREEN_MIN_FONT, Math.min(96, Math.round(line * 0.95)));
      el.style.fontSize = size + 'px';
      host.appendChild(el);
      // Türkçe çoğu zaman İngilizceden uzundur: kutu biraz uzayabilir, daha fazlası için yazı küçülür.
      var limit = Math.max(boxHeight * 1.5, line * 2.6);
      while (el.scrollHeight > limit && size > SCREEN_MIN_FONT) {
        size -= 2;
        el.style.fontSize = size + 'px';
      }
    }
    // "Ekran çevriliyor…" yazısı tam ekran sonuçlarının üstünde kalmasın; yalnız bilgi kutusu gelirse ana altyazı korunur.
    if (anyFull) hideSubtitle('screen');
    // Aynı bölgedeki bilgi kutuları (telefon balonları) üst üste binmesin: sırayla aşağı it.
    if (infoPlaced.length > 1) {
      var rects = [];
      for (var r = 0; r < infoPlaced.length; r++) rects.push(infoPlaced[r].rect);
      var stacked = SUB.stackRects(rects, STACK_GAP);
      for (var q = 0; q < infoPlaced.length; q++) infoPlaced[q].el.style.top = stacked[q].top + 'px';
    }
    screenRegions[name] = { el: host, timer: setTimeout(function () { hideScreen(name); }, duration || 20000) };
    updateScreenHost();
  }

  // Bilgi kutusu çevirisi: İngilizce kutunun altında ayrı blok; ana altyazıya dokunmaz.
  var INFO_MAX_LINES = 6;
  var INFO_MIN_FONT = 16;

  function infoRect(placement, box, line, width, height, margin, contentHeight) {
    return placement === 'left' ? SUB.leftRect(box, line, width, height, margin, contentHeight) : SUB.belowRect(box, line, width, height, margin, contentHeight);
  }

  function showInfoItem(host, item, box, width, height) {
    var s = curSub();
    var line = typeof item.line === 'number' && item.line > 0 ? item.line : 0.03;
    var margin = Math.min(s.margin, Math.floor(width / 2) - 1);
    var el = document.createElement('div');
    el.className = 'info-item';
    el.textContent = item.text;
    var opacity = s.bgOpacity > 0 ? s.bgOpacity : 60;
    el.style.background = SUB.rgba(s.bgColor, opacity);
    el.style.color = s.textColor;
    var size = Math.max(26, Math.round(0.9 * line * height));
    var rect = infoRect(item.placement, box, line, width, height, margin, 0);
    el.style.left = rect.left + 'px';
    el.style.width = rect.width + 'px';
    el.style.top = rect.top + 'px';
    host.appendChild(el);
    for (var guard = 0; guard < 60; guard++) {
      el.style.fontSize = size + 'px';
      rect = infoRect(item.placement, box, line, width, height, margin, el.offsetHeight);
      el.style.left = rect.left + 'px';
      el.style.width = rect.width + 'px';
      el.style.top = rect.top + 'px';
      var maxLines = size * 1.25 * INFO_MAX_LINES + 12;
      if ((el.offsetHeight <= rect.maxHeight && el.offsetHeight <= maxLines) || size <= INFO_MIN_FONT) break;
      size -= 2;
    }
    return { el: el, rect: { left: rect.left, top: rect.top, width: rect.width, height: el.offsetHeight } };
  }

  // "over": Türkçe kart İngilizce arayüz öğesini tam örter (temalı, opak zemin). Yazı overFontSize ile başlar;
  // sığmazsa kart aşağı (alta yakınsa yukarı) en çok kutunun 1.6 katına büyür, sonra yazı en az 18 px'e küçülür.
  var OVER_MIN_FONT = 18;

  function showOverItem(host, item, box, width, height) {
    var s = curSub();
    var line = typeof item.line === 'number' && item.line > 0 ? item.line : 0.03;
    var margin = Math.min(s.margin, Math.floor(width / 2) - 1);
    var parts = SUB.overParts(item);
    var st = SUB.overCardStyle(SUB.overTheme(item), s.bgOpacity);
    var el = document.createElement('div');
    el.className = 'info-item over-item over-' + st.theme;
    el.style.background = st.background;
    el.style.color = st.color;
    el.style.boxShadow = st.boxShadow;
    el.style.textShadow = st.textShadow;
    if (parts.speaker) {
      var sp = document.createElement('div');
      sp.className = 'over-speaker';
      sp.textContent = parts.speaker;
      el.appendChild(sp);
    }
    for (var i = 0; i < parts.segments.length; i++) {
      var seg = document.createElement('div');
      seg.className = (i === 0 && parts.titleBold) ? 'over-seg over-title' : 'over-seg';
      seg.textContent = parts.segments[i];
      el.appendChild(seg);
    }
    var size = SUB.overFontSize(line, height, s.fontSize);
    var boxH = Math.max(0, (box.bottom - box.top) * height);
    var rect = SUB.overRect(box, line, width, height, margin, 0);
    el.style.opacity = '0';
    host.appendChild(el);
    for (var guard = 0; guard < 60; guard++) {
      el.style.fontSize = size + 'px';
      el.style.left = rect.left + 'px';
      el.style.width = rect.width + 'px';
      el.style.minHeight = rect.height + 'px';
      el.style.top = rect.top + 'px';
      rect = SUB.overRect(box, line, width, height, margin, el.offsetHeight);
      el.style.top = rect.top + 'px';
      var limit = Math.max(rect.height, SUB.overMaxGrow(boxH, rect.maxHeight));
      if (SUB.overFit(el.offsetHeight, limit, size, OVER_MIN_FONT) !== 'shrink') break;
      size -= 2;
    }
    // Yumuşak giriş: ilk kare ölçüldükten sonra opaklık geçişi (eski WebView'da animasyon yerine geçiş).
    el.style.webkitTransition = 'opacity 150ms ease-out';
    el.style.transition = 'opacity 150ms ease-out';
    setTimeout(function () { el.style.opacity = '1'; }, 16);
    return { el: el, rect: { left: rect.left, top: rect.top, width: rect.width, height: el.offsetHeight } };
  }

  // "beside": telefonun yanında sohbet paneli (başlıkta kişi adı, "them" solda, "me" sağda mavi). İngilizce gösterilmez.
  // Taşarsa en eski mesajlar üstten atılır; en yeni görünür kalır.
  function showBesideItem(host, item, box, width, height) {
    var s = curSub();
    var margin = Math.min(s.margin, Math.floor(width / 2) - 1);
    var rect = SUB.besideRect(box, width, height, margin);
    var msgs = SUB.besideMessages(item);
    var el = document.createElement('div');
    el.className = 'chat-panel';
    el.style.left = rect.left + 'px';
    el.style.top = rect.top + 'px';
    el.style.width = rect.width + 'px';
    el.style.maxHeight = rect.maxHeight + 'px';
    el.style.fontSize = SUB.besideFontSize(height) + 'px';
    var head = null;
    var who = typeof item.speaker === 'string' ? item.speaker.replace(/^\s+|\s+$/g, '') : '';
    if (who) {
      head = document.createElement('div');
      head.className = 'chat-head';
      head.textContent = who;
      el.appendChild(head);
    }
    var list = document.createElement('div');
    list.className = 'chat-list';
    var bubbles = [];
    for (var i = 0; i < msgs.length; i++) {
      var b = document.createElement('div');
      b.className = 'chat-bubble chat-' + msgs[i].from;
      b.textContent = msgs[i].text;
      list.appendChild(b);
      bubbles.push(b);
    }
    el.appendChild(list);
    el.style.opacity = '0';
    host.appendChild(el);
    var heights = [];
    for (var k = 0; k < bubbles.length; k++) heights.push(bubbles[k].offsetHeight);
    var cs = window.getComputedStyle ? window.getComputedStyle(el) : null;
    var pad = cs ? (parseFloat(cs.paddingTop) || 0) + (parseFloat(cs.paddingBottom) || 0) : 20;
    var avail = rect.maxHeight - pad - (head ? head.offsetHeight + 6 : 0);
    var first = SUB.besideFirstVisible(heights, 6, avail);
    for (var j = 0; j < first; j++) bubbles[j].style.display = 'none';
    el.style.webkitTransition = 'opacity 150ms ease-out';
    el.style.transition = 'opacity 150ms ease-out';
    setTimeout(function () { el.style.opacity = '1'; }, 16);
    return el;
  }

  // Yalnız geliştirme önizlemesi (dev-preview ?demoInfo=1) için.
  window.GT_DEBUG_SCREEN = function (data) { showScreen(data.items, data.duration, data.region); };

  // region verilmezse hepsi temizlenir (eski davranış).
  function hideScreen(region) {
    var names = [];
    if (typeof region === 'string' && region) names.push(region);
    else for (var k in screenRegions) if (screenRegions.hasOwnProperty(k)) names.push(k);
    for (var i = 0; i < names.length; i++) {
      var r = screenRegions[names[i]];
      if (!r) continue;
      clearTimeout(r.timer);
      if (r.el.parentNode) r.el.parentNode.removeChild(r.el);
      delete screenRegions[names[i]];
    }
    updateScreenHost();
  }

  // Konum hem GameTranslator uygulamasından hem kumandanın oklarıyla ayarlanır. Kumandayla
  // seçilen konum, uygulamadaki ayar değişene kadar korunur (son değiştiren geçerlidir).
  function setBottom(percent) {
    bottomPercent = Math.max(0, Math.min(85, percent));
    layout();
  }

  // --- TV altyazı ayarı: tek yerleşim yolu ---
  // Gerçek altyazılar, test altyazısı ve ayar ekranındaki önizleme hep bu işlevden geçer (applySubtitleLayout).

  function pageW() { return window.innerWidth || 1920; }
  function pageH() { return window.innerHeight || 1080; }

  // Ayar ekranı açıkken taslak, yoksa kayıtlı ayar geçerlidir.
  function curSub() { return draft || macDraft || subSettings; }
  function tvActive() { return curSub().positionSource === 'tv'; }

  function loadSubSettings() {
    var s = SUB.parse(load('subStyle'), pageW(), pageH());
    if (s) return s;
    var old = load('bottom') || load('appBottom');
    return old ? SUB.migrate(Number(old), pageH()) : SUB.defaults(pageW(), pageH());
  }

  // Kutuyu GT_SUBLAYOUT ile yerleştirir (font küçültme döngüsü orada); örtü ve sığdırma temizliği burada kalır.
  function applySubtitleLayout() {
    var s = curSub();
    $('subtitle-cover').className = 'hidden';
    unfit();
    var r = window.GT_SUBLAYOUT.apply($('subtitle'), $('subtitle-text'), s, pageW(), pageH(), { paragraphs: currentParagraphs });
    contentHeight = r.contentHeight;
  }

  // Konum kaynağı Mac olunca TV'nin yazdığı satır içi stiller silinir.
  function clearTvStyles() {
    var el = $('subtitle'), text = $('subtitle-text');
    el.style.width = '';
    el.style.lineHeight = '';
    el.style.textAlign = '';
    text.style.textShadow = '';
    text.style.verticalAlign = '';
    text.style.textAlign = '';
    text.style.color = '';
    text.style.background = '';
    text.style.fontWeight = '';
    text.style.fontSize = '';
  }

  // Test altyazısı ve ayar önizlemesi gerçek #subtitle öğesini kullanır.
  function showSample(text) {
    sampleShown = true;
    clearTimeout(hideTimer);
    hidePrevious();
    currentBox = null;
    currentSource = null;
    currentLine = 0;
    currentParagraphs = false;
    subState.shownText = null;
    setSubtitleText($('subtitle-text'), text, false);
    $('subtitle-text').style.whiteSpace = '';
    $('subtitle-text').style.textAlign = '';
    $('subtitle').className = '';
    layout();
  }

  function refreshSample() {
    if (ed.open) showSample(t('sampleSubtitle'));
    else if (testOn) showSample(t('localTest'));
    else if (sampleShown) {
      sampleShown = false;
      hideSubtitle('sample');
    }
  }

  function toggleTest() {
    testOn = !testOn;
    refreshSample();
    if (menu.open && !ed.open) renderMenu();
  }

  // Altyazıyı seçilen yerleşime göre konumlar. İngilizce altyazının yeri bilinmiyorsa sabit yükseklik kullanılır.
  function layout() {
    if (tvActive()) {
      tvApplied = true;
      applySubtitleLayout();
      return;
    }
    if (tvApplied) {
      // TV ayarından Mac'e dönüldü: TV'nin yazdığı satır içi stiller silinir, Mac'in son stili yeniden uygulanır.
      tvApplied = false;
      clearTvStyles();
      applyStyle(pcStyle);
    }
    var el = $('subtitle');
    var cover = $('subtitle-cover');
    var box = placement === 'fixed' ? null : currentBox;

    el.style.left = '4%';
    el.style.right = '4%';
    el.style.top = 'auto';
    el.style.bottom = Math.max(2, Math.min(88, bottomPercent)) + '%';
    el.style.transform = 'none';
    el.style.webkitTransform = 'none';
    cover.className = 'hidden';
    unfit();

    // Altyazı yokken veya kutu tanımlı değilken örtü tek başına kalmasın
    if (!box || el.className === 'hidden') return;

    var mode = placement;
    // Ekranın kenarında yer yoksa öbür yana geçilir.
    if (mode === 'below' && box.bottom > 0.88) mode = 'above';
    if (mode === 'above' && box.top < 0.12) mode = 'below';

    if (mode === 'cover' && currentSource) {
      fitToSource(el, cover, currentSource);
    } else if (mode === 'cover') {
      // İngilizce altyazının tam üstünü örten koyu zemin
      var padX = 0.02; // Yatayda %2 pay
      var padY = 0.015; // Dikeyde %1.5 pay
      var cLeft = Math.max(0.01, box.left - padX);
      var cTop = Math.max(0.01, box.top - padY);
      var cRight = Math.min(0.99, box.right + padX);
      var cBottom = Math.min(0.99, box.bottom + padY);

      cover.style.left = (cLeft * 100).toFixed(2) + '%';
      cover.style.top = (cTop * 100).toFixed(2) + '%';
      cover.style.width = ((cRight - cLeft) * 100).toFixed(2) + '%';
      cover.style.height = ((cBottom - cTop) * 100).toFixed(2) + '%';
      cover.className = '';

      // Türkçe çeviri bu örtünün tam ortasına dikeyde oturur
      var centerBottom = (1.0 - (box.top + box.bottom) / 2.0) * 100.0;
      el.style.top = 'auto';
      el.style.bottom = Math.max(2, Math.min(92, centerBottom)).toFixed(2) + '%';
      el.style.transform = 'translateY(50%)';
      el.style.webkitTransform = 'translateY(50%)';
    } else if (mode === 'above') {
      // İngilizce yazının hemen üstünde (İngilizce yazıyı kapatmaz)
      cover.className = 'hidden';
      var aboveBottom = (1.0 - box.top) * 100.0 + 1.5;
      el.style.top = 'auto';
      el.style.bottom = Math.max(5, Math.min(94, aboveBottom)).toFixed(2) + '%';
      el.style.transform = 'none';
      el.style.webkitTransform = 'none';
    } else if (mode === 'below') {
      // İngilizce yazının hemen altında
      cover.className = 'hidden';
      var belowTop = box.bottom * 100.0 + 1.5;
      el.style.bottom = 'auto';
      el.style.top = Math.max(5, Math.min(94, belowTop)).toFixed(2) + '%';
      el.style.transform = 'none';
      el.style.webkitTransform = 'none';
    }
  }

  // Çeviriyi oyunun altyazısının kapladığı alana sığdırır: aynı genişlikte sarılır, yazı boyu alana sığan en büyük
  // boydur (ayarlardaki boyun 0,6–1,6 katı arasında). Böylece çeviri oyunun altyazısıyla aynı yerde, aynı satır
  // düzeninde ve hemen hemen aynı boyda durur; zemin tek parçadır. En küçük boyda da sığmıyorsa zemin yazıya göre büyür.
  function fitToSource(el, cover, box) {
    var padX = 0.008, padY = 0.006;
    var left = Math.max(0, box.left - padX), right = Math.min(1, box.right + padX);
    var top = Math.max(0, box.top - padY), bottom = Math.min(1, box.bottom + padY);
    // Çok dar altyazıda (tek sözcük) çeviri en az bu kadar yer bulur.
    var minWidth = 0.16;
    if (right - left < minWidth) {
      var center = (left + right) / 2;
      left = Math.max(0, center - minWidth / 2);
      right = Math.min(1, center + minWidth / 2);
    }
    // Zemin önce oyunun altyazısının yerine konur; ölçüler ondan alınır (yüzde ve piksel karışmasın).
    var place = function (l, t, r, b) {
      cover.style.left = (l * 100).toFixed(3) + '%';
      cover.style.top = (t * 100).toFixed(3) + '%';
      cover.style.width = ((r - l) * 100).toFixed(3) + '%';
      cover.style.height = ((b - t) * 100).toFixed(3) + '%';
    };
    place(left, top, right, bottom);
    // Oyunun altyazısı alttan hiç görünmesin: zemin tam opaktır.
    if (!fitted) coverBackground = cover.style.background;
    cover.style.background = 'rgb(10, 10, 12)';
    cover.className = '';
    var area = cover.getBoundingClientRect();
    var perX = area.width / (right - left), perY = area.height / (bottom - top);

    var text = $('subtitle-text');
    el.style.left = (left * 100).toFixed(3) + '%';
    el.style.right = ((1 - right) * 100).toFixed(3) + '%';
    el.style.bottom = 'auto';
    el.style.top = (((top + bottom) / 2) * 100).toFixed(3) + '%';
    el.style.transform = 'translateY(-50%)';
    el.style.webkitTransform = 'translateY(-50%)';
    text.style.padding = '0 6px';
    text.style.lineHeight = '1.2';

    // En büyük boy oyunun altyazısının boyudur (satır yüksekliği biliniyorsa); bilinmiyorsa ayarlardaki boy.
    var base = baseFontSize || 46;
    var low = Math.max(16, Math.round(base * 0.55));
    var high = currentLine > 0 ? Math.round(currentLine * perY * 1.05) : Math.round(base * 1.15);
    high = Math.max(low, Math.min(Math.round(base * 1.6), high));
    var fits = function (size) {
      text.style.fontSize = size + 'px';
      var rect = text.getBoundingClientRect();
      return rect.width <= area.width + 1 && rect.height <= area.height + 1;
    };
    var best = low;
    if (fits(high)) best = high;
    else if (fits(low)) {
      var a = low, b = high;
      while (b - a > 1) {
        var mid = Math.floor((a + b) / 2);
        if (fits(mid)) a = mid; else b = mid;
      }
      best = a;
    }
    text.style.fontSize = best + 'px';

    // Yazı en küçük boyda da taşıyorsa zemin onu da içine alacak kadar büyür (tek parça kalır).
    var rect = text.getBoundingClientRect();
    var growTop = Math.max(0, area.top - rect.top + 2) / perY, growBottom = Math.max(0, rect.bottom - area.bottom + 2) / perY;
    var growLeft = Math.max(0, area.left - rect.left + 2) / perX, growRight = Math.max(0, rect.right - area.right + 2) / perX;
    if (growTop || growBottom || growLeft || growRight)
      place(Math.max(0, left - growLeft), Math.max(0, top - growTop), Math.min(1, right + growRight), Math.min(1, bottom + growBottom));
    fitted = true;
  }

  // Sığdırma yerleşiminden çıkınca yazının ayarlardaki boyu ve boşlukları geri gelir.
  function unfit() {
    if (!fitted) return;
    fitted = false;
    var text = $('subtitle-text');
    $('subtitle-cover').style.background = coverBackground;
    text.style.padding = '';
    text.style.lineHeight = '';
    if (baseFontSize) text.style.fontSize = baseFontSize + 'px';
  }

  // Bazı TV'lerde sabit konumun ekran dışına taşmasını engeller.
  function keepOnScreen(el, cover) {
    if (placement === 'fixed') {
      bottomPercent = Math.max(2, Math.min(85, bottomPercent));
      el.style.bottom = bottomPercent + '%';
    }
  }

  function applyStyle(style) {
    for (var k in style) if (Object.prototype.hasOwnProperty.call(style, k)) pcStyle[k] = style[k];
    if (typeof style.provider === 'string' && style.provider) setProvider(style.provider);
    // Konum kaynağı TV ise Mac'in yazı boyu, renk, zemin, kalınlık, konum ve yerleşimi ana altyazıda yok sayılır.
    if (tvActive()) {
      if (typeof style.speakerColor === 'string') speakerColor = style.speakerColor;
      layout();
      return;
    }
    if (style.fontSize) {
      baseFontSize = style.fontSize;
      $('subtitle-text').style.fontSize = style.fontSize + 'px';
      $('subtitle-prev-text').style.fontSize = Math.round(style.fontSize * 0.8) + 'px';
    }
    if (style.color) {
      $('subtitle-text').style.color = style.color;
      $('subtitle-prev-text').style.color = style.color;
    }
    if (PLACEMENTS[style.placement]) placement = style.placement;
    if (typeof style.speakerColor === 'string') speakerColor = style.speakerColor;
    if (typeof style.background === 'number') {
      var alpha = Math.max(0, Math.min(100, style.background)) / 100;
      $('subtitle-text').style.background = 'rgba(0, 0, 0, ' + alpha + ')';
      $('subtitle-prev-text').style.background = 'rgba(0, 0, 0, ' + (alpha * 0.83).toFixed(2) + ')';
      var coverEl = $('subtitle-cover');
      // Örtü İngilizce altyazıyı tamamen kapatmalı: %92 koyulukta beyaz yazı soluk da olsa seçiliyordu.
      if (coverEl) coverEl.style.background = 'rgb(10, 10, 12)';
    }
    if (typeof style.bold === 'boolean') {
      $('subtitle-text').style.fontWeight = style.bold ? '700' : '600';
      $('subtitle-prev-text').style.fontWeight = style.bold ? '700' : '600';
    }
    if (style.bottomPercent >= 0) {
      bottomPercent = Math.max(0, Math.min(92, Number(style.bottomPercent)));
    }
    layout();
  }

  // --- Mac'ten uzaktan düzen (bkz. docs/uzaktan-duzen-protokolu.md) ---

  // TV'nin kayıtlı ayarını Mac'e bildirir (Content-Type text/plain: WebView'de preflight oluşmaz).
  function reportLayout() {
    if (!server) return;
    try {
      var xhr = new XMLHttpRequest();
      xhr.open('POST', server + '/tv-layout', true);
      xhr.setRequestHeader('Content-Type', 'text/plain');
      xhr.onerror = function () {};
      xhr.send(JSON.stringify({ settings: SUB.sanitize(subSettings, pageW(), pageH()), source: 'tv', app: APP_VERSION }));
    } catch (e) { /* bildirim başarısızsa ayar yine de TV'de kayıtlıdır */ }
  }

  function setMacBadge(on) {
    if (on && !macBadge) {
      macBadge = document.createElement('div');
      macBadge.textContent = t('macEditing');
      macBadge.style.cssText = 'position:absolute;top:24px;right:24px;z-index:30;padding:8px 18px;border-radius:12px;' +
        'background:rgba(20,20,24,0.85);color:#F5B83D;font-size:24px;font-weight:600;';
      document.body.appendChild(macBadge);
    } else if (!on && macBadge) {
      if (macBadge.parentNode) macBadge.parentNode.removeChild(macBadge);
      macBadge = null;
    }
  }

  // Önizlemeyi kapatır; örnek satır (başka sebeple açık değilse) gizlenir, gerçek altyazılar yeniden akar.
  function endMacPreview() {
    clearTimeout(macTimer);
    clearTimeout(macSampleTimer);
    macTimer = null;
    macSampleTimer = null;
    setMacBadge(false);
    if (!macDraft && !macSampleActive) return;
    macDraft = null;
    macSampleActive = false;
    refreshSample();
    if ($('subtitle').className !== 'hidden') layout();
  }
  var macSampleActive = false;

  function handleLayout(data) {
    // TV'deki düzenleme önceliklidir.
    if (ed.open) return;
    if (typeof data.rev === 'number') {
      if (data.rev < layoutRev) return;
      layoutRev = data.rev;
    }
    if (data.mode === 'cancel') return endMacPreview();
    if (data.mode !== 'preview' && data.mode !== 'save') return;
    var s = SUB.sanitize(data.settings, pageW(), pageH());
    s.positionSource = 'tv';
    clearTimeout(macTimer);
    clearTimeout(macSampleTimer);
    macSampleActive = true;
    if (data.mode === 'preview') {
      macDraft = s;
      setMacBadge(true);
      macTimer = setTimeout(endMacPreview, 60000);
      showSample(t('sampleSubtitle'));
      return;
    }
    macDraft = null;
    setMacBadge(false);
    subSettings = s;
    save('subStyle', SUB.serialize(subSettings, pageW(), pageH()));
    showSample(t('sampleSubtitle'));
    macSampleTimer = setTimeout(endMacPreview, 3000);
    reportLayout();
  }

  // --- Sunucu bağlantısı (Server-Sent Events) ---

  // Mac her 5 sn'de bir 'ping' olayı yollar; ölü (uykudan sonra sessiz kalan) akışı bundan anlarız.
  var lastEventAt = 0;
  var pingSeen = false;
  var watchdog = null;
  var STALE_MS = 20000;

  function noteAlive(name) {
    lastEventAt = Date.now();
    if (name === 'ping') pingSeen = true;
  }

  function startWatchdog() {
    if (watchdog !== null) return;
    watchdog = setInterval(function () {
      // Eski Mac ping yollamaz: ilk ping görülmeden bayatlık denetlenmez.
      if (connState !== 'connected' || !pingSeen || !server) return;
      if (Date.now() - lastEventAt <= STALE_MS) return;
      diag.drops++;
      renderDiag();
      connect(server);
    }, 5000);
  }

  function onEvent(name, handler) {
    eventSource.addEventListener(name, function (e) {
      noteAlive(name);
      var data;
      try {
        data = JSON.parse(e.data);
      } catch (err) {
        noteError(name + ': bad json');
        return;
      }
      try {
        handler(data);
      } catch (err) {
        noteError(name + ': ' + err.message);
      }
    });
  }

  function shortAddr(address) {
    return String(address || '').replace(/^https?:\/\//, '').replace(/\/$/, '');
  }

  // Bağlantı durumunu anlaşılır Türkçe (ya da İngilizce) cümleyle yazar.
  function renderConn() {
    var addr = shortAddr(server);
    var text, cls = '';
    if (connState === 'stopped') { text = t('connStopped'); cls = 'bad'; }
    else if (connState === 'unsupported') { text = t('noEventSource'); cls = 'bad'; }
    else if (connState === 'connected') { text = t('connected'); cls = 'ok'; }
    else if (connState === 'failed') { text = t('connFailed').replace('{addr}', addr) + (scanning ? t('connScanning') : ''); cls = 'bad'; }
    else if (connState === 'none') { text = t('connNone'); cls = 'bad'; }
    else { text = t('connecting') + (addr ? ' (' + addr + ')' : ''); cls = 'muted'; }
    setStatus('st-conn', text, cls);
  }

  function connect(address) {
    if (demoRunning()) stopDemo();
    if (eventSource) eventSource.close();
    if (address !== server) failures = 0;
    stopped = false;
    server = address;
    $('waiting').className = 'hidden';
    setStatus('st-server', shortAddr(address));
    if (!window.EventSource) {
      connState = 'unsupported';
      renderConn();
      return;
    }
    connState = 'connecting';
    renderConn();
    lastEventAt = Date.now();
    pingSeen = false;
    startWatchdog();
    eventSource = new EventSource(address + '/events');
    eventSource.onopen = function () {
      lastEventAt = Date.now();
      failures = 0;
      layoutRev = -1;
      reportLayout();
      cancelScan();
      clearTimeout(dropTimer);
      dropTimer = null;
      subState.lastAt = 0;
      connState = 'connected';
      renderConn();
    };
    eventSource.onerror = function () {
      // Bağlantı kısa süre içinde geri gelmezse eski altyazı ekranda asılı kalmasın. Bir anlık kopma yazıyı silmez.
      diag.drops++;
      renderDiag();
      if (dropTimer === null) {
        dropTimer = setTimeout(function () {
          dropTimer = null;
          hideSubtitle('drop');
          hideScreen();
        }, DROP_HIDE_MS);
      }
      connState = 'failed';
      renderConn();
      // Bilgisayarın adresi değişmiş olabilir.
      noteFailure();
    };
    onEvent('subtitle', function (data) {
      diag.subtitles++;
      renderDiag();
      if (typeof data.provider === 'string' && data.provider) setProvider(data.provider);
      if (!data.text || sampleShown) return;
      // Geç gelen eski sonuç atılır; ekrandaki metnin aynısı yalnız süreyi uzatır.
      var verdict = SUB.acceptSubtitle(subState, data);
      if (verdict === 'drop') {
        diag.stale++;
        renderDiag();
        return;
      }
      if (typeof data.at === 'number') subState.lastAt = data.at;
      if (verdict === 'extend') return extendSubtitle(data.text, data.duration);
      showSubtitle(data.text, data.duration, data.box, data.paragraphs === true, data.source, data.line);
    });
    onEvent('clear', onClear);
    onEvent('screen', function (data) { showScreen(data.items, data.duration, data.region); });
    // screen-clear verisi boş/bozuk olabilir (eski Mac): o zaman hepsini temizle.
    eventSource.addEventListener('screen-clear', function (e) {
      noteAlive('screen-clear');
      var region = null;
      try {
        var d = JSON.parse(e.data);
        if (d && typeof d.region === 'string' && d.region) region = d.region;
      } catch (err) { region = null; }
      hideScreen(region);
    });
    onEvent('style', applyStyle);
    onEvent('layout', handleLayout);
    onEvent('speech', function (data) { if (data.url) playSpeech(server + data.url); });
    onEvent('speech-stop', stopSpeech);
    onEvent('ping', function () {});
  }

  // --- Türkçe dublaj ---
  // Bilgisayar her cümleyi seslendirip adresini gönderir; TV oyunun sesiyle birlikte çalar. Bilgisayar sıradakini
  // bu ses bitince gönderir; yine de üst üste gelirse eskisi kesilir.

  var speech = null;

  function playSpeech(url) {
    stopSpeech();
    try {
      speech = new Audio(url);
      var played = speech.play();
      if (played && played.catch) played.catch(function () { speech = null; });
    } catch (e) {
      speech = null;
    }
  }

  function stopSpeech() {
    if (!speech) return;
    try { speech.pause(); } catch (e) { /* yok */ }
    speech = null;
  }

  // --- Bilgisayarı ağda bulma ---
  // Bilgisayar TV uygulamasını açamıyorsa adres gelmez; kayıtlı adres de eskiyebilir (bilgisayarın IP'si değişti).
  // Uygulama o zaman ağı tarar: TV'nin kendi ağını (adresini söyleyebiliyorsa) ve kayıtlı adresin ağını. Kayıtlı adres
  // art arda FAILS_BEFORE_SCAN kez yanıt vermezse tarama kısa süre içinde başlar (CouchSubs aff58ea). LG webOS'ta
  // platform.localAddress yoktu; tarama hiç başlamıyor, TV eski adreste sonsuza dek bekliyordu.

  var SERVER_PORT = 8787;
  var SCAN_PARALLEL = 24;
  var SCAN_TIMEOUT_MS = 1200;
  var SCAN_PAUSE_MS = 30000;
  var SCAN_SOON_MS = 3000;
  var FAILS_BEFORE_SCAN = 3;
  var scanTimer = null;
  var scanDue = 0;
  var scanning = false;
  var failures = 0;

  // Bekleyen tarama daha erken başlayacaksa korunur; EventSource birkaç saniyede bir yeniden denediği için sayaç
  // baştan kurulursa tarama hiç başlamazdı.
  function scheduleScan(delay) {
    if (scanning || stopped) return;
    if (delay === undefined) delay = SCAN_PAUSE_MS;
    var due = Date.now() + delay;
    if (scanTimer !== null && scanDue <= due) return;
    clearTimeout(scanTimer);
    scanDue = due;
    scanTimer = setTimeout(function () {
      scanTimer = null;
      scanForServer();
    }, delay);
  }

  function cancelScan() {
    clearTimeout(scanTimer);
    scanTimer = null;
  }

  // Bağlantı hatası: kayıtlı adres art arda birkaç kez yanıt vermediyse ağ hemen taranır, yoksa bir süre beklenir.
  function noteFailure() {
    failures++;
    scheduleScan(failures >= FAILS_BEFORE_SCAN ? SCAN_SOON_MS : SCAN_PAUSE_MS);
  }

  function subnetOf(address) {
    var match = /^(?:https?:\/\/)?(\d+\.\d+\.\d+)\.\d+(?::\d+)?\/?$/.exec(address || '');
    return match ? match[1] : null;
  }

  // Taranacak ağlar: TV'nin kendi ağı, sonra kayıtlı adresin ağı (aynıysa bir kez); ikisi de yoksa 192.168.1.
  function scanSubnets(done) {
    var fromServer = subnetOf(server || load('server'));
    function finish(ip) {
      var list = [];
      var own = subnetOf(ip);
      if (own) list.push(own);
      if (fromServer && fromServer !== own) list.push(fromServer);
      if (!list.length) list.push('192.168.1');
      done(list);
    }
    if (!platform.localAddress) return finish(null);
    var answered = false;
    function once(ip) {
      if (answered) return;
      answered = true;
      finish(ip);
    }
    // Adresi soran servis yanıt vermezse tarama kayıtlı adresin ağıyla sürer.
    setTimeout(function () { once(null); }, 3000);
    try {
      platform.localAddress(once);
    } catch (e) {
      once(null);
    }
  }

  function probe(address, done) {
    var request = new XMLHttpRequest();
    var finished = false;
    function finish(ok) {
      if (finished) return;
      finished = true;
      done(ok);
    }
    request.onload = function () {
      var body = (request.responseText || '').toLowerCase();
      finish(request.status === 200 && (body.indexOf('gametranslator') >= 0 || body.indexOf('gametrans') >= 0 || body.indexOf('couchsubs') >= 0));
    };
    request.onerror = function () { finish(false); };
    request.ontimeout = function () { finish(false); };
    try {
      request.open('GET', address + '/hello');
      request.timeout = SCAN_TIMEOUT_MS;
      request.send();
    } catch (e) {
      finish(false);
    }
  }

  function scanForServer() {
    if (scanning || stopped) return;
    cancelScan();
    scanning = true;
    renderConn();
    scanSubnets(function (subnets) {
      var addresses = [];
      for (var s = 0; s < subnets.length; s++) {
        for (var host = 1; host <= 254; host++) addresses.push('http://' + subnets[s] + '.' + host + ':' + SERVER_PORT);
      }
      var next = 0, active = 0, found = false;
      function pump() {
        if (stopped) {
          scanning = false;
          return;
        }
        while (!found && active < SCAN_PARALLEL && next < addresses.length) {
          active++;
          tryAddress(addresses[next++]);
        }
        if (!found && !active && next >= addresses.length) {
          scanning = false;
          renderConn();
          scheduleScan();
        }
      }
      function tryAddress(address) {
        probe(address, function (ok) {
          active--;
          if (found) return;
          if (!ok) return pump();
          found = true;
          scanning = false;
          renderConn();
          if (stopped) return;
          // Bulunan adres kaydedilir: TV bir sonraki açılışta eski adrese takılmasın.
          save('server', address);
          // Aynı bilgisayarsa EventSource kendisi yeniden bağlanır; kapanıp vazgeçtiyse (CLOSED) yeniden kurulur.
          if (address !== server || !eventSource || eventSource.readyState === 2) connect(address);
        });
      }
      pump();
    });
  }

  // --- Demo ---
  // Mağaza incelemesi ve ilk kez açanlar için: PS5 ve bilgisayar olmadan örnek altyazılar gösterilir.

  var DEMO_LINES = [
    'Buraya ayıracak vaktimiz yok.',
    'MİRA: Kapı daha fazla dayanmayacak!',
    'Yakın dur. İlerideki yol tehlikeli.',
    'Bekle! Sen de kimsin?',
    'Bu iş henüz bitmedi.'
  ];
  var DEMO_LINE_MS = 3200;
  var DEMO_GAP_MS = 700;
  var demoTimer = null;
  var demoIndex = 0;

  function demoRunning() { return demoTimer !== null; }

  function nextDemoLine() {
    showSubtitle(DEMO_LINES[demoIndex], DEMO_LINE_MS);
    demoIndex = (demoIndex + 1) % DEMO_LINES.length;
    demoTimer = setTimeout(nextDemoLine, DEMO_LINE_MS + DEMO_GAP_MS);
  }

  function startDemo() {
    if (demoRunning()) return;
    hidePanel();
    $('waiting').className = 'hidden';
    $('demo-badge').className = '';
    demoIndex = 0;
    nextDemoLine();
  }

  function stopDemo() {
    clearTimeout(demoTimer);
    demoTimer = null;
    hideSubtitle('demo');
    $('demo-badge').className = 'hidden';
    // Uygulamaya bağlı değilse bekleme ekranına dönülür.
    if (!server) $('waiting').className = '';
  }

  // Geri tuşu: önce demo ya da panel kapanır; giriş ekranında TV'nin ana ekranına dönülür.
  function goBack() {
    if (demoRunning()) return stopDemo();
    if ($('panel').className !== 'hidden') return hidePanel();
    platform.exit();
  }

  // --- Panel, menü ve kumanda ---

  // "1–4: HDMI girişi · 0: …" satırını tuş başına bir satıra böler; tuş adı vurgu renginde.
  function formatKeys() {
    var el = $('keys');
    var parts = el.textContent.split(' · ');
    el.innerHTML = '';
    for (var i = 0; i < parts.length; i++) {
      var colon = parts[i].indexOf(':');
      var row = document.createElement('div');
      row.className = 'key';
      var key = document.createElement('b');
      key.textContent = colon > 0 ? parts[i].slice(0, colon) : '';
      row.appendChild(key);
      row.appendChild(document.createTextNode(colon > 0 ? parts[i].slice(colon + 1).trim() : parts[i]));
      el.appendChild(row);
    }
  }

  var menu = { open: false, index: 0, ip: null, msg: '', timer: null };
  // Altyazı ayarı ekranı: open, odaktaki satır, taşıma kipi ve adımı, sayı girişi, panelin kaydırma payı.
  var ed = { open: false, index: 0, move: false, step: 40, num: null, scroll: 0 };

  function setProvider(value) {
    provider = String(value).slice(0, 60);
    renderProvider();
  }

  function renderProvider() {
    var el = $('st-provider');
    el.textContent = provider || t('providerUnknown');
    el.className = provider ? '' : 'muted';
  }

  function renderLast() {
    var el = $('st-last');
    if (!lastSubText) { el.textContent = '-'; return; }
    el.textContent = lastSubText + ' · ' + Math.max(0, Math.round((Date.now() - lastSubAt) / 1000)) + ' ' + t('secondsAgo');
  }

  function renderStatus() {
    renderLast();
    renderConn();
    renderProvider();
  }

  // Panel açıkken bekleme kutusu kapanır: ikisi üst üste binince yazılar okunmuyor.
  // Menü açık değilken yalnız durum satırları kısa süre görünür (autoHide).
  function showPanel(autoHide) {
    if (menu.open) return;
    $('waiting').className = 'hidden';
    $('panel').className = 'info';
    renderStatus();
    clearTimeout(panelTimer);
    if (autoHide) panelTimer = setTimeout(hidePanel, 8000);
  }

  function hidePanel() {
    clearTimeout(panelTimer);
    if (ed.open) closeSettings(false);
    menu.open = false;
    menu.ip = null;
    clearInterval(menu.timer);
    menu.timer = null;
    $('panel').className = 'hidden';
    if (!server && !demoRunning()) $('waiting').className = '';
  }

  function openMenu() {
    if (menu.open) return;
    $('waiting').className = 'hidden';
    clearTimeout(panelTimer);
    menu.open = true;
    menu.index = 0;
    menu.ip = null;
    menu.msg = '';
    $('panel').className = 'menu';
    renderStatus();
    renderMenu();
    menu.timer = setInterval(renderLast, 1000);
  }

  // --- Ortak satır çizimi ---

  function renderRows(container, items, index) {
    container.innerHTML = '';
    for (var i = 0; i < items.length; i++) {
      var item = items[i];
      var row = document.createElement('div');
      row.className = 'mrow' + (item.cls ? ' ' + item.cls : '') + (i === index ? ' focused' : '') + (item.editing ? ' editing' : '');
      var label = document.createElement('span');
      label.className = 'mlabel';
      label.textContent = item.label;
      var value = document.createElement('span');
      value.className = 'mvalue';
      if (item.node) item.node(value); else value.textContent = item.value || '';
      row.appendChild(label);
      row.appendChild(value);
      container.appendChild(row);
    }
  }

  function cycle(list, current, dir) {
    var at = -1;
    for (var i = 0; i < list.length; i++) if (list[i] === current) at = i;
    return list[(at + dir + list.length * 2) % list.length];
  }

  function wrap(index, dir, length) { return (index + dir + length) % length; }

  // --- Ana menü ---

  function toggleRun() {
    if (stopped) startSubtitles(); else stopSubtitles();
  }

  // Altyazıları durdurur: bağlantı kapanır, tarama durur, ekrandaki yazı kalkar. Kaydedilmez.
  function stopSubtitles() {
    stopped = true;
    if (eventSource) { eventSource.close(); eventSource = null; }
    cancelScan();
    clearTimeout(dropTimer);
    dropTimer = null;
    stopSpeech();
    hideScreen();
    if (demoRunning()) stopDemo();
    hideSubtitle('stopped');
    connState = 'stopped';
    renderConn();
  }

  function startSubtitles() {
    stopped = false;
    var address = server || load('server') || config.server;
    if (address) {
      connect(address);
    } else {
      connState = 'none';
      renderConn();
      scanForServer();
    }
  }

  function rescan() {
    stopped = false;
    cancelScan();
    scanForServer();
    menu.msg = t('mScanStarted');
  }

  function findConsole() {
    save('hdmi', '');
    autoSelectConsole(function () {
      setStatus('st-hdmi', hdmiPort + ' (' + t('consoleNotFound') + ')', 'bad');
    });
  }

  // Sayı tuşu olmayan kumandalar için: menüde sol ve sağ HDMI girişini değiştirir.
  function stepHdmi(step) {
    hdmiPort = (hdmiPort + step + 3) % 4 + 1;
    save('hdmi', hdmiPort);
    showHdmi(hdmiPort);
  }

  function mainItems() {
    var ipItem = {
      label: t('mIp'),
      value: shortAddr(server) || '-',
      ok: startIpEdit
    };
    if (menu.ip) {
      ipItem.editing = true;
      ipItem.node = function (el) {
        for (var i = 0; i < 4; i++) {
          if (i) el.appendChild(document.createTextNode(' . '));
          var oct = document.createElement('span');
          oct.className = 'oct' + (i === menu.ip.pos ? ' sel' : '');
          oct.textContent = String(menu.ip.o[i]);
          el.appendChild(oct);
        }
      };
    }
    var items = [
      { label: t('mRun'), value: stopped ? t('mRunOff') : t('mRunOn'), ok: toggleRun },
      { label: t('mSettings'), value: '▶', ok: openSettings },
      { label: t('mTest'), value: testOn ? t('mOn') : t('mOff'), ok: toggleTest, left: toggleTest, right: toggleTest },
      { label: t('mInfoBoxes'), value: load('infoBoxes') === '0' ? t('mOff') : t('mOn'), ok: toggleInfoBoxes, left: toggleInfoBoxes, right: toggleInfoBoxes },
      { label: t('mPhoneMessages'), value: load('phoneMessages') === '0' ? t('mOff') : t('mOn'), ok: togglePhoneMessages, left: togglePhoneMessages, right: togglePhoneMessages },
      ipItem,
      { label: t('mScan'), value: scanning ? t('mScanStarted') : '', cls: 'action', ok: rescan },
      { label: t('mHdmi'), value: '◀ ' + hdmiPort + ' ▶', left: function () { stepHdmi(-1); }, right: function () { stepHdmi(1); }, ok: function () { stepHdmi(1); } },
      { label: t('mHdmiAuto'), cls: 'action', ok: findConsole }
    ];
    if (platform.exit) items.push({ label: t('mExit'), cls: 'action', ok: function () { platform.exit(); } });
    return items;
  }

  function hideRegionsByKey(key) {
    var names = [];
    for (var k in screenRegions) if (screenRegions.hasOwnProperty(k) && SUB.toggleKeyForRegion(k) === key) names.push(k);
    for (var i = 0; i < names.length; i++) hideScreen(names[i]);
  }

  function toggleInfoBoxes() {
    var on = load('infoBoxes') === '0';
    save('infoBoxes', on ? '1' : '0');
    if (!on) hideRegionsByKey('infoBoxes');
    renderMenu();
  }

  function togglePhoneMessages() {
    var on = load('phoneMessages') === '0';
    save('phoneMessages', on ? '1' : '0');
    if (!on) hideRegionsByKey('phoneMessages');
    renderMenu();
  }

  function renderMenu() {
    renderRows($('menu-list'), mainItems(), menu.index);
    var msg = $('menu-msg');
    msg.textContent = menu.ip ? t('ipHint') : (menu.msg || t('navHint'));
    msg.className = 'msg' + (menu.ip || !menu.msg ? ' hint' : '');
  }

  // Elle IPv4 girişi: dört bölüm; ◀▶ bölüm seçer, ▲▼ ±1, rakam tuşları yazar, Tamam bağlanır ve kaydeder.
  function startIpEdit() {
    var m = /(\d+)\.(\d+)\.(\d+)\.(\d+)/.exec(server || load('server') || '');
    var octets = m ? [Number(m[1]), Number(m[2]), Number(m[3]), Number(m[4])] : [192, 168, 1, 2];
    for (var i = 0; i < 4; i++) octets[i] = Math.max(0, Math.min(255, octets[i]));
    menu.ip = { o: octets, pos: 3, typed: '' };
  }

  function ipKey(code) {
    var ip = menu.ip;
    if (code === KEY.LEFT || code === KEY.RIGHT) {
      ip.pos = Math.max(0, Math.min(3, ip.pos + (code === KEY.RIGHT ? 1 : -1)));
      ip.typed = '';
    } else if (code === KEY.UP || code === KEY.DOWN) {
      ip.o[ip.pos] = (ip.o[ip.pos] + (code === KEY.UP ? 1 : 255)) % 256;
      ip.typed = '';
    } else if (code >= 48 && code <= 57) {
      ip.typed = (ip.typed.length >= 3 ? '' : ip.typed) + String(code - 48);
      var value = Math.min(255, parseInt(ip.typed, 10));
      ip.o[ip.pos] = value;
      if ((ip.typed.length >= 3 || value * 10 > 255) && ip.pos < 3) {
        ip.pos++;
        ip.typed = '';
      }
    } else if (code === KEY.OK) {
      var address = 'http://' + ip.o.join('.') + ':' + SERVER_PORT;
      menu.ip = null;
      save('server', address);
      connect(address);
      menu.msg = t('ipSaved') + shortAddr(address);
    } else if (code === KEY.BACK) {
      menu.ip = null;
    } else {
      return false;
    }
    return true;
  }

  // Dönüş değeri: tuş menüye aitse true (sayfanın varsayılan davranışı engellenir).
  function menuKey(code) {
    menu.msg = '';
    if (menu.ip && ipKey(code)) {
      renderMenu();
      return true;
    }
    var items = mainItems();
    var item = items[menu.index];
    var used = true;
    if (code === KEY.UP || code === KEY.DOWN) {
      menu.index = wrap(menu.index, code === KEY.DOWN ? 1 : -1, items.length);
    } else if (code === KEY.LEFT && item.left) {
      item.left();
    } else if (code === KEY.RIGHT && item.right) {
      item.right();
    } else if (code === KEY.LEFT || code === KEY.RIGHT) {
      // bu satırda değişecek bir şey yok
    } else if (code === KEY.OK) {
      if (item.ok) item.ok();
    } else if (code === KEY.BACK || code === KEY.GREEN) {
      hidePanel();
      return true;
    } else if (code === KEY.RED) {
      toggleTest();
    } else if (code === KEY.BLUE) {
      if (demoRunning()) stopDemo(); else startDemo();
      if (!menu.open) return true;
    } else if (code === 48) {
      findConsole();
    } else if (code >= 49 && code <= 52) {
      hdmiPort = code - 48;
      save('hdmi', hdmiPort);
      showHdmi(hdmiPort);
    } else {
      used = false;
    }
    if (menu.open && !ed.open) renderMenu();
    return used;
  }

  // --- "Altyazı Konumu ve Görünümü" ekranı ---

  function openSettings() {
    endMacPreview();
    ed.open = true;
    ed.index = 0;
    ed.move = false;
    ed.num = null;
    ed.scroll = 0;
    draft = SUB.sanitize(subSettings, pageW(), pageH());
    $('panel').className = 'hidden';
    $('settings-panel').className = '';
    refreshSample();
    redraw(false);
  }

  // Kaydet ya da vazgeç sonrası: taslak atılır (kaydedildiyse subSettings zaten güncel), ana menüye dönülür.
  function closeSettings(toMenu) {
    if (ed.num) clearTimeout(ed.num.timer);
    ed.open = false;
    ed.move = false;
    ed.num = null;
    draft = null;
    $('settings-panel').className = 'hidden';
    layout();
    refreshSample();
    if (toMenu && menu.open) {
      $('panel').className = 'menu';
      renderStatus();
      renderMenu();
    }
  }

  function saveSettings() {
    subSettings = SUB.sanitize(draft, pageW(), pageH());
    save('subStyle', SUB.serialize(subSettings, pageW(), pageH()));
    reportLayout();
    menu.msg = t('sSaved');
    closeSettings(true);
  }

  // Yerleşimi yeniler; konum değişikliklerinde kutuyu ekrana sığacak aralığa çeker, sonra paneli altyazının öbür yarısına alır.
  function redraw(clampPos) {
    layout();
    if (clampPos) {
      var cp = SUB.clampPosition(draft, pageW(), pageH(), contentHeight);
      if (cp.x !== draft.x || cp.y !== draft.y) {
        draft.x = cp.x;
        draft.y = cp.y;
        layout();
      }
    }
    renderSettings();
  }

  function setField(key, value, clampPos) {
    draft[key] = value;
    draft = SUB.sanitize(draft, pageW(), pageH());
    redraw(clampPos === true);
  }

  function nudgeField(key, delta, clampPos) {
    setField(key, draft[key] + delta, clampPos);
  }

  function moveBy(dx, dy) {
    draft.x += dx * ed.step;
    draft.y += dy * ed.step;
    draft = SUB.sanitize(draft, pageW(), pageH());
    redraw(true);
  }

  function presetIndex(list) {
    for (var i = 0; i < list.length; i++) {
      if (list[i].x === draft.x && list[i].y === draft.y && list[i].align === draft.align) return i;
    }
    return -1;
  }

  function stepPreset(dir) {
    var list = SUB.presets(pageW(), pageH(), draft);
    var at = presetIndex(list);
    var next = list[at < 0 ? (dir > 0 ? 0 : list.length - 1) : wrap(at, dir, list.length)];
    draft.x = next.x;
    draft.y = next.y;
    draft.align = next.align;
    redraw(true);
  }

  function paletteIndex(list, value) {
    for (var i = 0; i < list.length; i++) if (list[i].value.toLowerCase() === String(value).toLowerCase()) return i;
    return -1;
  }

  function paletteName(entry) { return t('lang') === 'tr' ? entry.name : entry.nameEn; }

  function stepPalette(key, list, dir) {
    var at = paletteIndex(list, draft[key]);
    setField(key, list[wrap(at < 0 ? 0 : at, dir, list.length)].value);
  }

  function swatchNode(list, key) {
    return function (el) {
      var at = paletteIndex(list, draft[key]);
      var chip = document.createElement('span');
      chip.className = 'swatch';
      chip.style.background = draft[key];
      el.appendChild(chip);
      el.appendChild(document.createTextNode(at < 0 ? draft[key] : paletteName(list[at])));
    };
  }

  // Sayı satırı: ◀▶ adım kadar değiştirir; rakam tuşlarıyla doğrudan yazılabilir (Tamam ya da 1,5 sn sonra uygulanır).
  function numItem(labelKey, key, step, unit, clampPos) {
    var typing = ed.num && ed.num.key === key;
    return {
      label: t(labelKey),
      value: typing ? ed.num.text + '_' : draft[key] + ' ' + unit,
      editing: !!typing,
      numKey: key,
      left: function () { nudgeField(key, -step, clampPos); },
      right: function () { nudgeField(key, step, clampPos); }
    };
  }

  function settingsItems() {
    var presets = SUB.presets(pageW(), pageH(), draft);
    var pi = presetIndex(presets);
    var lang = t('lang');
    var outlineNames = { none: t('oNone'), shadow: t('oShadow'), outline: t('oOutline'), both: t('oBoth') };
    var alignNames = { left: t('aLeft'), center: t('aCenter'), right: t('aRight') };
    var items = [
      { label: t('sPreset'), value: pi < 0 ? '—' : (lang === 'tr' ? presets[pi].name : presets[pi].nameEn),
        left: function () { stepPreset(-1); }, right: function () { stepPreset(1); }, ok: function () { stepPreset(1); } },
      { label: t('sMove'), value: ed.move ? t('sMoveOn') : t('sMoveValue'), editing: ed.move,
        ok: function () { ed.move = true; renderSettings(); } },
      numItem('sX', 'x', 10, 'px', true),
      numItem('sY', 'y', 10, 'px', true),
      numItem('sFont', 'fontSize', SUB.RANGES.fontSize.step, 'px', false),
      numItem('sWidth', 'width', SUB.RANGES.width.step, 'px', false),
      { label: t('sTextColor'), node: swatchNode(SUB.TEXT_COLORS, 'textColor'),
        left: function () { stepPalette('textColor', SUB.TEXT_COLORS, -1); }, right: function () { stepPalette('textColor', SUB.TEXT_COLORS, 1); }, ok: function () { stepPalette('textColor', SUB.TEXT_COLORS, 1); } },
      { label: t('sBgColor'), node: swatchNode(SUB.BG_COLORS, 'bgColor'),
        left: function () { stepPalette('bgColor', SUB.BG_COLORS, -1); }, right: function () { stepPalette('bgColor', SUB.BG_COLORS, 1); }, ok: function () { stepPalette('bgColor', SUB.BG_COLORS, 1); } },
      numItem('sBgOpacity', 'bgOpacity', SUB.RANGES.bgOpacity.step, '%', false),
      { label: t('sOutline'), value: outlineNames[draft.outline],
        left: function () { setField('outline', cycle(SUB.OUTLINES, draft.outline, -1)); }, right: function () { setField('outline', cycle(SUB.OUTLINES, draft.outline, 1)); }, ok: function () { setField('outline', cycle(SUB.OUTLINES, draft.outline, 1)); } },
      { label: t('sAlign'), value: alignNames[draft.align],
        left: function () { setField('align', cycle(SUB.ALIGNS, draft.align, -1)); }, right: function () { setField('align', cycle(SUB.ALIGNS, draft.align, 1)); }, ok: function () { setField('align', cycle(SUB.ALIGNS, draft.align, 1)); } },
      { label: t('sBold'), value: draft.bold ? t('mOn') : t('mOff'),
        left: function () { setField('bold', !draft.bold); }, right: function () { setField('bold', !draft.bold); }, ok: function () { setField('bold', !draft.bold); } },
      numItem('sMargin', 'margin', SUB.RANGES.margin.step, 'px', true),
      { label: t('sSource'), value: draft.positionSource === 'tv' ? t('srcTv') : t('srcPc'),
        left: function () { setField('positionSource', cycle(SUB.SOURCES, draft.positionSource, -1)); }, right: function () { setField('positionSource', cycle(SUB.SOURCES, draft.positionSource, 1)); }, ok: function () { setField('positionSource', cycle(SUB.SOURCES, draft.positionSource, 1)); } },
      { label: t('sSave'), cls: 'action', ok: saveSettings },
      { label: t('sCancel'), cls: 'action', ok: function () { closeSettings(true); } },
      { label: t('sReset'), cls: 'action', ok: function () { draft = SUB.defaults(pageW(), pageH()); ed.move = false; redraw(false); } }
    ];
    return items;
  }

  function renderSettings() {
    var items = settingsItems();
    var list = $('settings-list');
    var panel = $('settings-panel');
    var keep = list.scrollTop;
    list.style.height = 'auto';
    renderRows(list, items, ed.index);

    // Bilgi satırları
    var info = $('settings-info');
    info.innerHTML = '';
    var line = document.createElement('div');
    line.textContent = t('sInfo').replace('{h}', String(pageH()));
    info.appendChild(line);
    if (draft.positionSource === 'pc') {
      var note = document.createElement('div');
      note.textContent = t('sPcNote');
      info.appendChild(note);
    }
    if (ed.move) {
      var live = document.createElement('div');
      live.className = 'live';
      live.textContent = t('sMoveHint').replace('{step}', ed.step === 2 ? t('sStepFine') : t('sStepFast'));
      info.appendChild(live);
    }
    $('settings-hint').textContent = ed.num ? t('sTyping') : t('navHint');

    // Panel, altyazının olmadığı yarıya alınır; satırlar sığmazsa liste kaydırılır.
    var sub = $('subtitle').getBoundingClientRect();
    var vh = pageH();
    var above = sub.top - 16, below = vh - sub.bottom - 16;
    var space;
    if (above >= below) {
      panel.style.top = '16px';
      panel.style.bottom = 'auto';
      space = above;
    } else {
      panel.style.top = 'auto';
      panel.style.bottom = '16px';
      space = below;
    }
    space = Math.max(220, space);
    var overhead = panel.offsetHeight - list.offsetHeight;
    list.style.height = Math.max(120, Math.min(list.scrollHeight, space - overhead)) + 'px';
    list.scrollTop = keep;
    var row = list.childNodes[ed.index];
    if (row) {
      if (row.offsetTop < list.scrollTop) list.scrollTop = row.offsetTop - 4;
      else if (row.offsetTop + row.offsetHeight > list.scrollTop + list.clientHeight) list.scrollTop = row.offsetTop + row.offsetHeight - list.clientHeight + 4;
    }
  }

  function commitNum() {
    if (!ed.num) return;
    clearTimeout(ed.num.timer);
    var key = ed.num.key;
    var value = parseInt(ed.num.text, 10);
    ed.num = null;
    if (isNaN(value)) return renderSettings();
    setField(key, value, key === 'x' || key === 'y' || key === 'margin');
  }

  function typeDigit(item, digit) {
    var key = item.numKey;
    if (!ed.num || ed.num.key !== key) {
      if (ed.num) commitNum();
      ed.num = { key: key, text: '', timer: null };
    }
    clearTimeout(ed.num.timer);
    if (ed.num.text.length >= 4) ed.num.text = '';
    ed.num.text += String(digit);
    ed.num.timer = setTimeout(commitNum, 1500);
    renderSettings();
  }

  function moveKey(code) {
    if (code === KEY.LEFT) moveBy(-1, 0);
    else if (code === KEY.RIGHT) moveBy(1, 0);
    else if (code === KEY.UP) moveBy(0, -1);
    else if (code === KEY.DOWN) moveBy(0, 1);
    else if (code === KEY.OK) { ed.step = ed.step === 2 ? 40 : 2; renderSettings(); }
    else if (code === KEY.BACK) { ed.move = false; renderSettings(); }
    else return false;
    return true;
  }

  function settingsKey(code) {
    if (ed.move) return moveKey(code);
    var digit = code >= 48 && code <= 57;
    if (ed.num) {
      if (digit) { typeDigit(settingsItems()[ed.index], code - 48); return true; }
      if (code === KEY.BACK) { clearTimeout(ed.num.timer); ed.num = null; renderSettings(); return true; }
      commitNum();
      if (code === KEY.OK) return true;
    }
    var items = settingsItems();
    var item = items[ed.index];
    if (digit) {
      if (!item.numKey) return false;
      typeDigit(item, code - 48);
      return true;
    }
    if (code === KEY.UP || code === KEY.DOWN) {
      ed.index = wrap(ed.index, code === KEY.DOWN ? 1 : -1, items.length);
      renderSettings();
    } else if (code === KEY.LEFT || code === KEY.RIGHT) {
      var action = code === KEY.LEFT ? item.left : item.right;
      if (action) action(); else renderSettings();
    } else if (code === KEY.OK) {
      if (item.ok) item.ok(); else renderSettings();
    } else if (code === KEY.BACK) {
      closeSettings(true);
    } else {
      return false;
    }
    return true;
  }

  document.addEventListener('keydown', function (e) {
    var code = e.keyCode;
    if (menu.open) {
      if (ed.open ? settingsKey(code) : menuKey(code)) e.preventDefault();
      return;
    }
    if (code === 48) { // 0: elle seçimi unut, konsolu yeniden ara
      findConsole();
      showPanel(true);
    } else if (code >= 49 && code <= 52) { // 1–4
      hdmiPort = code - 48;
      save('hdmi', hdmiPort);
      showHdmi(hdmiPort);
      showPanel(true);
    } else if (code === KEY.RED) {
      toggleTest();
    } else if (code === KEY.GREEN) {
      openMenu();
    } else if (code === KEY.BLUE) {
      if (demoRunning()) stopDemo(); else startDemo();
    } else if (code === KEY.OK && $('waiting').className !== 'hidden') {
      startDemo();
    } else if (code === KEY.OK && !demoRunning()) {
      openMenu();
    } else if (code === KEY.BACK) {
      goBack();
      e.preventDefault();
    }
    // Oklar menü kapalıyken hiçbir şey yapmaz ve engellenmez; kullanılmayan tuşlara dokunulmaz.
  });

  window.addEventListener('resize', function () {
    if ($('subtitle').className !== 'hidden') layout();
    if (ed.open) renderSettings();
  });

  // Uygulama zaten açıkken yeniden başlatılırsa (ör. GameTranslator onu öne getirdiğinde) parametreler burada gelir.
  platform.onRelaunch(function (raw) { applyParams(parseParams(raw)); });

  platform.init();
  window.applyI18n();
  formatKeys();
  subSettings = loadSubSettings();
  renderProvider();
  var savedBottom = load('bottom') || load('appBottom');
  if (savedBottom) setBottom(Number(savedBottom));
  showHdmi(hdmiPort);
  if (!savedPort) autoSelectConsole();

  $('app-version').textContent = APP_VERSION;
  renderDiag();
  var initial = parseParams(platform.launchParams());
  var address = initial.server || load('server') || config.server;
  applyParams(initial);
  if (!server) {
    if (address) connect(address); else $('waiting').className = '';
  }
  if (!server) scanForServer();
  // Bekleme ekranı açıksa panel kendiliğinden açılmaz; yeşil tuşla açılabilir.
  if (server) showPanel(true); else hidePanel();
})();
