using Microsoft.Extensions.DependencyInjection;
using Polly;
using Serilog;
using System.Collections.Concurrent;

namespace Onepunch.Common.Lib;
public interface IPollyPolicyFactory
{
    IAsyncPolicy GetHttpPolicy(string key);
}

public class PollyPolicyFactory : IPollyPolicyFactory
{
    private readonly ConcurrentDictionary<string, IAsyncPolicy> _policyRegistry = new();

    public IAsyncPolicy GetHttpPolicy(string key)
    {
        return _policyRegistry.GetOrAdd(key, _ => CreatePolicy(key));
    }
    private IAsyncPolicy CreatePolicy(string key)
    {
        var retry = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (ex, time, retryCount, context) =>
                {
                    Log.Warning("Retry {Count} for [{Key}] due to: {Msg}", retryCount, key, ex.Message);
                });

        var circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, breakDelay) =>
                    Log.Fatal("Circuit [{Key}] OPEN for {Delay}s. Reason: {Msg}", key, breakDelay.TotalSeconds, ex.Message),
                onReset: () => Log.Information("Circuit [{Key}] RESET (Closed).", key),
                onHalfOpen: () => Log.Warning("Circuit [{Key}] HALF-OPEN. Testing next request...", key)
            );

        // Wrap: Retry is the outer layer, Circuit Breaker is the inner layer.
        return Policy.WrapAsync(retry, circuitBreaker);
    }
}

public static class PollyServiceExtensions
{
    public static IServiceCollection AddPollyPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IPollyPolicyFactory, PollyPolicyFactory>();
        return services;
    }
}
