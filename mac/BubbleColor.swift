// SPDX-License-Identifier: AGPL-3.0-only

import CoreGraphics
import Foundation

/// Telefon balonlarının zemin rengi: oyuncunun balonları MAVİ (beyaz yazı), karşı tarafınkiler beyaz/açık gri (koyu yazı).
/// Konum güvenilmez (son seçenek balonunun merkezi telefonun solunda kalabiliyor); renk güvenilir.
public enum BubbleColor {
    /// Sıkı mavi kuralı: B belirgin baskın ve doygunluk yeterli.
    public static func isBlue(r: Double, g: Double, b: Double) -> Bool {
        let mx = max(r, g, b), mn = min(r, g, b)
        guard mx > 0 else { return false }
        return b > r + 40 && b > g + 15 && (mx - mn) / mx > 0.25
    }

    /// `rect`: satır kutusu (kareye göre normalize, sol-üst). Biraz genişletilmiş bölgenin EN SIK rengi (4 bit/kanal kutuları,
    /// kazanan kutunun ortalaması) balon zeminidir; yazı pikselleri azınlıkta kaldığı için dışarıda kalır.
    /// nil: bölge okunamadı.
    public static func dominantColor(in frame: CGImage, rect: CGRect) -> (r: Double, g: Double, b: Double)? {
        let w = Double(frame.width), h = Double(frame.height)
        let padX = 0.004, padY = 0.0
        let px = CGRect(x: (rect.minX - padX) * w, y: (rect.minY - padY) * h,
                        width: (rect.width + 2 * padX) * w, height: (rect.height + 2 * padY) * h).integral
            .intersection(CGRect(x: 0, y: 0, width: w, height: h))
        guard !px.isNull, px.width >= 2, px.height >= 2, let crop = frame.cropping(to: px) else { return nil }
        let cw = min(Int(px.width), 96), ch = min(Int(px.height), 48)
        var buf = [UInt8](repeating: 0, count: cw * ch * 4)
        guard let ctx = CGContext(data: &buf, width: cw, height: ch, bitsPerComponent: 8, bytesPerRow: cw * 4,
                                  space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        ctx.interpolationQuality = .low
        ctx.draw(crop, in: CGRect(x: 0, y: 0, width: cw, height: ch))
        var count = [Int](repeating: 0, count: 4096)
        var sum = [(Int, Int, Int)](repeating: (0, 0, 0), count: 4096)
        for i in 0..<(cw * ch) {
            let r = Int(buf[i * 4]), g = Int(buf[i * 4 + 1]), b = Int(buf[i * 4 + 2])
            let k = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4)
            count[k] += 1
            sum[k].0 += r; sum[k].1 += g; sum[k].2 += b
        }
        guard let k = count.indices.max(by: { count[$0] < count[$1] }), count[k] > 0 else { return nil }
        let n = Double(count[k])
        return (Double(sum[k].0) / n, Double(sum[k].1) / n, Double(sum[k].2) / n)
    }

    /// true: mavi (oyuncunun balonu), false: beyaz/gri, nil: okunamadı.
    public static func isBlueBubble(in frame: CGImage, rect: CGRect) -> Bool? {
        guard let c = dominantColor(in: frame, rect: rect) else { return nil }
        return isBlue(r: c.r, g: c.g, b: c.b)
    }
}
