// SPDX-License-Identifier: AGPL-3.0-only
// Çalıştırma (repo kökünden):
//   /System/Library/Frameworks/JavaScriptCore.framework/Versions/A/Helpers/jsc tv/webos/subtitle-settings.js tv/webos/tests/subtitle-settings.test.js
(function () {
  var S = GT_SUBSETTINGS;
  var count = 0;

  function ok(cond, message) {
    count++;
    if (!cond) throw new Error('FAIL: ' + message);
  }
  function eq(actual, expected, message) {
    ok(actual === expected, message + ' (beklenen ' + expected + ', gelen ' + actual + ')');
  }

  // --- defaults / sanitize ---
  var d = S.defaults(1920, 1080);
  eq(d.x, 960, 'varsayılan x orta');
  eq(d.positionSource, 'tv', 'varsayılan kaynak tv');
  eq(d.version, 1, 'sürüm');
  ok(d.y <= 1080 - d.margin, 'varsayılan y kenar boşluğu içinde');

  var s = S.sanitize({ x: -50, y: 99999, width: 10, fontSize: 500, margin: -3, bgOpacity: 250,
    textColor: 'red', bgColor: '#12ab34', outline: 'glow', align: 'middle', bold: 'yes', positionSource: 'x', junk: 1 }, 1920, 1080);
  eq(s.x, 0, 'x alt sınır');
  eq(s.y, 1080, 'y üst sınır');
  eq(s.width, 300, 'width alt sınır');
  eq(s.fontSize, 120, 'fontSize üst sınır');
  eq(s.margin, 0, 'margin alt sınır');
  eq(s.bgOpacity, 100, 'bgOpacity üst sınır');
  eq(s.textColor, d.textColor, 'geçersiz renk varsayılana döner');
  eq(s.bgColor, '#12AB34', 'geçerli renk korunur');
  eq(s.outline, 'shadow', 'geçersiz outline varsayılan');
  eq(s.align, 'center', 'geçersiz align varsayılan');
  eq(s.bold, true, 'geçersiz bold varsayılan');
  eq(s.positionSource, 'tv', 'geçersiz kaynak varsayılan');
  ok(s.junk === undefined, 'bilinmeyen alan atılır');
  var n = S.sanitize(null, 1920, 1080);
  eq(n.fontSize, d.fontSize, 'null -> varsayılan');
  var nan = S.sanitize({ x: 'abc', fontSize: NaN, width: Infinity }, 1920, 1080);
  eq(nan.x, d.x, 'NaN x varsayılan');
  eq(nan.fontSize, d.fontSize, 'NaN fontSize varsayılan');
  eq(nan.width, d.width, 'Infinity width varsayılan');

  // --- parse / serialize ---
  eq(S.parse('{bozuk', 1920, 1080), null, 'bozuk JSON null');
  eq(S.parse('', 1920, 1080), null, 'boş JSON null');
  eq(S.parse('5', 1920, 1080), null, 'sayı JSON null');
  var round = S.parse(S.serialize(s), 1920, 1080);
  eq(JSON.stringify(round), JSON.stringify(S.sanitize(s, 1920, 1080)), 'serialize/parse gidiş-dönüş');

  // --- migrate ---
  eq(S.migrate(7, 1080).y, Math.round(1080 - 1080 * 0.07), 'migrate %7');
  eq(S.migrate(0, 1080).y, 1080, 'migrate %0 -> 1080');
  eq(S.migrate(50, 1200).y, 600, 'migrate %50 1200 yüksek');
  eq(S.migrate(500, 1080).y, Math.round(1080 - 1080 * 0.92), 'migrate üst sınır %92');
  eq(S.migrate('abc', 1080).y, d.y, 'migrate geçersiz -> varsayılan y');
  eq(S.migrate(-5, 1080).y, 1080, 'migrate negatif -> %0');
  eq(S.migrate(7, 1080).positionSource, 'tv', 'migrate kaynak tv');

  // --- presets ---
  var views = [[1920, 1080], [1920, 1200], [1920, 900]];
  var expectedNames = ['Alt orta', 'Üst orta', 'Alt sol', 'Alt sağ', 'Ekran ortası'];
  for (var v = 0; v < views.length; v++) {
    var vw = views[v][0], vh = views[v][1];
    var list = S.presets(vw, vh, S.defaults(vw, vh));
    eq(list.length, 5, 'beş hazır konum');
    for (var p = 0; p < list.length; p++) {
      eq(list[p].name, expectedNames[p], 'hazır konum adı ' + p);
      var applied = S.defaults(vw, vh);
      applied.x = list[p].x; applied.y = list[p].y; applied.align = list[p].align;
      var r = S.computeRect(applied, vw, vh, 120);
      ok(r.left >= applied.margin && r.left + r.width <= vw - applied.margin, 'hazır konum yatay içeride: ' + list[p].id);
      ok(r.top >= applied.margin && r.bottom <= vh - applied.margin, 'hazır konum dikey içeride: ' + list[p].id);
    }
    eq(list[2].align, 'left', 'Alt sol sola yaslı');
    eq(list[3].align, 'right', 'Alt sağ sağa yaslı');
    eq(S.presets(vw, vh)[0].x, vw / 2, 'ayarsız çağrı çalışır');
  }
  var leftSet = S.defaults(1920, 1080); var lp = S.presets(1920, 1080, leftSet)[2];
  eq(lp.x - Math.min(leftSet.width, 1920 - 2 * leftSet.margin) / 2, leftSet.margin, 'Alt sol kutunun kenarı kenar boşluğunda');

  // --- acceptSubtitle ---
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: null }, { text: 'a', at: 999 }), 'drop', 'eski at düşer');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: null }, { text: 'a', at: 1000 }), 'show', 'eşit at kabul');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: null }, { text: 'a', at: 1001 }), 'show', 'yeni at kabul');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: null }, { text: 'a' }), 'show', 'at yoksa kabul');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: null }, { text: 'a', at: 'x' }), 'show', 'sayı olmayan at yoksayılır');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: 'a' }, { text: 'a', at: 1500 }), 'extend', 'aynı metin uzatır');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: 'a' }, { text: 'a' }), 'extend', 'at yoksa da aynı metin uzatır');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: 'a' }, { text: 'a', at: 10 }), 'drop', 'eski aynı metin önce düşer');
  eq(S.acceptSubtitle({ lastAt: 1000, shownText: 'a' }, { text: 'b', at: 1500 }), 'show', 'farklı metin gösterilir');
  eq(S.acceptSubtitle({ lastAt: 0, shownText: null }, { text: '' }), 'drop', 'boş metin düşer');
  eq(S.acceptSubtitle({}, { text: 'a', at: 5 }), 'show', 'boş durum kabul');

  // --- rgba ---
  eq(S.rgba('#FF8000', 50), 'rgba(255, 128, 0, 0.5)', 'rgba');

  // --- computeRect ızgara / özellik testi ---
  var viewports = [[1920, 1080], [1920, 1200], [1920, 900], [1280, 720], [1000, 600]];
  var xs = [-100, 0, 1, 100, 500, 960, 1500, 1919, 1920, 5000];
  var ys = [-100, 0, 40, 500, 1000, 1080, 1199, 1200, 9999];
  var widths = [300, 777, 1000, 1400, 1920, 3000];
  var margins = [0, 4, 48, 100, 200];
  var heights = [0, 50, 130, 400, 900, 1000, 1100, 3000];
  var checked = 0, overflow = 0;
  for (var a = 0; a < viewports.length; a++) {
    for (var b = 0; b < xs.length; b++) for (var c = 0; c < ys.length; c++) for (var e = 0; e < widths.length; e++)
      for (var f = 0; f < margins.length; f++) for (var g = 0; g < heights.length; g++) {
        var w = viewports[a][0], h = viewports[a][1];
        var st = { x: xs[b], y: ys[c], width: widths[e], margin: margins[f] };
        var ch = heights[g];
        var rc = S.computeRect(st, w, h, ch);
        var m = Math.min(margins[f], Math.floor(w / 2) - 1);
        var tag = JSON.stringify([w, h, st, ch, rc]);
        ok(rc.left >= m && rc.left + rc.width <= w - m, 'yatay içeride ' + tag);
        ok(rc.width <= widths[e] && rc.width <= w - 2 * m, 'genişlik sınırı ' + tag);
        eq(rc.bottom - rc.top, ch, 'yükseklik korunur ' + tag);
        if (ch <= h - 2 * m) {
          ok(rc.top >= m && rc.bottom <= h - m, 'dikey içeride ' + tag);
        } else {
          eq(rc.top, m, 'sığmayan içerik üstten başlar ' + tag);
          overflow++;
        }
        var cp = S.clampPosition(st, w, h, ch);
        var back = S.computeRect({ x: cp.x, y: cp.y, width: st.width, margin: st.margin }, w, h, ch);
        if (ch <= h - 2 * m) {
          eq(back.bottom, cp.y, 'clampPosition y sabit ' + tag);
          ok(Math.abs((back.left + back.width / 2) - cp.x) <= 1, 'clampPosition x sabit ' + tag);
        }
        checked++;
      }
  }
  ok(overflow > 0, 'taşan içerik durumu denendi');

  // --- belowRect / isInfoItem ---
  var gtaBox = { left: 0.02, top: 0.28, right: 0.23, bottom: 0.40 };
  var r1 = S.belowRect(gtaBox, 0.02, 1920, 1080, 48, 0);
  eq(r1.left, 48, 'sol kenar boşluğuna çekilir (0.02*1920=38 < 48)');
  eq(r1.width, Math.round(0.22 * 1920), 'dar kutu: genişlik ekranın %22 si');
  ok(r1.width >= Math.round(0.22 * 1920) - 1, 'en az %22');
  eq(r1.top, Math.round(0.40 * 1080 + 0.6 * 0.02 * 1080), 'üst = alt + 0.6 x satır');
  ok(r1.top > 0.40 * 1080, 'İngilizce kutuyu örtmez');
  var r2 = S.belowRect({ left: 0.85, top: 0.3, right: 0.97, bottom: 0.4 }, 0.02, 1920, 1080, 48, 0);
  eq(r2.left + r2.width, 1920 - 48, 'sağ kenarda sağ boşluğa dayanır');
  ok(r2.left >= 48, 'sol sınır içinde');
  var r3 = S.belowRect({ left: 0.1, top: 0.8, right: 0.3, bottom: 0.95 }, 0.02, 1920, 1080, 48, 200);
  eq(r3.placed, 'above', 'altta yer yok: kutunun üstüne');
  eq(r3.top, Math.round(0.8 * 1080 - 0.6 * 0.02 * 1080 - 200), 'üstte: üst = kutu üstü - boşluk - yükseklik');
  ok(r3.top + 200 <= 0.8 * 1080, 'üstteki içerik kutuyu örtmez');
  var r3b = S.belowRect({ left: 0.1, top: 0.05, right: 0.3, bottom: 0.95 }, 0.02, 1920, 1080, 48, 200);
  eq(r3b.placed, 'below', 'üstte de yer yoksa below kalır');
  eq(r3b.top, Math.round(0.95 * 1080), 'üstte de yok: kutunun altından yukarı çıkmaz');
  eq(r1.placed, 'below', 'normal durumda below');
  var r4 = S.belowRect({ left: 0.0, top: 0.2, right: 1.0, bottom: 0.3 }, 0.02, 1920, 1080, 48, 100);
  eq(r4.left, 48, 'çok geniş kutu: sol boşluk');
  eq(r4.width, 1920 - 96, 'çok geniş kutu: genişlik ekrana sıkıştırılır');
  var r5 = S.belowRect(gtaBox, 0.02, 1920, 1080, 48, 100);
  ok(r5.top + 100 <= 1080 - 48, 'sığan içerik kaydırılmaz');
  eq(r5.top, r1.top, 'sığan içerikte üst değişmez');
  ok(S.isInfoItem({ placement: 'below', box: gtaBox }), 'placement below');
  ok(S.isInfoItem({ placement: 'left', box: gtaBox }), 'placement left');

  // --- leftRect ---
  var lb = { left: 0.5, top: 0.3, right: 0.6, bottom: 0.4 };
  var l1 = S.leftRect(lb, 0.02, 1920, 1080, 48, 100);
  eq(l1.placed, 'left', 'solda yer var: left');
  eq(l1.width, Math.round(0.22 * 1920), 'genişlik kuralı belowRect ile aynı');
  eq(l1.top, Math.round(0.3 * 1080), 'üst kutunun üstüyle hizalı');
  eq(l1.left + l1.width + l1.gap, Math.round(0.5 * 1920), 'sağ kenar = kutu solu - boşluk');
  ok(l1.left >= 48, 'sol boşluk içinde');
  var wideBox = { left: 0.6, top: 0.3, right: 0.9, bottom: 0.4 };
  var l2 = S.leftRect(wideBox, 0.02, 1920, 1080, 48, 100);
  eq(l2.width, Math.round(0.3 * 1920), 'geniş kutu: genişlik kutu kadar');
  var l3 = S.leftRect(gtaBox, 0.02, 1920, 1080, 48, 100);
  eq(l3.placed, 'below', 'solda yer yok: below');
  eq(l3.top, S.belowRect(gtaBox, 0.02, 1920, 1080, 48, 100).top, 'fallback belowRect ile aynı');
  var l4 = S.leftRect({ left: 0.02, top: 0.8, right: 0.1, bottom: 0.95 }, 0.02, 1920, 1080, 48, 200);
  eq(l4.placed, 'above', 'solda ve altta yer yok: üste');
  var l5 = S.leftRect({ left: 0.5, top: 0.9, right: 0.6, bottom: 0.95 }, 0.02, 1920, 1080, 48, 200);
  eq(l5.placed, 'left', 'solda yer var');
  eq(l5.top, 1080 - 48 - 200, 'alta taşan içerik ekran içine çekilir');
  var l6 = S.leftRect({ left: 0.5, top: 0.0, right: 0.6, bottom: 0.05 }, 0.02, 1920, 1080, 48, 100);
  eq(l6.top, 48, 'üst kenar boşluğuna çekilir');
  eq(S.leftRect(lb, 0.02, 1920, 1080, 48, 0).placed, 'left', 'contentHeight 0 ile de çalışır');

  // --- overRect / bölge anahtarları ---
  var obj = S.overRect({ left: 0.375, top: 0.946, right: 0.625, bottom: 0.977 }, 0.031, 1920, 1080, 48, 0);
  eq(obj.left, 720, 'objective sol'); eq(obj.width, 480, 'objective genişlik');
  eq(obj.top, 1022, 'objective: içerik kutudan kısa, top = kutu üstü');
  var objTall = S.overRect({ left: 0.375, top: 0.946, right: 0.625, bottom: 0.977 }, 0.031, 1920, 1080, 48, 90);
  ok(objTall.top + 90 <= 1080 - 8, 'objective uzun içerik: ekran dışına çıkmaz');
  ok(objTall.top < 1022, 'objective uzun içerik yukarı uzar');
  ok(objTall.top + 90 >= Math.round(0.977 * 1080) - 1, 'objective: alt kenar İngilizce kutunun altını örter');
  ok(obj.top + 34 >= 1055 - 1 && obj.top <= 1022, 'objective kısa içerik: kutuyu örter');
  var desc = S.overRect({ left: 0.547, top: 0.483, right: 0.801, bottom: 0.581 }, 0.026, 1920, 1080, 48, 150);
  eq(desc.top, Math.round(0.483 * 1080), 'description: aşağı uzar, üst sabit');
  eq(desc.left, Math.round(0.547 * 1920), 'description sol');
  ok(desc.left + desc.width <= 1920 - 48, 'description sağ sınır içinde');
  var nar = S.overRect({ left: 0.5, top: 0.3, right: 0.52, bottom: 0.33 }, 0.02, 1920, 1080, 48, 0);
  eq(nar.width, Math.round(0.18 * 1920), 'dar kutu en az %18 genişlik');
  var edge = S.overRect({ left: 0.95, top: 0.3, right: 0.99, bottom: 0.33 }, 0.02, 1920, 1080, 48, 0);
  eq(edge.left + edge.width, 1920 - 48, 'sağ kenarda sıkıştırılır');
  var topEdge = S.overRect({ left: 0.1, top: 0.0, right: 0.3, bottom: 0.03 }, 0.02, 1920, 1080, 48, 2000);
  ok(topEdge.top >= 8, 'üst kenar boşluğu');
  eq(S.overFontSize(0.031, 1080, 44), 25, 'over yazı boyu 0.75 x satır');
  eq(S.overFontSize(0.01, 1080, 44), 18, 'alt sınır 18');
  eq(S.overFontSize(0.1, 1080, 44), 44, 'üst sınır kullanıcı boyu');
  ok(S.isOverItem({ placement: 'over' }) && !S.isOverItem({ placement: 'below' }), 'isOverItem');
  eq(S.toggleKeyForRegion('phone'), 'phoneMessages', 'phone'); eq(S.toggleKeyForRegion('notify'), 'phoneMessages', 'notify');
  eq(S.toggleKeyForRegion('objective'), 'infoBoxes', 'objective'); eq(S.toggleKeyForRegion('description'), 'infoBoxes', 'description');
  eq(S.toggleKeyForRegion('info'), 'infoBoxes', 'info');
  var nl = S.leftRect({ left: 0.80, top: 0.85, right: 0.97, bottom: 0.95 }, 0.026, 1920, 1080, 48, 100);
  ok(nl.left >= 48 && nl.left + nl.width <= 1920 - 48 && nl.top + 100 <= 1080 - 48, 'notify sığar');
  // Aynı bölgede çakışmayan over kutuları itilmez
  var two = S.stackRects([{ left: 720, top: 1022, width: 480, height: 40 }, { left: 1050, top: 520, width: 487, height: 100 }], 8);
  eq(two[0].top, 1022, 'over kutusu itilmez'); eq(two[1].top, 520, 'ikinci de yerinde');

  // --- over teması / konuşmacı / sığdırma ---
  eq(S.overTheme({ theme: 'light' }), 'light', 'tema light'); eq(S.overTheme({ theme: 'dark' }), 'dark', 'tema dark');
  eq(S.overTheme({}), 'dark', 'tema yoksa koyu'); eq(S.overTheme({ theme: 'x' }), 'dark', 'geçersiz tema koyu'); eq(S.overTheme(null), 'dark', 'null koyu');
  var lc = S.overCardStyle('light', 0), dc = S.overCardStyle('dark', 0);
  eq(lc.color, '#15161a', 'açık kart yazı rengi'); eq(dc.color, '#ffffff', 'koyu kart yazı rengi');
  ok(lc.background.indexOf('rgba(246, 246, 248') === 0 && dc.background.indexOf('rgba(14, 14, 18') === 0, 'kart zeminleri');
  ok(lc.alpha >= 0.92 && dc.alpha >= 0.92, 'bgOpacity 0 olsa da alfa >= 0.92');
  eq(S.overCardStyle('dark', 100).alpha, 1, 'yüksek ayar alfayı artırır'); eq(S.overCardStyle('light', 10).alpha, 0.98, 'açık alfa 0.98');
  var pa = S.overParts({ speaker: ' Lucia ', text: 'Selam\nnaber' });
  eq(pa.speaker, 'Lucia', 'konuşmacı kırpılır'); eq(pa.segments.length, 2, '\\n parçalar'); ok(!pa.titleBold, 'konuşmacı varken başlık kalın değil');
  var pd = S.overParts({ text: 'Başlık\n\nGövde metni' });
  eq(pd.segments.length, 2, 'boş parça atılır'); ok(pd.titleBold, 'açıklamada ilk parça kalın'); eq(pd.speaker, '', 'konuşmacı yok');
  ok(!S.overParts({ text: 'Tek satır' }).titleBold, 'tek parça kalın değil');
  eq(S.overMaxGrow(100, 5000), 160, '1.6 kat büyüme sınırı'); eq(S.overMaxGrow(100, 120), 120, 'ekran sınırı daha küçükse o');
  eq(S.overFit(150, 160, 30, 18), 'ok', 'sığdı'); eq(S.overFit(200, 160, 30, 18), 'shrink', 'küçült'); eq(S.overFit(200, 160, 18, 18), 'min', 'en küçük boy');

  // --- stackRects ---
  var inR = [{ left: 100, top: 100, width: 300, height: 80 }, { left: 150, top: 120, width: 300, height: 60 }, { left: 800, top: 100, width: 200, height: 50 }, { left: 120, top: 400, width: 200, height: 40 }];
  var st = S.stackRects(inR, 10);
  eq(st[0].top, 100, 'ilk kutu yerinde');
  eq(st[1].top, 190, 'çakışan kutu öncekinin altına + boşluk');
  eq(st[2].top, 100, 'yatayda ayrık kutu yerinde');
  eq(st[3].top, 400, 'çakışmayan kutu yerinde');
  eq(inR[1].top, 120, 'girdi değişmez');
  var st2 = S.stackRects([{ left: 0, top: 0, width: 100, height: 50 }, { left: 0, top: 0, width: 100, height: 50 }, { left: 0, top: 10, width: 100, height: 50 }], 8);
  eq(st2[1].top, 58, 'ikinci: 50 + 8');
  eq(st2[2].top, 116, 'üçüncü: ikincinin altına');
  eq(S.stackRects([], 8).length, 0, 'boş dizi');
  ok(!S.isInfoItem({ placement: 'over', box: gtaBox }), 'placement başka -> hayır');
  ok(S.isInfoItem({ box: gtaBox }), 'placement yok, üst-sol dar kutu -> bilgi');
  ok(!S.isInfoItem({ box: { left: 0.1, top: 0.8, right: 0.3, bottom: 0.9 } }), 'placement yok, alttaki kutu -> hayır');
  ok(!S.isInfoItem({ box: { left: 0.1, top: 0.1, right: 0.6, bottom: 0.2 } }), 'placement yok, geniş kutu -> hayır');
  ok(!S.isInfoItem({ box: gtaBox, paragraphs: true }), 'paragraphs -> hayır');

  print('PASS: ' + count + ' denetim, ' + checked + ' computeRect kombinasyonu (' + overflow + ' taşan içerik)');
})();
