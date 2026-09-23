using Polly;
using Polly.Retry;
using Polly.Timeout;
using Polly.CircuitBreaker;
using openLuo.Modules.AppShell.Application;

namespace openLuo.Infrastructure.Resilience;

public class RetryPolicies(IRuntimeConfigCenter? configCenter = null)
{
    private readonly IRuntimeConfigCenter? _configCenter = configCenter;

    public ResiliencePipeline<T> LlmRetryPolicy<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = _configCenter?.GetSnapshot().Resilience.LlmMaxRetryAttempts ?? 3,
                Delay = TimeSpan.FromSeconds(_configCenter?.GetSnapshot().Resilience.LlmRetryDelaySeconds ?? 1),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
            })
            .AddTimeout(TimeSpan.FromSeconds(_configCenter?.GetSnapshot().Timeouts.ChatTimeoutSeconds ?? 120))
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<T>
            {
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(30),
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(60)
            })
            .Build();

    public ResiliencePipeline<T> DatabaseRetryPolicy<T>() =>
        new ResiliencePipelineBuilder<T>()
            .AddRetry(new RetryStrategyOptions<T>
            {
                MaxRetryAttempts = _configCenter?.GetSnapshot().Resilience.DatabaseMaxRetryAttempts ?? 2,
                Delay = TimeSpan.FromMilliseconds(_configCenter?.GetSnapshot().Resilience.DatabaseRetryDelayMilliseconds ?? 500),
                BackoffType = DelayBackoffType.Linear,
                ShouldHandle = new PredicateBuilder<T>()
                    .Handle<Microsoft.Data.Sqlite.SqliteException>()
            })
            .AddTimeout(TimeSpan.FromSeconds(_configCenter?.GetSnapshot().Timeouts.DefaultTimeoutSeconds ?? 30))
            .Build();
}
