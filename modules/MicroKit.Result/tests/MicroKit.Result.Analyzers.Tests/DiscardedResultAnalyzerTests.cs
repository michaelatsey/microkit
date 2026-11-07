using MicroKit.Result.Analyzers.Tests.Stubs;

namespace MicroKit.Result.Analyzers.Tests;

public sealed class DiscardedResultAnalyzerTests
{
    private static Task Verify(string code) =>
        new CSharpAnalyzerTest<DiscardedResultAnalyzer, CompatXUnitVerifier> { TestCode = ResultStubs.All + code }.RunAsync();

    [Fact]
    public Task MKR001_WhenCallReturningResultIsAStatement_Reports() => Verify("""
        class Service
        {
            Result Save() => Result.Success();
            void Run() { {|MKR001:Save()|}; }
        }
        """);

    [Fact]
    public Task MKR001_WhenCallReturningGenericResultIsAStatement_Reports() => Verify("""
        class Service
        {
            Result<int> Load() => Result.Success(1);
            void Run() { {|MKR001:Load().Map(x => x + 1)|}; }
        }
        """);

    [Fact]
    public Task MKR001_WhenAwaitedResultIsDiscarded_Reports() => Verify("""
        class Service
        {
            Task<Result> SaveAsync() => Task.FromResult(Result.Success());
            async Task Run() { {|MKR001:await SaveAsync()|}; }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnAFreshResult_Reports() => Verify("""
        class Service
        {
            Result<int> Load() => Result.Success(1);
            void Run() { {|MKR001:Load().Tap(x => System.Console.WriteLine(x))|}; }
        }
        """);

    [Fact]
    public Task MKR001_WhenResultIsAssignedReturnedOrExplicitlyDiscarded_DoesNotReport() => Verify("""
        class Service
        {
            Result Save() => Result.Success();
            Result Run()
            {
                var kept = Save();
                _ = Save();
                return kept;
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnAHeldResult_DoesNotReport() => Verify("""
        class Service
        {
            private readonly Result<int> _field = Result.Success(1);
            Result<int> Property => _field;

            async Task Run(Result<int> parameter)
            {
                var local = Result.Success(2);
                local.Tap(x => System.Console.WriteLine(x));
                parameter.Tap(x => System.Console.WriteLine(x));
                _field.Tap(x => System.Console.WriteLine(x));
                Property.Tap(x => System.Console.WriteLine(x));
                await local.TapAsync(x => Task.CompletedTask);
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenStatementIsNotAResult_DoesNotReport() => Verify("""
        class Service
        {
            int Count() => 1;
            Task WorkAsync() => Task.CompletedTask;
            async Task Run()
            {
                Count();
                await WorkAsync();
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenSideEffectIsAwaitedWithConfigureAwaitOnAHeldResult_DoesNotReport() => Verify("""
        class Service
        {
            async Task Run(Result<int> held)
            {
                await held.TapAsync(x => Task.CompletedTask).ConfigureAwait(false);
                await held.FinallyAsync(r => Task.CompletedTask).ConfigureAwait(false);
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenSideEffectsAreChainedOnAHeldResult_DoesNotReport() => Verify("""
        class Service
        {
            void Run(Result<int> held, Result command, Result<int>[] all)
            {
                held.Tap(x => { }).TapError(e => { }).Finally(r => { });
                command.TapError(e => { }).Finally(r => { });
                all[0].Tap(x => { });
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenResultIsBehindAConditionalAccess_Reports() => Verify("""
        class Repository { public Result Save() => Result.Success(); }
        class Service
        {
            void Run(Repository? repository) { {|MKR001:repository?.Save()|}; }
        }
        """);

    [Fact]
    public Task MKR001_WhenTaskOfResultIsDiscardedOutsideAnAsyncContext_Reports() => Verify("""
        class Service
        {
            Task<Result> SaveAsync(int i) => Task.FromResult(Result.Success());
            void Run(System.Collections.Generic.List<int> items)
            {
                {|MKR001:SaveAsync(1)|};
                items.ForEach(i => {|MKR001:SaveAsync(i)|});
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenDiscardedInALambdaOrAnExpressionBodiedMethod_Reports() => Verify("""
        class Service
        {
            Result Save(int i) => Result.Success();
            void Each(System.Collections.Generic.List<int> items) => items.ForEach(i => {|MKR001:Save(i)|});
            void Run() => {|MKR001:Save(0)|};
        }
        """);

    [Fact]
    public Task MKR001_WhenValueTaskOfResultIsDiscardedOutsideAnAsyncContext_Reports() => Verify("""
        class Service
        {
            ValueTask<Result<int>> LoadAsync() => new(Result.Success(1));
            void Run(Result<int> held)
            {
                {|MKR001:LoadAsync()|};
                {|MKR001:held.TapAsync(x => Task.CompletedTask)|};
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTaskOfResultIsDiscardedThroughConditionalAccessInAsyncMethod_Reports() => Verify("""
        class Repository { public Task<Result> SaveAsync() => Task.FromResult(Result.Success()); }
        class Service
        {
            async Task Run(Repository? repository)
            {
                await Task.Yield();
                {|MKR001:repository?.SaveAsync()|};
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenAUserMethodIsNamedLikeASideEffect_Reports() => Verify("""
        static class UserExtensions
        {
            public static Result Finally(this Result result, Func<Result> next) => result;
        }
        class Service
        {
            Result Save() => Result.Success();
            void Run(Result command) { {|MKR001:command.Finally(() => Save())|}; }
        }
        """);

    [Fact]
    public Task MKR001_WhenCodeIsGenerated_DoesNotReport()
    {
        var test = new CSharpAnalyzerTest<DiscardedResultAnalyzer, CompatXUnitVerifier>();
        test.TestState.Sources.Add(("Stubs.cs", ResultStubs.All));
        test.TestState.Sources.Add(("Generated.g.cs", """
            using MicroKit.Result;

            class Generated
            {
                Result Save() => Result.Success();
                void Run() { Save(); }
            }
            """));
        return test.RunAsync();
    }
}
