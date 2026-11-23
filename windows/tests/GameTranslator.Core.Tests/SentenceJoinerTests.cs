// SPDX-License-Identifier: AGPL-3.0-only

using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// <summary>mac/tests/SentenceJoinerTests.swift'in birebir karşılığı.</summary>
public class SentenceJoinerTests
{
    [Fact]
    public void JoinsEllipsisFragments()
    {
        var j = new SentenceJoiner();
        var r = j.Feed("- And sometimes that means that uh...", 0);
        Assert.Equal("And sometimes that means that uh...", r.Source);
        Assert.False(r.IsContinuation);
        r = j.Feed("we have to do stuff that isn't always very...", 2);
        Assert.Equal("And sometimes that means that we have to do stuff that isn't always very...", r.Source);
        Assert.True(r.IsContinuation);
        r = j.Feed("fun.", 4);
        Assert.Equal("And sometimes that means that we have to do stuff that isn't always very fun.", r.Source);
    }

    [Fact]
    public void KeepsAtMostThreeFragments()
    {
        var j = new SentenceJoiner();
        var r = j.Feed("- What we're trying to say, darling, is that,", 0);
        Assert.Equal("What we're trying to say, darling, is that,", r.Source);
        r = j.Feed("Well...", 2);
        Assert.Equal("What we're trying to say, darling, is that, Well...", r.Source);
        Assert.False(r.Hold);
        r = j.Feed("Mom and Dad,", 4);
        Assert.Equal("What we're trying to say, darling, is that, Well, Mom and Dad,", r.Source);
        r = j.Feed("we've decided to go our separate ways.", 6);
        Assert.Equal("Well, Mom and Dad, we've decided to go our separate ways.", r.Source);
    }

    [Fact]
    public void LoneFillerIsHeldThenJoined()
    {
        var j = new SentenceJoiner();
        var r = j.Feed("Well...", 0);
        Assert.True(r.Hold);
        r = j.Feed("Mom and Dad,", 2);
        Assert.Equal("Well, Mom and Dad,", r.Source);
        Assert.False(r.Hold);
    }

    [Fact]
    public void TimeoutStartsNewSentence()
    {
        var j = new SentenceJoiner();
        j.Feed("very...", 0);
        var r = j.Feed("fun.", 7);
        Assert.False(r.IsContinuation);
        Assert.Equal("fun.", r.Source);
    }

    [Fact]
    public void NewSpeakerStartsNewSentence()
    {
        var j = new SentenceJoiner();
        j.Feed("We should go...", 0);
        var r = j.Feed("- Fine.", 1);
        Assert.False(r.IsContinuation);
        Assert.Equal("Fine.", r.Source);

        j = new SentenceJoiner();
        j.Feed("We should go", 0);
        r = j.Feed("May: Fine.", 1);
        Assert.False(r.IsContinuation);
    }

    [Fact]
    public void UppercaseWithoutEllipsisIsNewSentence()
    {
        var j = new SentenceJoiner();
        j.Feed("We should go", 0);
        var r = j.Feed("Right now.", 1);
        Assert.False(r.IsContinuation);
        Assert.Equal("Right now.", r.Source);
    }

    [Fact]
    public void ClosedSentenceDoesNotContinueEvenLowercase()
    {
        var j = new SentenceJoiner();
        j.Feed("We left.", 0);
        Assert.False(j.Feed("and then", 1).IsContinuation);
    }

    [Fact]
    public void LowercaseContinues()
    {
        var j = new SentenceJoiner();
        j.Feed("We should go", 0);
        Assert.Equal("We should go right now.", j.Feed("right now.", 1).Source);
    }

    [Fact]
    public void MaxFragments()
    {
        var j = new SentenceJoiner();
        SentenceJoiner.Result r = default;
        string[] words = ["a,", "b,", "c,", "d,", "e."];
        for (var i = 0; i < words.Length; i++) r = j.Feed(words[i], i);
        Assert.Equal("c, d, e.", r.Source);
    }

    [Fact]
    public void MaxChars()
    {
        var j = new SentenceJoiner();
        j.Feed(new string('x', 150) + ",", 0);
        var r = j.Feed(new string('y', 100) + ",", 1);
        Assert.True(r.Source.Length <= 220);
    }

    [Fact]
    public void SupersetReplacesAndSubsetIgnored()
    {
        var j = new SentenceJoiner();
        j.Feed("They should check with Brian or Bonnie,", 0);
        var r = j.Feed("They should check with Brian or Bonnie, and", 1);
        Assert.Equal("They should check with Brian or Bonnie, and", r.Source);
        r = j.Feed("they should check with Brian or Bonnie,", 2);
        Assert.Equal("They should check with Brian or Bonnie, and", r.Source);
    }

    [Fact]
    public void SameReadTwiceIsNotContinuation()
    {
        var j = new SentenceJoiner();
        j.Feed("Welcome to BuckMe,", 0);
        var r = j.Feed("Welcome to BuckMe,", 1);
        Assert.Equal("Welcome to BuckMe,", r.Source);
        Assert.False(r.IsContinuation);
    }

    [Fact]
    public void OverlapIsTrimmedNotDuplicated()
    {
        var j = new SentenceJoiner();
        j.Feed("We should probably go see the boss...", 0);
        var r = j.Feed("see the boss and tell him now.", 1);
        Assert.Equal(1, r.Source.Split("see the boss").Length - 1);
    }
}
