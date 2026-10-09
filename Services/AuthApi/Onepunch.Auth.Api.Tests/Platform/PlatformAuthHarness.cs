using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Onepunch.Auth.Core.Platform;
using Onepunch.Auth.Domain.DTOs;
using Onepunch.Auth.Domain.Entities;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib.Platform;
using System.Text;

namespace Onepunch.Auth.Api.Tests.Platform;

/// <summary>
/// A clock the tests move by hand. Starts at the real "now": ASP.NET's JWT validation reads the
/// system clock, so tokens minted against a clock in the past would look expired to it.
/// </summary>
public sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>
/// Wires the real platform sign-in pieces (service, tokens, keys, TOTP) over an in-memory database
/// and a manual clock, so a whole sign-in can be driven end to end without a web host.
/// </summary>
public sealed class PlatformAuthHarness : IDisposable
{
    public const string StrongPassword = "Correct-Horse-Battery-9";
    public const string NewStrongPassword = "Another-Strong-Pass-7!";

    private readonly string _keyDirectory = Path.Combine(Path.GetTempPath(), "platform-key-" + Guid.NewGuid().ToString("N"));
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly EphemeralDataProtectionProvider _dataProtection = new();

    public ManualTime Time { get; } = new();
    public PlatformAuthSettings Settings { get; } = new();
    public PlatformRsaKeyProvider Keys { get; }
    public PlatformTokenService Tokens { get; }

    public PlatformAuthHarness()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformAuth:SigningKeyPath"] = Path.Combine(_keyDirectory, "platform-signing.key"),
            })
            .Build();
        Keys = new PlatformRsaKeyProvider(configuration, _dataProtection);
        Tokens = new PlatformTokenService(Keys, Options.Create(Settings), Time);
    }

    public PlatformAuthContext NewContext()
    {
        var options = new DbContextOptionsBuilder<PlatformAuthContext>()
            .UseInMemoryDatabase(_databaseName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new PlatformAuthContext(options);
    }

    public PlatformAuthService NewService(PlatformAuthContext? context = null) => new(
        context ?? NewContext(), Tokens, _dataProtection, Options.Create(Settings), Time,
        NullLogger<PlatformAuthService>.Instance);

    /// <summary>An operator with a (strong) password who has not enrolled an authenticator yet.</summary>
    public async Task<PlatformUser> SeedUserAsync(
        string email = "ops@example.com", string role = PlatformRoles.Billing, bool mustChangePassword = false)
    {
        var service = NewService();
        var user = await service.CreateUserAsync(email, "Ops User", role, StrongPassword, "test", CancellationToken.None);
        if (!mustChangePassword)
        {
            await using var db = NewContext();
            var stored = await db.PlatformUsers.SingleAsync(x => x.Id == user.Id);
            stored.MustChangePassword = false;
            await db.SaveChangesAsync();
        }
        return user;
    }

    /// <summary>Password, enrolment (if needed) and code: the full happy path, returning the session.</summary>
    public async Task<PlatformSession> SignInAsync(PlatformAuthService service, string email = "ops@example.com", string password = StrongPassword)
    {
        var challenge = await service.LoginAsync(email, password, CancellationToken.None);
        string? secretBase32 = null;
        if (challenge.Step == PlatformLoginSteps.MfaSetup)
            secretBase32 = (await service.GetMfaSetupAsync(challenge.ChallengeToken, CancellationToken.None)).Secret;

        secretBase32 ??= _knownSecrets.GetValueOrDefault(email);
        _knownSecrets[email] = secretBase32!;
        return await service.VerifyMfaAsync(challenge.ChallengeToken, CodeFor(secretBase32!), CancellationToken.None);
    }

    private readonly Dictionary<string, string> _knownSecrets = new();

    /// <summary>The code the operator's authenticator app would show right now.</summary>
    public string CodeFor(string secretBase32, int stepOffset = 0) =>
        Totp.ComputeCode(Base32Decode(secretBase32), Totp.StepOf(Time.GetUtcNow()) + stepOffset);

    public static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bitsLeft = 0;
        foreach (var c in value.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }
        return bytes.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_keyDirectory)) Directory.Delete(_keyDirectory, recursive: true);
    }
}
