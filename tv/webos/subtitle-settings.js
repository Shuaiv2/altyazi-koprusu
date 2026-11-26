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

  // Bilgi kutusu çevirisi: oyunun İngilizce kutusunun HEMEN ALTINA konur. box/line normalleştirilmiş (0..1).
  // Genişlik = max(kutu genişliği, ekranın %22'si), ekran içine (margin) sıkıştırılır; boşluk = 0.6 x satır yüksekliği.
  // contentHeight verilirse ve alta sığmıyorsa kutunun ÜSTÜNE konur (üstte de yer yoksa eski gibi yukarı kayar, kutunun altından yukarı çıkmaz).
  // Dönüş: { left, top, width, maxHeight, gap, placed } (px); placed 'below' | 'above'; maxHeight = kalan yükseklik.
  function belowRect(box, line, vw, vh, margin, contentHeight) {
    var w = Math.max(1, num(vw, 1920)), h = Math.max(1, num(vh, 1080));
    var m = Math.max(0, Math.min(num(margin, 48), Math.floor(w / 2) - 1));
    var ln = num(line, 0.03);
    if (!(ln > 0)) ln = 0.03;
    var gap = Math.round(0.6 * ln * h);
    var boxW = Math.max(0, (num(box.right, 0) - num(box.left, 0)) * w);
    var width = Math.min(Math.max(boxW, 0.22 * w), w - 2 * m);
    var left = clamp(num(box.left, 0) * w, m, w - m - width);
    var boxBottom = num(box.bottom, 0) * h;
    var boxTop = num(box.top, 0) * h;
    var top = boxBottom + gap;
    var ch = Math.max(0, num(contentHeight, 0));
    var placed = 'below';
    var maxH = null;
    if (ch > 0 && top + ch > h - m) {
      var aboveTop = boxTop - gap - ch;
      if (aboveTop >= m) {
        top = aboveTop;
        placed = 'above';
        maxH = boxTop - gap - m;
      } else {
        top = Math.max(boxBottom, h - m - ch);
      }
    }
    top = Math.min(top, Math.max(boxBottom, h - m - 1));
    if (placed === 'above') top = Math.min(top, h);
    return { left: Math.round(left), top: Math.round(top), width: Math.round(width), maxHeight: Math.max(0, Math.round(maxH === null ? h - m - top : maxH)), gap: gap, placed: placed };
  }

  // Bilgi kutusunun SOLUNA: üst kenarı kutunun üstüyle hizalı, genişlik kuralı belowRect ile aynı.
  // Solda yer yoksa (box.left*w - gap < width + margin) belowRect'e (o da sığmazsa üste) düşer.
  // Dönüş belowRect ile aynı biçimde; placed 'left' | 'below' | 'above'.
  function leftRect(box, line, vw, vh, margin, contentHeight) {
    var w = Math.max(1, num(vw, 1920)), h = Math.max(1, num(vh, 1080));
    var m = Math.max(0, Math.min(num(margin, 48), Math.floor(w / 2) - 1));
    var ln = num(line, 0.03);
    if (!(ln > 0)) ln = 0.03;
    var gap = Math.round(0.6 * ln * h);
    var boxW = Math.max(0, (num(box.right, 0) - num(box.left, 0)) * w);
    var width = Math.min(Math.max(boxW, 0.22 * w), w - 2 * m);
    var boxLeft = num(box.left, 0) * w;
    if (boxLeft - gap < width + m) return belowRect(box, line, vw, vh, margin, contentHeight);
    var ch = Math.max(0, num(contentHeight, 0));
    var top = clamp(num(box.top, 0) * h, m, Math.max(m, h - m - ch));
    return { left: Math.round(boxLeft - gap - width), top: Math.round(top), width: Math.round(width), maxHeight: Math.max(0, Math.round(h - m - top)), gap: gap, placed: 'left' };
  }

  // Türkçe metin İngilizce kutunun TAM ÜSTÜNÜ örter: left = box.left, top = box.top, genişlik = max(kutu, %18 ekran) ekran içine sıkıştırılır.
  // İçerik kutudan uzunsa aşağı uzar; alt kenar boşluğuna taşacaksa alt kenar kutunun altında kalacak şekilde YUKARI uzar.
  // Dönüş: { left, top, width, height, maxHeight, placed:'over' } (px); height = kutu yüksekliği (en az), maxHeight = sığabilecek en çok yükseklik.
  function overRect(box, line, vw, vh, margin, contentHeight) {
    var w = Math.max(1, num(vw, 1920)), h = Math.max(1, num(vh, 1080));
    var m = Math.max(0, Math.min(num(margin, 48), Math.floor(w / 2) - 1));
    var boxW = Math.max(0, (num(box.right, 0) - num(box.left, 0)) * w);
    var width = Math.min(Math.max(boxW, 0.18 * w), w - 2 * m);
    var left = clamp(num(box.left, 0) * w, m, w - m - width);
    // Dikeyde örtmek esastır: İngilizce kutu zaten kenar boşluğuna taşabilir (alt satır y≈0.977), bu yüzden dikey pay en çok 8 px.
    var vm = Math.min(m, 8);
    var boxTop = num(box.top, 0) * h;
    var boxBottom = Math.max(boxTop, num(box.bottom, 0) * h);
    var boxH = boxBottom - boxTop;
    var ch = Math.max(boxH, num(contentHeight, 0));
    var top = boxTop;
    if (top + ch > h - vm) top = Math.max(vm, boxBottom - ch);
    if (top + ch > h - vm) top = Math.max(vm, h - vm - ch);
    var maxH = Math.max(h - vm - boxTop, boxBottom - vm);
    return { left: Math.round(left), top: Math.round(top), width: Math.round(width), height: Math.round(boxH), maxHeight: Math.max(0, Math.round(maxH)), placed: 'over' };
  }

  // "over" yazı boyu: İngilizce satır yüksekliğinden (0..1) türetilir, 18 ile kullanıcının altyazı boyu arasına sıkışır.
  function overFontSize(line, vh, userFont) {
    var ln = num(line, 0.03);
    if (!(ln > 0)) ln = 0.03;
    var px = Math.round(0.75 * ln * Math.max(1, num(vh, 1080)));
    return clamp(px, 18, Math.max(18, num(userFont, 44)));
  }

  // "over" kartının teması: İngilizce arayüz öğesinin parlaklığı. Yok ya da geçersizse koyu.
  function overTheme(item) {
    return item && item.theme === 'light' ? 'light' : 'dark';
  }

  // Kart rengi: açık tema neredeyse beyaz, koyu tema yarı opak siyah. Kullanıcının zemin saydamlığı altındaki İngilizceyi
  // göstermeyecek kadar saydam olamaz (alfa en az 0.92); yüksek ayar tema alfasını artırabilir.
  function overCardStyle(theme, bgOpacity) {
    var light = theme === 'light';
    var base = light ? 0.98 : 0.93;
    var user = clamp(num(bgOpacity, 0), 0, 100) / 100;
    var alpha = Math.min(1, Math.max(0.92, base, user));
    alpha = Math.round(alpha * 100) / 100;
    return {
      theme: light ? 'light' : 'dark',
      alpha: alpha,
      background: light ? 'rgba(246, 246, 248, ' + alpha + ')' : 'rgba(14, 14, 18, ' + alpha + ')',
      color: light ? '#15161a' : '#ffffff',
      boxShadow: light ? '0 2px 10px rgba(0, 0, 0, 0.28)' : '0 2px 10px rgba(0, 0, 0, 0.45)',
      textShadow: light ? 'none' : '0 1px 2px rgba(0, 0, 0, 0.6)'
    };
  }

  // Kartın metin parçaları: konuşmacı (varsa) ayrı satır; metin "\n" ile bölünür. Açıklamada (konuşmacı yokken, birden çok parça)
  // ilk parça başlıktır ve kalın yazılır.
  function overParts(item) {
    var speaker = item && typeof item.speaker === 'string' ? item.speaker.replace(/^\s+|\s+$/g, '') : '';
    var raw = item && typeof item.text === 'string' ? item.text.split('\n') : [];
    var segs = [];
    for (var i = 0; i < raw.length; i++) {
      var t = raw[i].replace(/^\s+|\s+$/g, '');
      if (t) segs.push(t);
    }
    return { speaker: speaker, segments: segs, titleBold: !speaker && segs.length > 1 };
  }

  // Kartın en çok büyüyebileceği yükseklik: İngilizce kutunun 1.6 katı (en az kutu kadar), ekran sınırını da aşmaz (rectMaxHeight).
  function overMaxGrow(boxHeightPx, rectMaxHeight) {
    var bh = Math.max(0, num(boxHeightPx, 0));
    var cap = Math.max(0, num(rectMaxHeight, bh * 1.6));
    return Math.max(0, Math.min(Math.round(bh * 1.6), cap));
  }

  // Sığdırma kararı: 'ok' (sığdı), 'shrink' (yazıyı küçült) ya da 'min' (en küçük boya ulaşıldı, daha fazlası yok).
  function overFit(contentHeight, limitHeight, size, minSize) {
    if (num(contentHeight, 0) <= num(limitHeight, 0)) return 'ok';
    return size > num(minSize, 18) ? 'shrink' : 'min';
  }

  function isOverItem(item) {
    return !!item && typeof item === 'object' && item.placement === 'over';
  }


  // --- "Ayna" (oyunun kendi arayüzü gibi) yardımcıları ---
  // '#rrggbb' dışındaki renkler reddedilir (satır içi stile güvenle yazılır); geçersizse fallback döner.
  function hexColor(value, fallback) {
    return typeof value === 'string' && /^#[0-9a-fA-F]{6}$/.test(value) ? value : fallback;
  }

  // Düğme istemi: tek başına Türkçe yazı (zemin yok), İngilizce istemin SOLUNA konur.
  function isPromptItem(item) {
    return isLabelItem(item) && item.style === 'prompt';
  }

  // Aynalı kart: "over" öğesi mirror:true ve geçerli bg ile gelirse oyun kartının kopyası çizilir.
  function isMirrorItem(item) {
    return isOverItem(item) && item.mirror === true && hexColor(item.bg, null) !== null;
  }

  function rectsOverlap(a, b, pad) {
    var p = num(pad, 0);
    return a.left < b.left + b.width + p && b.left < a.left + a.width + p && a.top < b.top + b.height + p && b.top < a.top + a.height + p;
  }

  // Düğme isteminin yeri (px): önce kutunun soluna (sağ kenar = box.left - %0.6 genişlik, dikeyde ortalı); yer yoksa kutunun
  // hemen üstüne, sağ kenar box.right'a hizalı. Önceki istemlerle (taken) çakışırsa yukarı kaydırılır. Dönüş {left, top, placed}.
  function promptPlace(box, tw, th, vw, vh, taken) {
    var gap = Math.round(0.006 * vw);
    var cy = (num(box.top, 0) + num(box.bottom, 0)) / 2 * vh;
    var left = num(box.left, 0) * vw - gap - tw;
    var top = cy - th / 2;
    var placed = 'left';
    if (left < 0.01 * vw) {
      placed = 'above';
      left = num(box.right, 0) * vw - tw;
      top = num(box.top, 0) * vh - th - 2;
    }
    left = clamp(left, 0, Math.max(0, vw - tw));
    top = clamp(top, 0, Math.max(0, vh - th));
    var list = taken || [];
    for (var guard = 0; guard < 20; guard++) {
      var hit = null;
      for (var i = 0; i < list.length; i++) {
        if (rectsOverlap({ left: left, top: top, width: tw, height: th }, list[i], 0)) { hit = list[i]; break; }
      }
      if (!hit) break;
      top = Math.max(0, hit.top - th - 1);
    }
    return { left: Math.round(left), top: Math.round(top), placed: placed };
  }

  // Aynalı kartın yeri: kutunun üstü (alt kenar = box.top - gap, üst kenar >= %3), olmazsa altı (alt kenar <= ekranın %98'i);
  // iki durumda da başka kutularla (obstacles: [{left, top, width, height}]) çakışmamalı. Sığmazsa null (kutuyu örtme).
  function mirrorPlace(box, cw, ch, vw, vh, obstacles) {
    var gap = Math.round(0.008 * vh);
    var left = clamp(num(box.left, 0) * vw, 0.01 * vw, Math.max(0.01 * vw, vw - 0.01 * vw - cw));
    var obs = obstacles || [];
    var tries = [
      { placed: 'above', top: num(box.top, 0) * vh - gap - ch },
      { placed: 'below', top: num(box.bottom, 0) * vh + gap }
    ];
    for (var t = 0; t < tries.length; t++) {
      var top = tries[t].top;
      if (top < 0.03 * vh || top + ch > 0.98 * vh) continue;
      var r = { left: left, top: top, width: cw, height: ch };
      var clash = false;
      for (var i = 0; i < obs.length; i++) if (rectsOverlap(r, obs[i], 0)) { clash = true; break; }
      if (!clash) return { left: Math.round(left), top: Math.round(top), placed: tries[t].placed };
    }
    return null;
  }

  // "beside": telefon sohbet paneli. Telefon kutusunun SOLUNA (sağ kenar = box.left*w - 24), yer yoksa SAĞINA konur;
  // ikisine de sığmazsa daha geniş olan yan seçilir ve ekran kenar boşluğuna sıkıştırılır. Genişlik min(0.24*w, 460),
  // üst kenar = box.top, en çok yükseklik = kutu yüksekliği (ekrana sıkıştırılır). placed: 'left' | 'right'.
  function besideRect(box, vw, vh, margin) {
    var w = Math.max(1, num(vw, 1920)), h = Math.max(1, num(vh, 1080));
    var m = Math.max(0, Math.min(num(margin, 48), Math.floor(w / 4)));
    var gap = 24;
    var width = Math.min(0.24 * w, 460);
    var bl = num(box.left, 0) * w, br = num(box.right, 0) * w;
    var vm = Math.min(m, 24);
    var boxTop = num(box.top, 0) * h;
    var boxH = Math.max(0, (num(box.bottom, 0) - num(box.top, 0)) * h);
    var maxH = Math.max(60, Math.min(boxH, h - 2 * vm));
    var top = clamp(boxTop, vm, Math.max(vm, h - vm - maxH));
    var leftRoom = bl - gap - m, rightRoom = w - m - (br + gap);
    var placed, left;
    if (leftRoom >= width) { placed = 'left'; left = bl - gap - width; }
    else if (rightRoom >= width) { placed = 'right'; left = br + gap; }
    else {
      placed = leftRoom >= rightRoom ? 'left' : 'right';
      width = Math.max(1, Math.min(width, Math.max(leftRoom, rightRoom)));
      left = placed === 'left' ? bl - gap - width : br + gap;
    }
    left = clamp(left, m, Math.max(m, w - m - width));
    return { left: Math.round(left), top: Math.round(top), width: Math.round(width), maxHeight: Math.round(maxH), placed: placed };
  }

  // Sohbet panel yazı boyu: 1080p'de 23 px, yükseklikle ölçeklenir, en az 18.
  function besideFontSize(vh) {
    return Math.max(18, Math.round(23 * Math.max(1, num(vh, 1080)) / 1080));
  }

  // Taşan sohbet: heights = balon yükseklikleri (eskiden yeniye), gap = balonlar arası boşluk, avail = balonlara ayrılan yükseklik.
  // Dönen indeks ilk görünen balondur; en eski balonlar atılır, en yeni (son) her zaman kalır.
  function besideFirstVisible(heights, gap, avail) {
    var n = (heights || []).length;
    if (n === 0) return 0;
    var g = Math.max(0, num(gap, 6)), used = 0, first = n;
    for (var i = n - 1; i >= 0; i--) {
      var add = Math.max(0, num(heights[i], 0)) + (first === n ? 0 : g);
      if (first !== n && used + add > avail) break;
      used += add;
      first = i;
    }
    return first;
  }

  // Geçerli iletiler: [{from:'them'|'me', text}] (boş metin atılır); from 'me' dışı 'them' sayılır.
  function besideMessages(item) {
    var out = [];
    var list = item && item.messages;
    if (!list || typeof list.length !== 'number') return out;
    for (var i = 0; i < list.length; i++) {
      var m = list[i];
      if (!m || typeof m.text !== 'string') continue;
      var t = m.text.replace(/^\s+|\s+$/g, '');
      if (t) out.push({ from: m.from === 'me' ? 'me' : 'them', text: t });
    }
    return out;
  }

  function isBesideItem(item) {
    return !!item && typeof item === 'object' && item.placement === 'beside' && besideMessages(item).length > 0;
  }

  // "label": araç bilgi paneli gibi arayüz satırlarında tek hücre. Hücre İngilizce yazının kutusunu yatayda 4 px, dikeyde 2 px
  // payla tam örter. Etiket sağa (align yok/"left"), değer sola ("right") uzayabilir; en geniş hücre İngilizce kutunun 2.2 katı
  // ya da (maxRight, normalize) verilmişse o kenara kadar. Yazı tek satır, boyu 0.8 x satır yüksekliği; sığana kadar küçülür.
  var LABEL_PAD_X = 4;
  var LABEL_PAD_Y = 2;
  var LABEL_WIDTH_FACTOR = 2.2;
  var LABEL_MIN_FONT = 10;

  function isLabelItem(item) {
    return !!item && typeof item === 'object' && item.placement === 'label' && typeof item.text === 'string' && item.text.length > 0;
  }

  function labelFontSize(line, vh) {
    return Math.max(LABEL_MIN_FONT, Math.round(0.8 * num(line, 0.03) * vh));
  }

  // Sığma kısıtlarını verir: {align, top, height, minWidth, maxWidth, anchor (sol hizada sol kenar, sağ hizada sağ kenar), fontSize}.
  function labelGeometry(item, box, vw, vh) {
    var bl = box.left * vw, br = box.right * vw, bt = box.top * vh, bb = box.bottom * vh;
    var bw = Math.max(1, br - bl);
    var right = !!item && item.align === 'right';
    var minWidth = bw + 2 * LABEL_PAD_X;
    var maxWidth = LABEL_WIDTH_FACTOR * bw + 2 * LABEL_PAD_X;
    var anchor;
    if (right) {
      anchor = Math.min(vw, br + LABEL_PAD_X);
      maxWidth = Math.min(maxWidth, anchor);
    } else {
      anchor = Math.max(0, bl - LABEL_PAD_X);
      if (item && typeof item.maxRight === 'number' && !isNaN(item.maxRight)) maxWidth = item.maxRight * vw - anchor;
      maxWidth = Math.min(maxWidth, vw - anchor);
    }
    maxWidth = Math.max(minWidth, maxWidth);
    var line = item && typeof item.line === 'number' && item.line > 0 ? item.line : (bb - bt) / vh;
    return {
      align: right ? 'right' : 'left', top: bt - LABEL_PAD_Y, height: (bb - bt) + 2 * LABEL_PAD_Y,
      minWidth: minWidth, maxWidth: maxWidth, anchor: anchor, fontSize: labelFontSize(line, vh)
    };
  }

  // Ölçülen yazı genişliğine (iç boşluk dahil) göre hücrenin yeri: {left, width}. Hücre en az İngilizce kutuyu örter.
  function labelPlace(g, contentWidth) {
    var width = Math.min(g.maxWidth, Math.max(g.minWidth, num(contentWidth, 0)));
    var left = g.align === 'right' ? Math.max(0, g.anchor - width) : g.anchor;
    return { left: Math.round(left), width: Math.round(width) };
  }

  function labelFits(g, contentWidth) {
    return num(contentWidth, 0) <= g.maxWidth + 0.5;
  }

  function labelStyle(theme) {
    if (theme === 'light') return { background: 'rgba(238, 240, 244, 0.97)', color: '#14161c' };
    return { background: 'rgba(52, 58, 74, 0.97)', color: '#ffffff' };
  }

  // "label" + style:"cell": harita/menü listesi satırı. Türkçe, İngilizce satırın TAM üstüne oyunun kendi renkleriyle çizilir
  // (zemin bg opak, yazı fg). Kutu her yandan ~%0.25 genişler; Türkçe uzunsa sağa maxRight'a kadar uzar, sonra yazı başlangıç
  // boyunun %75'ine kadar küçülür, son çare "…".
  var CELL_PAD_X = 0.0025;
  var CELL_PAD_Y = 0.0025;
  var CELL_MIN_SCALE = 0.75;
  var CELL_FONT_FACTOR = 1.0;

  function isCellItem(item) {
    return isLabelItem(item) && item.style === 'cell';
  }

  // {left, top, width, height (px; kaynak kutu + pay), maxWidth (sağa uzama sınırı), fontSize, minFont, caps}
  function cellGeometry(item, box, vw, vh) {
    var left = Math.max(0, (box.left - CELL_PAD_X) * vw);
    var right = Math.min(vw, (box.right + CELL_PAD_X) * vw);
    var top = Math.max(0, (box.top - CELL_PAD_Y) * vh);
    var bottom = Math.min(vh, (box.bottom + CELL_PAD_Y) * vh);
    var line = item && typeof item.line === 'number' && item.line > 0 ? item.line : (box.bottom - box.top);
    var fontSize = Math.max(LABEL_MIN_FONT, Math.round(CELL_FONT_FACTOR * line * vh));
    var maxRight = item && typeof item.maxRight === 'number' && !isNaN(item.maxRight) ? item.maxRight * vw : right;
    return {
      left: Math.round(left), top: Math.round(top), width: Math.round(right - left), height: Math.round(bottom - top),
      maxWidth: Math.round(Math.max(right, Math.min(maxRight, vw)) - left), fontSize: fontSize,
      minFont: Math.max(LABEL_MIN_FONT, Math.round(fontSize * CELL_MIN_SCALE)), caps: !!(item && item.caps === true)
    };
  }

  // measure(size) = yazının o punto ile iç boşluksuz genişliği (px). Dönüş: {width, fontSize, ellipsis}.
  // Sığıyorsa kaynak genişliği korunur; değilse sağa uzar; o da yetmezse küçülür; son çare üç nokta.
  function cellFit(g, measure, padX) {
    var px = num(padX, 0);
    var size = g.fontSize;
    var w = measure(size) + 2 * px;
    if (w <= g.width) return { width: g.width, fontSize: size, ellipsis: false };
    if (w <= g.maxWidth) return { width: Math.ceil(w), fontSize: size, ellipsis: false };
    while (size > g.minFont) {
      size -= 1;
      w = measure(size) + 2 * px;
      if (w <= g.maxWidth) return { width: Math.ceil(w), fontSize: size, ellipsis: false };
    }
    return { width: g.maxWidth, fontSize: g.minFont, ellipsis: true };
  }

  // Hücreler (px: {left, top, width, height}) birbirinin üstüne binmesin: dikey çakışan ve yatay örtüşen komşu çiftlerde
  // sınır iki hücrenin ortasına çekilir (üstteki kısalır, alttaki iner). Yeni dizi döner, sırası girdiyle aynıdır.
  function cellResolve(rects) {
    var out = [];
    var order = [];
    for (var i = 0; i < (rects || []).length; i++) {
      out.push({ left: rects[i].left, top: rects[i].top, width: rects[i].width, height: rects[i].height });
      order.push(i);
    }
    order.sort(function (a, b) { return out[a].top - out[b].top; });
    for (var k = 1; k < order.length; k++) {
      var cur = out[order[k]];
      for (var j = k - 1; j >= 0; j--) {
        var prev = out[order[j]];
        var overlapX = cur.left < prev.left + prev.width && prev.left < cur.left + cur.width;
        var prevBottom = prev.top + prev.height;
        if (!overlapX || prevBottom <= cur.top) continue;
        var cut = Math.round((cur.top + prevBottom) / 2);
        cut = Math.max(prev.top + 1, Math.min(cut, cur.top + cur.height - 1));
        prev.height = cut - prev.top;
        cur.height = cur.top + cur.height - cut;
        cur.top = cut;
      }
    }
    return out;
  }

  // Bölge -> menü anahtarı: 'phone' ve 'notify' telefonMesajları, geri kalan bölgeler bilgi kutuları anahtarına bağlı.
  // 'vehicle' (araç paneli) bilgi kutuları anahtarına bağlıdır.
  function toggleKeyForRegion(region) {
    return (region === 'phone' || region === 'notify') ? 'phoneMessages' : 'infoBoxes';
  }

  // Aynı bölgedeki kutular (telefon balonları) üst üste binmesin: rects = [{left, top, width, height}] sırayla gelir;
  // bir kutu daha önce yerleşenlerden biriyle çakışırsa onun altına (+ gap) itilir. Yeni dizi döner, girdi değişmez.
  function stackRects(rects, gap) {
    var g = Math.max(0, num(gap, 8));
    var out = [];
    for (var i = 0; i < (rects || []).length; i++) {
      var r = { left: rects[i].left, top: rects[i].top, width: rects[i].width, height: rects[i].height };
      for (var j = 0; j < out.length; j++) {
        var p = out[j];
        var overlapX = r.left < p.left + p.width && p.left < r.left + r.width;
        var overlapY = r.top < p.top + p.height && p.top < r.top + r.height;
        if (overlapX && overlapY) r.top = p.top + p.height + g;
      }
      out.push(r);
    }
    return out;
  }

  // placement yoksa: üst %60'taki ve %40'tan dar kutu bilgi kutusu sayılır; placement 'below' ise kesin evet, başka değerse hayır.
  function isInfoItem(item) {
    if (!item || typeof item !== 'object') return false;
    if (typeof item.placement === 'string') return item.placement === 'below' || item.placement === 'left';
    var b = item.box;
    if (!b || item.paragraphs === true) return false;
    return num(b.top, 1) < 0.6 && (num(b.right, 1) - num(b.left, 0)) < 0.4;
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
    belowRect: belowRect,
    leftRect: leftRect,
    overRect: overRect,
    besideRect: besideRect,
    besideFontSize: besideFontSize,
    besideFirstVisible: besideFirstVisible,
    besideMessages: besideMessages,
    isBesideItem: isBesideItem,
    isLabelItem: isLabelItem,
    isPromptItem: isPromptItem,
    isMirrorItem: isMirrorItem,
    hexColor: hexColor,
    rectsOverlap: rectsOverlap,
    promptPlace: promptPlace,
    mirrorPlace: mirrorPlace,
    labelFontSize: labelFontSize,
    labelGeometry: labelGeometry,
    labelPlace: labelPlace,
    labelFits: labelFits,
    labelStyle: labelStyle,
    isCellItem: isCellItem,
    cellGeometry: cellGeometry,
    cellFit: cellFit,
    cellResolve: cellResolve,
    CELL_PAD_X: CELL_PAD_X,
    LABEL_PAD_X: LABEL_PAD_X,
    LABEL_PAD_Y: LABEL_PAD_Y,
    overFontSize: overFontSize,
    isOverItem: isOverItem,
    overTheme: overTheme,
    overCardStyle: overCardStyle,
    overParts: overParts,
    overMaxGrow: overMaxGrow,
    overFit: overFit,
    toggleKeyForRegion: toggleKeyForRegion,
    stackRects: stackRects,
    isInfoItem: isInfoItem,
    acceptSubtitle: acceptSubtitle,
    rgba: rgba
  };
})(typeof window !== 'undefined' ? window : this);
