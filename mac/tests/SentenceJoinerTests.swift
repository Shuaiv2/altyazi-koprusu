// SPDX-License-Identifier: AGPL-3.0-only
//
// swiftc -parse-as-library -o /tmp/sentence-joiner-tests mac/SentenceJoiner.swift mac/tests/SentenceJoinerTests.swift && /tmp/sentence-joiner-tests
import Foundation

@main struct Tests {
    static var failures = 0
    static func eq<T: Equatable>(_ a: T, _ b: T, _ n: String, _ l: UInt = #line) {
        if a != b { failures += 1; print("FAIL (\(l)) \(n): \(a) != \(b)") }
    }
    static func main() {
        var j = SentenceJoiner()
        var r = j.feed("- And sometimes that means that uh...", at: 0)
        eq(r.source, "And sometimes that means that uh...", "s1a"); eq(r.isContinuation, false, "s1a c")
        r = j.feed("we have to do stuff that isn't always very...", at: 2)
        eq(r.source, "And sometimes that means that we have to do stuff that isn't always very...", "s1b"); eq(r.isContinuation, true, "s1b c")
        r = j.feed("fun.", at: 4)
        eq(r.source, "And sometimes that means that we have to do stuff that isn't always very fun.", "s1c")
        print("SEQ1:", r.source)

        j = SentenceJoiner()
        r = j.feed("- What we're trying to say, darling, is that,", at: 0)
        eq(r.source, "What we're trying to say, darling, is that,", "s2a")
        r = j.feed("Well...", at: 2)
        eq(r.source, "What we're trying to say, darling, is that, Well...", "s2b"); eq(r.hold, false, "s2b hold")
        r = j.feed("Mom and Dad,", at: 4)
        eq(r.source, "What we're trying to say, darling, is that, Well, Mom and Dad,", "s2c")
        r = j.feed("we've decided to go our separate ways.", at: 6)
        eq(r.source, "Well, Mom and Dad, we've decided to go our separate ways.", "s2d (max 3)")
        print("SEQ2:", r.source)

        // Dolgu tek başına: beklet; sonraki parça ile birleşir.
        j = SentenceJoiner()
        r = j.feed("Well...", at: 0); eq(r.hold, true, "filler hold")
        r = j.feed("Mom and Dad,", at: 2); eq(r.source, "Well, Mom and Dad,", "filler joined"); eq(r.hold, false, "no hold")

        // Zaman aşımı
        j = SentenceJoiner()
        _ = j.feed("very...", at: 0)
        r = j.feed("fun.", at: 7); eq(r.isContinuation, false, "timeout"); eq(r.source, "fun.", "timeout src")

        // Yeni konuşmacı
        j = SentenceJoiner()
        _ = j.feed("We should go...", at: 0)
        r = j.feed("- Fine.", at: 1); eq(r.isContinuation, false, "dash"); eq(r.source, "Fine.", "dash src")
        j = SentenceJoiner()
        _ = j.feed("We should go", at: 0)
        r = j.feed("May: Fine.", at: 1); eq(r.isContinuation, false, "name")

        // Büyük harf, üç nokta yok -> yeni cümle
        j = SentenceJoiner()
        _ = j.feed("We should go", at: 0)
        r = j.feed("Right now.", at: 1); eq(r.isContinuation, false, "upper"); eq(r.source, "Right now.", "upper src")
        // Kapalı cümle küçük harfle de devam etmez
        j = SentenceJoiner()
        _ = j.feed("We left.", at: 0)
        r = j.feed("and then", at: 1); eq(r.isContinuation, false, "closed")
        // Küçük harf devamı
        j = SentenceJoiner()
        _ = j.feed("We should go", at: 0)
        r = j.feed("right now.", at: 1); eq(r.source, "We should go right now.", "lower")

        // Maks parça
        j = SentenceJoiner()
        for (i, w) in ["a,", "b,", "c,", "d,", "e."].enumerated() { r = j.feed(w, at: Double(i)) }
        eq(r.source, "c, d, e.", "max fragments")
        // Maks karakter
        j = SentenceJoiner()
        _ = j.feed(String(repeating: "x", count: 150) + ",", at: 0)
        r = j.feed(String(repeating: "y", count: 100) + ",", at: 1)
        eq(r.source.count <= 220, true, "max chars")

        print(failures == 0 ? "OK" : "\(failures) FAILED")
        exit(failures == 0 ? 0 : 1)
    }
}
