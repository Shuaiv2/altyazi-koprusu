// SPDX-License-Identifier: AGPL-3.0-only

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using GameTranslator.Core;

namespace GameTranslator.Core.Tests;

/// <summary>Sağlayıcı adı ve uzaktan düzen protokolü (docs/uzaktan-duzen-protokolu.md; Mac: NativeSubtitleServer).</summary>
public class SubtitleServerLayoutTests
{
    private static readonly HttpClient Http = new();

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.True(condition());
    }

    private static async Task<List<string>> ReadUntil(StreamReader reader, Func<string, bool> stop)
    {
        var lines = new List<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await reader.ReadLineAsync(timeout.Token) is { } line)
        {
            lines.Add(line);
            if (stop(line)) break;
        }
        return lines;
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "text/plain");

    [Fact]
    public async Task ProviderIsInSubtitleAndStyleAndInitialFrame()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        server.SendStyle(new SubtitleStyle());
        server.Provider = "Google ücretsiz";

        using var response = await Http.GetAsync($"http://127.0.0.1:{port}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        // Yeni istemcinin ilk stil çerçevesi sağlayıcıyı taşır.
        var first = await ReadUntil(reader, l => l.StartsWith("data:") && l.Contains("fontSize"));
        Assert.Contains("\"provider\":\"Google ücretsiz\"", first[^1]);

        await WaitUntil(() => server.ConnectedClients == 1);
        server.Provider = "Gemini";
        var changed = await ReadUntil(reader, l => l.StartsWith("data:"));
        Assert.Contains("\"provider\":\"Gemini\"", changed[^1]);

        server.SendSubtitle("Merhaba");
        var sub = await ReadUntil(reader, l => l.StartsWith("data:"));
        Assert.Contains("\"provider\":\"Gemini\"", sub[^1]);
        Assert.Contains("\"text\":\"Merhaba\"", sub[^1]);
    }

    [Fact]
    public async Task EmptyProviderAddsNoField()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        server.SendStyle(new SubtitleStyle());
        using var response = await Http.GetAsync($"http://127.0.0.1:{port}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        var lines = await ReadUntil(reader, l => l.StartsWith("data:"));
        Assert.DoesNotContain("provider", lines[^1]);
    }

    [Fact]
    public async Task PostStoresLayoutAndGetReturnsIt()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        var changes = 0;
        server.TvStatusChanged += () => Interlocked.Increment(ref changes);

        using (var empty = await Http.GetAsync($"http://127.0.0.1:{port}/tv-layout"))
            Assert.Equal("{}", await empty.Content.ReadAsStringAsync());

        using var post = await Http.PostAsync($"http://127.0.0.1:{port}/tv-layout",
            Body("{\"settings\":{\"y\":5,\"x\":1,\"version\":1},\"source\":\"tv\",\"app\":\"1.2.0\"}"));
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await post.Content.ReadAsStringAsync());
        Assert.Equal("*", post.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(1, changes);

        using var get = await Http.GetAsync($"http://127.0.0.1:{port}/tv-layout");
        Assert.Equal("{\"version\":1,\"x\":1,\"y\":5}", await get.Content.ReadAsStringAsync());
        Assert.NotNull(server.LastTvLayout);
        Assert.NotNull(server.LastTvLayout!.Value.At);
    }

    [Fact]
    public async Task InvalidLayoutBodyIs400()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        foreach (var bad in new[] { "nonsense", "[]", "{\"settings\":5}", "{}" })
        {
            using var post = await Http.PostAsync($"http://127.0.0.1:{port}/tv-layout", Body(bad));
            Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
            Assert.Equal("{\"status\":\"error\"}", await post.Content.ReadAsStringAsync());
        }
        Assert.Null(server.LastTvLayout);
    }

    [Fact]
    public async Task OversizedBodyIs413()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        using var post = await Http.PostAsync($"http://127.0.0.1:{port}/tv-layout", Body("{\"settings\":{\"a\":\"" + new string('x', 70000) + "\"}}"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, post.StatusCode);
        Assert.Null(server.LastTvLayout);
    }

    [Fact]
    public async Task BodyArrivingInSeparatePacketsIsRead()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        var stream = tcp.GetStream();
        const string body = "{\"settings\":{\"x\":2}}";
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"POST /tv-layout HTTP/1.1\r\nHost: x\r\nContent-Length: {body.Length}\r\n\r\n"));
        await stream.FlushAsync();
        await Task.Delay(100);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(body));
        using var reader = new StreamReader(stream);
        Assert.Contains("200 OK", await reader.ReadLineAsync());
        Assert.Equal("{\"x\":2}", server.LastTvLayout!.Value.Json);
    }

    [Fact]
    public async Task OptionsAnswersCors()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        using var response = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Options, $"http://127.0.0.1:{port}/tv-layout"));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
    }

    [Fact]
    public async Task LastLayoutSurvivesRestartWhenStoreIsGiven()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gt-layout-{Guid.NewGuid():N}", "tv-layout.json");
        try
        {
            using (var server = new SubtitleServer(0, layoutStorePath: path))
            {
                var port = await server.StartAsync();
                using var post = await Http.PostAsync($"http://127.0.0.1:{port}/tv-layout", Body("{\"settings\":{\"fontSize\":60}}"));
                Assert.Equal(HttpStatusCode.OK, post.StatusCode);
            }
            using var again = new SubtitleServer(0, layoutStorePath: path);
            var port2 = await again.StartAsync();
            using var get = await Http.GetAsync($"http://127.0.0.1:{port2}/tv-layout");
            Assert.Equal("{\"fontSize\":60}", await get.Content.ReadAsStringAsync());
            Assert.NotNull(again.LastTvLayout!.Value.At);
        }
        finally
        {
            if (Directory.Exists(Path.GetDirectoryName(path))) Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public async Task SendLayoutBroadcastsEventWithIncreasingRev()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        using var response = await Http.GetAsync($"http://127.0.0.1:{port}/events", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        await WaitUntil(() => server.ConnectedClients == 1);

        server.SendLayout("preview", new JsonObject { ["x"] = 10, ["align"] = "center" });
        server.SendLayout("save", "{\"x\":11}");
        var lines = await ReadUntil(reader, l => l.Contains("\"mode\":\"save\""));
        Assert.Contains("event: layout", lines);
        var datas = lines.Where(l => l.StartsWith("data:") && l.Contains("\"mode\"")).Select(l => JsonNode.Parse(l[5..])!).ToList();
        Assert.Equal(2, datas.Count);
        Assert.Equal("preview", (string?)datas[0]["mode"]);
        Assert.Equal(10, (int?)datas[0]["settings"]!["x"]);
        Assert.Equal("save", (string?)datas[1]["mode"]);
        Assert.True((int?)datas[1]["rev"] > (int?)datas[0]["rev"]);
    }

    [Fact]
    public async Task ViewersDoNotCountAsTv()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        var changes = 0;
        server.TvStatusChanged += () => Interlocked.Increment(ref changes);
        Assert.False(server.TvConnected);

        using var viewer = await Http.GetAsync($"http://127.0.0.1:{port}/events?viewer=obs", HttpCompletionOption.ResponseHeadersRead);
        await Task.Delay(200);
        Assert.False(server.TvConnected);

        var tv = await Http.GetAsync($"http://127.0.0.1:{port}/events", HttpCompletionOption.ResponseHeadersRead);
        await WaitUntil(() => server.TvConnected);
        Assert.True(changes >= 1);
        var before = changes;
        tv.Dispose();
        await WaitUntil(() => !server.TvConnected);
        await WaitUntil(() => changes > before);
    }

    [Fact]
    public async Task TranslateTestIsOffWithoutDevEnvironment()
    {
        using var server = new SubtitleServer(0) { OnTranslateTest = text => Task.FromResult("{\"ok\":true}") };
        var port = await server.StartAsync();
        var dev = Environment.GetEnvironmentVariable("ALTYAZI_DEV") == "1";
        using var response = await Http.GetAsync($"http://127.0.0.1:{port}/translate-test?text=hello+world");
        Assert.Equal(dev ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExistingHelloAndEventsStillWork()
    {
        using var server = new SubtitleServer(0);
        var port = await server.StartAsync();
        using var hello = await Http.GetAsync($"http://127.0.0.1:{port}/hello");
        Assert.Contains("\"app\":\"gametranslator\"", await hello.Content.ReadAsStringAsync());
        using var missing = await Http.GetAsync($"http://127.0.0.1:{port}/nope");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
