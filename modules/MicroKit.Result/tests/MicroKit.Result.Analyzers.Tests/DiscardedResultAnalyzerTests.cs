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
            Result<int> Property { get; } = Result.Success(3);

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

    // #195 item 7: a computed property builds a new result at each read, so a side effect on it discards that result.
    [Fact]
    public Task MKR001_WhenTapIsCalledOnAComputedProperty_Reports() => Verify("""
        class Service
        {
            Result<int> Computed => Result.Success(1);
            Result<int> Block { get { return Result.Success(2); } }
            Result<int> Accessor { get => Result.Success(3); }
            static Result<int> StaticComputed => Result.Success(4);

            async Task Run(Service other)
            {
                {|MKR001:Computed.Tap(x => { })|};
                {|MKR001:Block.Tap(x => { })|};
                {|MKR001:Accessor.TapError(e => { })|};
                {|MKR001:StaticComputed.Finally(r => { })|};
                {|MKR001:other.Computed.Tap(x => { })|};
                {|MKR001:Computed.Tap(x => { }).TapError(e => { })|};
                {|MKR001:await Computed.TapAsync(x => Task.CompletedTask)|};
                {|MKR001:await Block.FinallyAsync(r => Task.CompletedTask).ConfigureAwait(false)|};
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnAnAutoProperty_DoesNotReport() => Verify("""
        class Service
        {
            Result<int> GetOnly { get; } = Result.Success(1);
            Result<int> GetSet { get; set; } = Result.Success(2);
            Result<int> GetInit { get; init; } = Result.Success(3);
            static Result<int> StaticAuto { get; } = Result.Success(4);

            async Task Run(Service other)
            {
                GetOnly.Tap(x => { });
                GetSet.TapError(e => { });
                GetInit.Finally(r => { });
                StaticAuto.Tap(x => { });
                other.GetOnly.Tap(x => { }).TapError(e => { });
                await GetSet.TapAsync(x => Task.CompletedTask);
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnAnIndexer_DoesNotReport() => Verify("""
        class Results
        {
            public Result<int> this[int index] => Result.Success(index);
        }
        class Service
        {
            void Run(Results results)
            {
                results[0].Tap(x => { });
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnAVirtualProperty_DoesNotReport() => Verify("""
        class Holder
        {
            public virtual Result<int> Current => Result.Success(1);
        }
        class CachingHolder : Holder
        {
            public override Result<int> Current { get; } = Result.Success(2);
        }
        class Service
        {
            void Run(CachingHolder holder, Holder any)
            {
                holder.Current.Tap(x => { });
                any.Current.Tap(x => { });
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnTheValueOfAKeyValuePair_DoesNotReport() => Verify("""
        class Service
        {
            void Run(System.Collections.Generic.Dictionary<int, Result<int>> results)
            {
                foreach (var kv in results)
                    kv.Value.Tap(x => { });
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnTheValueOfANullableHeldResult_DoesNotReport() => Verify("""
        class Service
        {
            private Result<int>? _cached;

            void Run(Result<int>? held)
            {
                _cached.Value.Tap(x => { });
                held.Value.Tap(x => { });
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenTapIsCalledOnAComputedPropertyThroughConditionalAccess_Reports() => Verify("""
        class Holder
        {
            public Result<int> Computed => Result.Success(1);
            public Result<int> Auto { get; } = Result.Success(2);
        }
        class Service
        {
            void Run(Holder? holder)
            {
                {|MKR001:holder?.Computed.Tap(x => { })|};
                holder?.Auto.Tap(x => { });
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
    public Task MKR001_WhenSideEffectGoesThroughAConditionalAccessOnAHeldResult_DoesNotReport() => Verify("""
        class Service
        {
            private Result<int>? _cached;

            void Run(Result<int>? held)
            {
                held?.Tap(x => { });
                _cached?.Tap(x => { }).TapError(e => { });
            }
        }
        """);

    [Fact]
    public Task MKR001_WhenSideEffectGoesThroughAConditionalAccessOnAFreshResult_Reports() => Verify("""
        class Service
        {
            Result<int>? Load() => Result.Success(1);
            void Run() { {|MKR001:Load()?.Tap(x => { })|}; }
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
