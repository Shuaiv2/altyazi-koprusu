// SPDX-License-Identifier: AGPL-3.0-only
// TV altyazı yerleşimi: kutuyu ve yazıyı DOM'a yerleştirir. TV önizlemesi, gerçek altyazılar ve Mac düzenleyicisi
// hep bu işlevden geçer (tek yerleşim yolu). Hesap GT_SUBSETTINGS.computeRect'tedir. ES5.
(function (root) {
  'use strict';

  function outlineShadow(kind) {
    var shadow = '0 2px 4px #000';
    var outline = '-2px 0 #000, 2px 0 #000, 0 -2px #000, 0 2px #000, -2px -2px #000, 2px -2px #000, -2px 2px #000, 2px 2px #000';
    if (kind === 'none') return 'none';
    if (kind === 'shadow') return shadow;
    if (kind === 'outline') return outline;
    return outline + ', ' + shadow;
  }

  // boxEl: konumlanan kutu (position: absolute), textEl: içindeki yazı (inline-block).
  // Kutuyu GT_SUBSETTINGS.computeRect ile yerleştirir; içerik dikeyde sığmazsa yazıyı 2 px adımlarla küçültür.
  // opts.paragraphs: görev paneli gibi yazılar sola yaslanır. Dönüş: {fontSize, contentHeight, rect}.
  function apply(boxEl, textEl, s, vw, vh, opts) {
    var SUB = root.GT_SUBSETTINGS;
    var paragraphs = !!(opts && opts.paragraphs);
    textEl.style.color = s.textColor;
    textEl.style.background = SUB.rgba(s.bgColor, s.bgOpacity);
    textEl.style.fontWeight = s.bold ? '700' : '600';
    textEl.style.textShadow = outlineShadow(s.outline);
    textEl.style.textAlign = paragraphs ? 'left' : s.align;
    textEl.style.verticalAlign = 'top';
    boxEl.style.textAlign = s.align;
    boxEl.style.lineHeight = '0';
    boxEl.style.right = 'auto';
    boxEl.style.bottom = 'auto';
    boxEl.style.transform = 'none';
    boxEl.style.webkitTransform = 'none';
    var margin = Math.min(s.margin, Math.floor(vw / 2) - 1);
    var size = s.fontSize, rect, contentHeight = 0;
    for (var guard = 0; guard < 60; guard++) {
      textEl.style.fontSize = size + 'px';
      rect = SUB.computeRect(s, vw, vh, 0);
      boxEl.style.left = rect.left + 'px';
      boxEl.style.width = rect.width + 'px';
      boxEl.style.top = '0px';
      contentHeight = textEl.offsetHeight;
      if (contentHeight <= vh - 2 * margin || size <= 18) break;
      size -= 2;
    }
    rect = SUB.computeRect(s, vw, vh, contentHeight);
    boxEl.style.top = rect.top + 'px';
    return { fontSize: size, contentHeight: contentHeight, rect: rect };
  }

  root.GT_SUBLAYOUT = { apply: apply, outlineShadow: outlineShadow };
})(typeof window !== 'undefined' ? window : this);
