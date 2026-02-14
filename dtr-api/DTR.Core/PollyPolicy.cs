using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTR.Core;

public class PollyPolicy
{
    public AsyncRetryPolicy ImmediateRetryPolicy { get; } = Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(3, retryAttempt =>
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            (exception, timeSpan, retryCount, context) =>
            {
                Log.Logger.Warning("Retry {RetryCount} after {Delay}s due to: {Message}",
                    retryCount, timeSpan.TotalSeconds, exception.Message);
            });

    public AsyncRetryPolicy RetryPolicy { get; } = Policy
        .Handle<Exception>()
        .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

    // 2. The Circuit Breaker (Stop trying if things are broken)
    public AsyncCircuitBreakerPolicy CircuitBreakerPolicy { get; } = Policy
     .Handle<Exception>()
     .CircuitBreakerAsync(
         exceptionsAllowedBeforeBreaking: 5,
         durationOfBreak: TimeSpan.FromSeconds(30),
         onBreak: (ex, breakDelay) =>
         {
             Log.Logger.Fatal("Circuit Broken! Stopped for {Delay}s. Reason: {Msg}",
                 breakDelay.TotalSeconds, ex.Message);
         },
         onReset: () =>
         {
             Log.Logger.Information("Circuit Reset! Service has fully recovered.");
         },
         onHalfOpen: () =>
         {
             // This is the "Test Phase"
             Log.Logger.Warning("Circuit is Half-Open. Attempting a trial execution to check service health...");
         }
     );
    // 3. The Wrap (Combine them)
    public AsyncPolicy WrapPolicy => Policy.WrapAsync(RetryPolicy, CircuitBreakerPolicy);
}