using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Security;
using Xunit;

namespace TenantStoreApi.Tests.Security;

public class SigningKeyResolverTests
{
    private sealed class StoreWithoutKeys : IJwksStore
    {
        public string? GetJwks() => null;
        public void SetJwks(string json, TimeSpan ttl) { }
        public bool IsFetchFailing() => true; // no fetch attempted
        public void MarkFetchFailed(TimeSpan ttl) { }
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no fetch expected");
    }

    private static JwksKeyCache EmptyCache() => new(new NoHttp(), new StoreWithoutKeys(), "http://auth/");

    [Fact]
    public void LegacyHmacAllowed_AddsTheSharedSecretKey()
    {
        var resolver = JwksServiceCollectionExtensions.CreateSigningKeyResolver(EmptyCache(), "a-legacy-signing-key-of-sufficient-length", allowLegacyHmac: true);

        resolver("t", null!, "kid", new TokenValidationParameters())
            .Should().ContainSingle().Which.Should().BeOfType<SymmetricSecurityKey>();
    }

    [Fact]
    public void LegacyHmacDisabled_OnlyJwksKeys()
    {
        var resolver = JwksServiceCollectionExtensions.CreateSigningKeyResolver(EmptyCache(), "a-legacy-signing-key-of-sufficient-length", allowLegacyHmac: false);

        resolver("t", null!, "kid", new TokenValidationParameters()).Should().BeEmpty();
    }

    [Fact]
    public void LegacyKey_IsCreatedOnce_NotPerValidation()
    {
        var resolver = JwksServiceCollectionExtensions.CreateSigningKeyResolver(EmptyCache(), "a-legacy-signing-key-of-sufficient-length", allowLegacyHmac: true);

        var first = resolver("t", null!, "kid", new TokenValidationParameters()).Single();
        var second = resolver("t", null!, "kid", new TokenValidationParameters()).Single();

        second.Should().BeSameAs(first);
    }
}
