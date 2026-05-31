using System.Security.Claims;
using BenchmarkDotNet.Attributes;
using MediatR;
using MicroKit.MediatR;
using MicroKit.MediatR.Behaviors.DependencyInjection;
using MicroKit.Result;
using static MicroKit.Result.Result;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MicroKit.MediatR.PerformanceTests.Benchmarks;

/// <summary>
/// Measures the dispatch overhead of MicroKit.MediatR relative to raw MediatR.
///
/// Scenarios:
///   RawMediatR_NoMicroKit       — baseline: raw MediatR, no MicroKit pipeline
///   MicroKit_LoggingOnly        — MicroKit with only LoggingBehavior (always active)
///   MicroKit_FullPipeline_None  — all 6 behaviors registered; none apply (marker absent pass-through)
///
/// Budget (from .claude-context/standards/performance-budget.md):
///   Full pipeline overhead vs raw MediatR:  ≤ 250 ns / ≤ 64 bytes
///   Behavior pass-through (marker absent):  ≤ 10 ns / 0 bytes each
/// </summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn]
public class DispatchBenchmarks
{
    private ServiceProvider _rawProvider = null!;
    private ServiceProvider _loggingProvider = null!;
    private ServiceProvider _fullProvider = null!;

    private IMediator _rawMediator = null!;
    private IMediator _loggingMediator = null!;
    private IMediator _fullMediator = null!;

    [GlobalSetup]
    public void Setup()
    {
        _rawProvider = BuildRawProvider();
        _loggingProvider = BuildMicroKitProvider(loggingOnly: true);
        _fullProvider = BuildMicroKitProvider(full: true);

        _rawMediator = _rawProvider.GetRequiredService<IMediator>();
        _loggingMediator = _loggingProvider.GetRequiredService<IMediator>();
        _fullMediator = _fullProvider.GetRequiredService<IMediator>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rawProvider.Dispose();
        _loggingProvider.Dispose();
        _fullProvider.Dispose();
    }

    /// <summary>Baseline — raw MediatR IRequestHandler, no MicroKit pipeline or adapters.</summary>
    [Benchmark(Baseline = true)]
    public Task<string> RawMediatR_NoMicroKit()
        => _rawMediator.Send(new RawRequest());

    /// <summary>
    /// MicroKit dispatch with only LoggingBehavior active (order 100, always-on).
    /// Measures the logging overhead: BeginScope, two log calls, Stopwatch.
    /// </summary>
    [Benchmark]
    public ValueTask<Result<string>> MicroKit_LoggingBehavior_Only()
        => _loggingMediator.SendCommandAsync<BenchmarkCommand, Result<string>>(new BenchmarkCommand());

    /// <summary>
    /// Full pipeline: 6 behaviors registered, none apply to BenchmarkCommand (no opt-in markers).
    /// Measures: LoggingBehavior overhead + 5 × single-line marker guard (near-zero each).
    /// </summary>
    [Benchmark]
    public ValueTask<Result<string>> MicroKit_FullPipeline_NoMarkersActive()
        => _fullMediator.SendCommandAsync<BenchmarkCommand, Result<string>>(new BenchmarkCommand());

    // ── Service provider factories ─────────────────────────────────────────

    private static ServiceProvider BuildRawProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<RawRequestHandler>());
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildMicroKitProvider(bool loggingOnly = false, bool full = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ICurrentUserAccessor, NullCurrentUserAccessor>();
        services.AddSingleton<IAuthorizationService, AlwaysSucceedAuthService>();
        services.AddSingleton<IDistributedCache, NullDistributedCache>();
        services.AddSingleton(Options.Create(new System.Text.Json.JsonSerializerOptions()));

        services.AddMicroKitMediatR(cfg =>
        {
            cfg.FromAssemblyContaining<BenchmarkCommand>();
            if (loggingOnly)
            {
                cfg.AddLoggingBehavior();
            }
            if (full)
            {
                cfg.AddLoggingBehavior();
                cfg.AddAuthorizationBehavior();
                cfg.AddValidationBehavior();
                cfg.AddIdempotencyBehavior();
                cfg.AddCachingBehavior();
                cfg.AddRetryBehavior();
            }
        });

        return services.BuildServiceProvider();
    }

    // ── Test types ─────────────────────────────────────────────────────────

    // Baseline: raw IRequestHandler — bypasses MicroKit adapters entirely.
    internal sealed record RawRequest : IRequest<string>;

    internal sealed class RawRequestHandler : IRequestHandler<RawRequest, string>
    {
        public Task<string> Handle(RawRequest request, CancellationToken ct)
            => Task.FromResult("result");
    }

    // MicroKit command — no opt-in markers, so only LoggingBehavior applies.
    internal sealed record BenchmarkCommand : ICommand<Result<string>>;

    internal sealed class BenchmarkHandler : ICommandHandler<BenchmarkCommand, Result<string>>
    {
        public ValueTask<Result<string>> Handle(BenchmarkCommand command, CancellationToken ct = default)
            => new(Success("result"));
    }

    // ── Infrastructure stubs ───────────────────────────────────────────────

    private sealed class NullCurrentUserAccessor : ICurrentUserAccessor
    {
        public ClaimsPrincipal? Current => null;
    }

    private sealed class AlwaysSucceedAuthService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class NullDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default) => Task.CompletedTask;
    }
}
