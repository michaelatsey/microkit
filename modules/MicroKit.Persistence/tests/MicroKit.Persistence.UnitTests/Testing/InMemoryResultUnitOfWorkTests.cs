using MicroKit.Persistence.Abstractions;
using MicroKit.Persistence.Testing;

namespace MicroKit.Persistence.UnitTests.Testing;

using MicroKit.Result;

/// <summary>
/// #197 phase 3: the in-memory double of <see cref="IResultUnitOfWork"/> follows the EF Core rules — it
/// converts a version conflict once the operation it owns has failed, a failed Result counts no commit,
/// and a nested call converts nothing.
/// </summary>
public sealed class InMemoryResultUnitOfWorkTests
{
    private static readonly Error BusinessFailure = Error.BusinessRule(ErrorCode.From("ORDER.CLOSED"), "The order is closed.");

    private readonly InMemoryRepository<TestItem, TestItemId> _repo = new();
    private readonly InMemoryUnitOfWork _unitOfWork = new();

    [Fact]
    public void InMemoryUnitOfWork_ImplementsTheResultContract()
    {
        _unitOfWork.ShouldBeAssignableTo<IResultUnitOfWork>();
    }

    [Fact]
    public async Task FindAsync_WithAStaleVersion_ThrowsTheVersionConflictMarker()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);
        _repo.SetVersion(item, 3);

        var thrown = await Should.ThrowAsync<PersistenceException>(async () =>
            await _repo.FindAsync(item.Id, expectedVersion: 2));

        thrown.InnerException.ShouldBeOfType<AggregateVersionConflictException>().ErrorName.ShouldBe("TEST_ITEM");
    }

    [Fact]
    public async Task TryExecuteAsync_WhenTheFindIsStale_ReturnsTheConcurrencyErrorOfTheRoot()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);
        _repo.SetVersion(item, 3);

        var result = await _unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var found = await s.Repo.FindAsync(s.Id, expectedVersion: 2, ct);
                found!.Rename("Bob");
                await s.UnitOfWork.CommitAsync(ct);
                return Result.Success();
            },
            (Repo: _repo, UnitOfWork: _unitOfWork, item.Id));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(PersistenceErrors.Concurrency("TEST_ITEM"));
        _unitOfWork.CommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task TryExecuteAsync_OfAValue_WhenTheFindIsStale_ReturnsTheConcurrencyError()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);
        _repo.SetVersion(item, 3);

        var result = await _unitOfWork.TryExecuteAsync(
            static async (s, ct) =>
            {
                var found = await s.Repo.FindAsync(s.Id, expectedVersion: 1, ct);
                return Result.Success(found!.Name);
            },
            (Repo: _repo, item.Id));

        result.Error.Code.Value.ShouldBe("PERSISTENCE.TEST_ITEM.CONCURRENCY");
    }

    [Fact]
    public async Task TryExecuteAsync_WhenTheOperationSucceeds_CountsItsCommits()
    {
        var result = await _unitOfWork.TryExecuteAsync(
            static async (u, ct) =>
            {
                await u.CommitAsync(ct);
                return Result.Success(42);
            },
            _unitOfWork);

        result.Value.ShouldBe(42);
        _unitOfWork.CommitCount.ShouldBe(1);
    }

    [Fact]
    public async Task TryExecuteAsync_WhenTheOperationReturnsAFailure_ReturnsItAndCountsNoCommit()
    {
        var result = await _unitOfWork.TryExecuteAsync(
            static async (u, ct) =>
            {
                await u.CommitAsync(ct);
                return Result.Failure(BusinessFailure);
            },
            _unitOfWork);

        result.Error.ShouldBeSameAs(BusinessFailure);
        _unitOfWork.CommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task TryExecuteAsync_WhenTheOperationThrowsAnythingElse_PropagatesAndCountsNoCommit()
    {
        await Should.ThrowAsync<PersistenceException>(async () => await _unitOfWork.TryExecuteAsync<InMemoryUnitOfWork>(
            static async (u, ct) =>
            {
                await u.CommitAsync(ct);
                throw new PersistenceException("Database error during commit.");
            },
            _unitOfWork));

        _unitOfWork.CommitCount.ShouldBe(0);
    }

    [Fact]
    public async Task TryExecuteAsync_Nested_ConvertsNothing_AndTheOwnerConverts()
    {
        var item = new TestItem("Alice");
        await _repo.AddAsync(item);
        _repo.SetVersion(item, 3);
        PersistenceException? seenByTheOwner = null;

        var result = await _unitOfWork.TryExecuteAsync(
            async (s, ct) =>
            {
                try
                {
                    return await s.UnitOfWork.TryExecuteAsync(
                        static async (inner, token) =>
                        {
                            await inner.Repo.FindAsync(inner.Id, expectedVersion: 2, token);
                            return Result.Success();
                        },
                        s,
                        ct);
                }
                catch (PersistenceException ex)
                {
                    seenByTheOwner = ex;
                    throw;
                }
            },
            (Repo: _repo, UnitOfWork: _unitOfWork, item.Id));

        seenByTheOwner.ShouldNotBeNull();
        result.Error.ShouldBe(PersistenceErrors.Concurrency("TEST_ITEM"));
    }

    [Fact]
    public async Task TryExecuteAsync_Nested_ReturnsAFailureUnchanged_AndTheOwnerDecides()
    {
        var result = await _unitOfWork.TryExecuteAsync(
            static async (u, ct) =>
            {
                await u.CommitAsync(ct);
                var inner = await u.TryExecuteAsync(static (_, _) => Task.FromResult(Result.Failure(BusinessFailure)), 0, ct);
                inner.Error.ShouldBeSameAs(BusinessFailure);
                return Result.Success();
            },
            _unitOfWork);

        result.IsSuccess.ShouldBeTrue();
        _unitOfWork.CommitCount.ShouldBe(1);
    }

    [Fact]
    public async Task TryCommitAsync_OutsideAnOperation_CountsACommit()
    {
        (await _unitOfWork.TryCommitAsync()).IsSuccess.ShouldBeTrue();

        _unitOfWork.CommitCount.ShouldBe(1);
    }

    [Fact]
    public async Task TryCommitAsync_InsideAnOperation_IsRefused()
    {
        await Should.ThrowAsync<InvalidOperationException>(async () => await _unitOfWork.TryExecuteAsync(
            static async (u, ct) => await u.TryCommitAsync(ct),
            _unitOfWork));
    }

    [Fact]
    public async Task TryExecuteAsync_WithANullOperation_Throws()
    {
        await Should.ThrowAsync<ArgumentNullException>(async () =>
            await _unitOfWork.TryExecuteAsync<int>(null!, 0));
    }
}
