using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Security;
using System.Net;
using System.Security.Cryptography;
using Xunit;

namespace TenantStoreApi.Tests.Security;

/// <summary>
/// Onepunch.Common.Lib.Security.JwksKeyCache (used by TenantApi and NotificationApi) replaced an inline JWT key resolver that made a blocking HTTP call on every
/// request once its cache went stale and never backed off after a failure -- while the Auth
/// service was slow or down, every request blocked on its own fetch and requests (and memory)
/// piled up. Keys now come from memory, then the shared (Redis) copy, then one fetch at a time.
/// </summary>
public class JwksKeyCacheTests
{
    private const string AuthUrl = "http://auth-api:8080/";

    private static string JwksJson(string kid)
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        return $$"""{"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{kid}}","n":"{{Base64UrlEncoder.Encode(p.Modulus)}}","e":"{{Base64UrlEncoder.Encode(p.Exponent)}}"}]}""";
    }

    private static (JwksKeyCache Cache, CountingHandler Handler, InMemoryJwksStore Store, ManualTime Time) Build(
        Func<HttpResponseMessage> respond, TimeSpan? delay = null)
    {
        var handler = new CountingHandler(respond, delay ?? TimeSpan.Zero);
        var factory = new FakeHttpClientFactory(handler);
        var store = new InMemoryJwksStore();
        var time = new ManualTime();
        return (new JwksKeyCache(factory, store, AuthUrl, time), handler, store, time);
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    [Fact]
    public void FirstCall_FetchesOnce_AndSharesTheDocument()
    {
        var json = JwksJson("k1");
        var (cache, handler, store, _) = Build(() => Ok(json));

        var keys = cache.GetKeys();

        keys.Should().ContainSingle().Which.KeyId.Should().Be("k1");
        handler.Calls.Should().Be(1);
        handler.LastUri.Should().Be("http://auth-api:8080/.well-known/jwks.json");
        store.Jwks.Should().Be(json, "other instances read it from Redis instead of calling Auth");
    }

    [Fact]
    public void WithinTheLocalTtl_NeverLeavesTheProcess()
    {
        var (cache, handler, _, time) = Build(() => Ok(JwksJson("k1")));
        cache.GetKeys();

        time.Advance(JwksKeyCache.LocalTtl - TimeSpan.FromSeconds(1));
        cache.GetKeys();
        cache.GetKeys();

        handler.Calls.Should().Be(1);
    }

    [Fact]
    public void SharedCopyPresent_IsUsedWithoutCallingAuth()
    {
        var (cache, handler, store, _) = Build(() => Ok(JwksJson("fetched")));
        store.Jwks = JwksJson("shared");

        cache.GetKeys().Should().ContainSingle().Which.KeyId.Should().Be("shared");
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public void FailedFetch_BacksOff_AndKeepsServingTheLastKnownKeys()
    {
        var healthy = true;
        var (cache, handler, store, time) = Build(() => healthy ? Ok(JwksJson("k1")) : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        cache.GetKeys();
        store.Jwks = null; // shared copy expired
        healthy = false;

        time.Advance(JwksKeyCache.LocalTtl + TimeSpan.FromSeconds(1));
        var duringOutage = cache.GetKeys();

        duringOutage.Should().ContainSingle().Which.KeyId.Should().Be("k1");
        store.FetchFailing.Should().BeTrue("other instances skip the fetch too");
        handler.Calls.Should().Be(2);

        // Repeated requests inside the backoff window don't retry.
        for (var i = 0; i < 20; i++) cache.GetKeys();
        handler.Calls.Should().Be(2);
    }

    [Fact]
    public void AnotherInstanceMarkedAuthDown_NoFetchIsAttempted()
    {
        var (cache, handler, store, _) = Build(() => Ok(JwksJson("k1")));
        store.FetchFailing = true;

        cache.GetKeys().Should().BeEmpty();
        handler.Calls.Should().Be(0);
    }

    [Fact]
    public void MalformedDocument_CountsAsAFailedFetch()
    {
        var (cache, _, store, _) = Build(() => Ok("not json"));

        cache.GetKeys().Should().BeEmpty();
        store.Jwks.Should().BeNull("a bad document must never be shared");
        store.FetchFailing.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentColdStart_OnlyOneRequestCallsAuth()
    {
        var (cache, handler, _, _) = Build(() => Ok(JwksJson("k1")), delay: TimeSpan.FromMilliseconds(300));

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(cache.GetKeys)));

        handler.Calls.Should().Be(1);
    }

    private sealed class CountingHandler(Func<HttpResponseMessage> respond, TimeSpan delay) : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;
        public string? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastUri = request.RequestUri?.ToString();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            return respond();
        }
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class InMemoryJwksStore : IJwksStore
    {
        public string? Jwks { get; set; }
        public bool FetchFailing { get; set; }
        public string? GetJwks() => Jwks;
        public void SetJwks(string json, TimeSpan ttl) => Jwks = json;
        public bool IsFetchFailing() => FetchFailing;
        public void MarkFetchFailed(TimeSpan ttl) => FetchFailing = true;
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
