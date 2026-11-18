// SPDX-License-Identifier: AGPL-3.0-only

namespace GameTranslator.Translation.Tests;

public class GoogleGtxTranslatorTests
{
    [Theory]
    [InlineData("MJ: According to witnesses, he was running\ndown Broadway, yelling nonsense.",
        "MJ: According to witnesses, he was running down Broadway, yelling nonsense.")]
    [InlineData("You'll whip him into shape in no \r\n time.", "You'll whip him into shape in no time.")]
    [InlineData("One\ntwo\nthree", "One two three")]
    [InlineData("Single line.", "Single line.")]
    // Konuşmacı işaretiyle başlayan satır ayrı konuşmadır.
    [InlineData("Where are you going?\n- Home.", "Where are you going?\n- Home.")]
    [InlineData("Wait for me\nplease.\n>> No.", "Wait for me please.\n>> No.")]
    // Yarım cümlenin ortasındaki tire konuşmacı işareti değildir.
    [InlineData("There is a\n- bakery up the street!", "There is a bakery up the street!")]
    public void WrappedLinesBecomeOneSentence(string text, string expected) =>
        Assert.Equal(expected, GoogleGtxTranslator.JoinWrappedLines(text));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Hosts { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };

    /// 429 veren ana uç nokta 10 dakika atlanır; çeviri clients5 aynasından gelir.
    [Fact]
    public async Task RateLimitedMainEndpointIsSkippedForTenMinutes()
    {
        GoogleGtxTranslator.ResetBreaker();
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var handler = new Handler(request => request.RequestUri!.Host == "translate.googleapis.com"
            ? new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests)
            : Json("""["Merhaba dünya"]"""));
        var translator = new GoogleGtxTranslator("en", "tr", new HttpClient(handler), () => now);

        Assert.Equal("Merhaba dünya", await translator.TranslateAsync("Hello breaker one"));
        Assert.Equal(["translate.googleapis.com", "clients5.google.com"], handler.Hosts);
        Assert.NotNull(GoogleGtxTranslator.Notice);

        handler.Hosts.Clear();
        Assert.Equal("Merhaba dünya", await translator.TranslateAsync("Hello breaker two"));
        Assert.Equal(["clients5.google.com"], handler.Hosts);

        // 10 dakika sonra ana uç nokta yeniden denenir.
        now += GoogleGtxTranslator.BlockDuration + TimeSpan.FromSeconds(1);
        handler.Hosts.Clear();
        await translator.TranslateAsync("Hello breaker three");
        Assert.Equal("translate.googleapis.com", handler.Hosts[0]);
        GoogleGtxTranslator.ResetBreaker();
    }

    /// Hiçbir uç nokta çeviri vermezse İngilizce Türkçe diye dönmez, hata fırlar.
    [Fact]
    public async Task FailureThrowsInsteadOfEchoingEnglish()
    {
        GoogleGtxTranslator.ResetBreaker();
        var handler = new Handler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var translator = new GoogleGtxTranslator("en", "tr", new HttpClient(handler), null);
        await Assert.ThrowsAsync<GameTranslator.Core.TranslatorException>(() => translator.TranslateAsync("Hello failing"));
        GoogleGtxTranslator.ResetBreaker();
    }
}
