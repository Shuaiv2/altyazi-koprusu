// SPDX-License-Identifier: AGPL-3.0-only
// TV altyazı ayarları: saf hesap modülü (DOM'a dokunmaz, tarayıcıda ve jsc'de çalışır). ES5.
// Bütün uzunluklar "sayfa pikseli"dir: genişlik 1920, yükseklik window.innerHeight. Panel pikseli ya da dp değildir.
(function (root) {
  'use strict';

  var VERSION = 1;
  var DEFAULT_VW = 1920;
  var DEFAULT_VH = 1080;

  // Ayarların sınırları (min, max, tarayıcıdaki adım).
  var RANGES = {
    fontSize: { min: 20, max: 120, step: 2 },
    width: { min: 300, max: 1920, step: 20 },
    margin: { min: 0, max: 200, step: 4 },
    bgOpacity: { min: 0, max: 100, step: 5 }
  };

  var TEXT_COLORS = [
    { id: 'white', value: '#FFFFFF', name: 'Beyaz', nameEn: 'White' },
    { id: 'yellow', value: '#FFE600', name: 'Sarı', nameEn: 'Yellow' },
    { id: 'lightblue', value: '#8FD8FF', name: 'Açık mavi', nameEn: 'Light blue' },
    { id: 'green', value: '#7CFC8A', name: 'Yeşil', nameEn: 'Green' },
    { id: 'orange', value: '#FFA64D', name: 'Turuncu', nameEn: 'Orange' },
    { id: 'pink', value: '#FF9FD0', name: 'Pembe', nameEn: 'Pink' }
  ];

  var BG_COLORS = [
    { id: 'black', value: '#000000', name: 'Siyah', nameEn: 'Black' },
    { id: 'darkgray', value: '#1E1E1E', name: 'Koyu gri', nameEn: 'Dark gray' },
    { id: 'navy', value: '#0B1B3A', name: 'Lacivert', nameEn: 'Navy' }
  ];

  var OUTLINES = ['none', 'shadow', 'outline', 'both'];
  var ALIGNS = ['left', 'center', 'right'];
  var SOURCES = ['tv', 'pc'];

  function num(v, fallback) {
    var n = typeof v === 'string' && v.replace(/\s/g, '') === '' ? NaN : Number(v);
    return (typeof n === 'number' && isFinite(n)) ? n : fallback;
  }

  function clamp(v, lo, hi) {
    if (lo > hi) return hi;
    return Math.max(lo, Math.min(hi, v));
  }

  function isHex(v) { return typeof v === 'string' && /^#[0-9a-fA-F]{6}$/.test(v); }

  function pick(list, v, fallback) {
    for (var i = 0; i < list.length; i++) if (list[i] === v) return v;
    return fallback;
  }

  function dims(vw, vh) {
    return { w: num(vw, DEFAULT_VW) > 0 ? num(vw, DEFAULT_VW) : DEFAULT_VW, h: num(vh, DEFAULT_VH) > 0 ? num(vh, DEFAULT_VH) : DEFAULT_VH };
  }

  // Alt orta, kenar boşluğunun üstünde; eski %7 alt konumuna yakın.
  function defaults(vw, vh) {
    var d = dims(vw, vh);
    return {
      x: Math.round(d.w / 2),
      y: Math.round(d.h - d.h * 0.07),
      width: 1400,
      fontSize: 44,
      margin: 48,
      textColor: '#FFFFFF',
      bgColor: '#000000',
      bgOpacity: 60,
      outline: 'shadow',
      align: 'center',
      bold: true,
      positionSource: 'tv',
      version: VERSION
    };
  }

  // Bilinmeyen ya da geçersiz alan varsayılana döner; sayılar sınırlara çekilir.
  function sanitize(obj, vw, vh) {
    var d = dims(vw, vh);
    var def = defaults(d.w, d.h);
    var o = (obj && typeof obj === 'object') ? obj : {};
    var out = {};
    out.x = Math.round(clamp(num(o.x, def.x), 0, d.w));
    out.y = Math.round(clamp(num(o.y, def.y), 0, d.h));
    out.width = Math.round(clamp(num(o.width, def.width), RANGES.width.min, Math.max(RANGES.width.min, Math.min(RANGES.width.max, d.w))));
    out.fontSize = Math.round(clamp(num(o.fontSize, def.fontSize), RANGES.fontSize.min, RANGES.fontSize.max));
    out.margin = Math.round(clamp(num(o.margin, def.margin), RANGES.margin.min, RANGES.margin.max));
    out.textColor = isHex(o.textColor) ? o.textColor.toUpperCase() : def.textColor;
    out.bgColor = isHex(o.bgColor) ? o.bgColor.toUpperCase() : def.bgColor;
    out.bgOpacity = Math.round(clamp(num(o.bgOpacity, def.bgOpacity), RANGES.bgOpacity.min, RANGES.bgOpacity.max));
    out.outline = pick(OUTLINES, o.outline, def.outline);
    out.align = pick(ALIGNS, o.align, def.align);
    out.bold = typeof o.bold === 'boolean' ? o.bold : def.bold;
    out.positionSource = pick(SOURCES, o.positionSource, def.positionSource);
    out.version = VERSION;
    return out;
  }

  // Geçersiz JSON'da null döner (çağıran varsayılana ya da eski ayara düşer).
  function parse(json, vw, vh) {
    if (typeof json !== 'string' || !json) return null;
    var obj;
    try { obj = JSON.parse(json); } catch (e) { return null; }
    if (!obj || typeof obj !== 'object') return null;
    return sanitize(obj, vw, vh);
  }

  function serialize(s, vw, vh) {
    return JSON.stringify(sanitize(s, vw, vh));
  }

  // Eski "gametranslator.bottom" anahtarı: ekranın altından yüzde. Yeni y, kutunun alt kenarının sayfa üstünden uzaklığıdır.
  function migrate(oldBottomPercent, vh) {
    var d = dims(DEFAULT_VW, vh);
    var s = defaults(d.w, d.h);
    var pct = num(oldBottomPercent, NaN);
    if (!isFinite(pct)) return s;
    pct = clamp(pct, 0, 92);
    s.y = Math.round(d.h - d.h * pct / 100);
    return sanitize(s, d.w, d.h);
  }

  // Hazır konumlar. s verilirse sol ve sağ konumlarda kutunun kenarı kenar boşluğuna oturur.
  function presets(vw, vh, s) {
    var d = dims(vw, vh);
    var margin = s && isFinite(num(s.margin, NaN)) ? s.margin : 48;
    var width = Math.min(s && isFinite(num(s.width, NaN)) ? s.width : 1400, d.w - 2 * margin);
    var half = Math.round(width / 2);
    var bottomY = Math.round(d.h - d.h * 0.07);
    return [
      { id: 'bottomCenter', name: 'Alt orta', nameEn: 'Bottom center', x: Math.round(d.w / 2), y: bottomY, align: 'center' },
      { id: 'topCenter', name: 'Üst orta', nameEn: 'Top center', x: Math.round(d.w / 2), y: Math.round(margin + d.h * 0.16), align: 'center' },
      { id: 'bottomLeft', name: 'Alt sol', nameEn: 'Bottom left', x: margin + half, y: bottomY, align: 'left' },
      { id: 'bottomRight', name: 'Alt sağ', nameEn: 'Bottom right', x: d.w - margin - half, y: bottomY, align: 'right' },
      { id: 'middle', name: 'Ekran ortası', nameEn: 'Screen middle', x: Math.round(d.w / 2), y: Math.round(d.h / 2 + d.h * 0.06), align: 'center' }
    ];
  }

  // Kutunun sayfadaki yeri. Sığıyorsa [margin, vw-margin] x [margin, vh-margin] dışına çıkmaz.
  // İçerik dikeyde sığmıyorsa top = margin olur (çağıran yazıyı küçültür).
  function computeRect(s, vw, vh, contentHeight) {
    var d = dims(vw, vh);
    var margin = Math.max(0, Math.min(num(s.margin, 48), Math.floor(d.w / 2) - 1));
    var ch = Math.max(0, num(contentHeight, 0));
    var width = Math.min(num(s.width, 1400), d.w - 2 * margin);
    var left = clamp(num(s.x, d.w / 2) - width / 2, margin, d.w - margin - width);
    var top, bottom;
    if (ch > d.h - 2 * margin) {
      top = margin;
      bottom = margin + ch;
    } else {
      bottom = clamp(num(s.y, d.h), margin + ch, d.h - margin);
      top = bottom - ch;
    }
    return { left: Math.round(left), top: Math.round(top), width: Math.round(width), bottom: Math.round(bottom) };
  }

  // x ve y'yi kutu ekranda tam sığacak aralığa çeker (taşıma ve sayı girişi için).
  function clampPosition(s, vw, vh, contentHeight) {
    var d = dims(vw, vh);
    var margin = Math.max(0, Math.min(num(s.margin, 48), Math.floor(d.w / 2) - 1));
    var width = Math.min(num(s.width, 1400), d.w - 2 * margin);
    var ch = Math.max(0, num(contentHeight, 0));
    var half = width / 2;
    return {
      x: Math.round(clamp(num(s.x, d.w / 2), margin + half, d.w - margin - half)),
      y: Math.round(clamp(num(s.y, d.h), Math.min(margin + ch, d.h - margin), d.h - margin))
    };
  }

  // Eski bir sonucun geç gelmesi: at (gönderenin epoch ms) son gösterilenden küçükse atılır.
  // state: { lastAt: number, shownText: string|null }. Durumu değiştirmez; çağıran günceller.
  function acceptSubtitle(state, payload) {
    if (!payload || typeof payload.text !== 'string' || !payload.text) return 'drop';
    var last = state && typeof state.lastAt === 'number' ? state.lastAt : 0;
    if (typeof payload.at === 'number' && isFinite(payload.at) && payload.at < last) return 'drop';
    if (state && typeof state.shownText === 'string' && state.shownText === payload.text) return 'extend';
    return 'show';
  }

  // '#rrggbb' + 0–100 saydamlık -> 'rgba(r, g, b, a)'
  function rgba(hex, opacityPercent) {
    var h = isHex(hex) ? hex : '#000000';
    var a = clamp(num(opacityPercent, 100), 0, 100) / 100;
    return 'rgba(' + parseInt(h.substr(1, 2), 16) + ', ' + parseInt(h.substr(3, 2), 16) + ', ' + parseInt(h.substr(5, 2), 16) + ', ' + a + ')';
  }

  root.GT_SUBSETTINGS = {
    VERSION: VERSION,
    RANGES: RANGES,
    TEXT_COLORS: TEXT_COLORS,
    BG_COLORS: BG_COLORS,
    OUTLINES: OUTLINES,
    ALIGNS: ALIGNS,
    SOURCES: SOURCES,
    defaults: defaults,
    sanitize: sanitize,
    parse: parse,
    serialize: serialize,
    migrate: migrate,
    presets: presets,
    computeRect: computeRect,
    clampPosition: clampPosition,
    acceptSubtitle: acceptSubtitle,
    rgba: rgba
  };
})(typeof window !== 'undefined' ? window : this);
