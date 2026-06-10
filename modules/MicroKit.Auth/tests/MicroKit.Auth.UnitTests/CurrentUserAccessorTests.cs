namespace MicroKit.Auth.UnitTests;

public sealed class CurrentUserAccessorTests : IDisposable
{
    private readonly CurrentUserAccessor _sut = new();

    public void Dispose() => _sut.Clear();

    [Fact]
    public void Get_WhenNothingSet_ReturnsNull()
    {
        _sut.Get().ShouldBeNull();
    }

    [Fact]
    public void Get_AfterSet_ReturnsUser()
    {
        var user = FakeCurrentUserBuilder.Create().Build();

        _sut.Set(user);

        _sut.Get().ShouldBe(user);
    }

    [Fact]
    public void Get_AfterClear_ReturnsNull()
    {
        var user = FakeCurrentUserBuilder.Create().Build();
        _sut.Set(user);

        _sut.Clear();

        _sut.Get().ShouldBeNull();
    }

    [Fact]
    public async Task Set_InChildTask_DoesNotAffectParentContext()
    {
        // AsyncLocal changes in a child task do not propagate back to the parent scope
        var childUser = FakeCurrentUserBuilder.Create().Build();

        await Task.Run(() => _sut.Set(childUser));

        _sut.Get().ShouldBeNull();
    }

    [Fact]
    public async Task Set_InParent_IsVisibleInChildTask()
    {
        // AsyncLocal values set in the parent flow down to child tasks started after the Set
        var user = FakeCurrentUserBuilder.Create().Build();
        _sut.Set(user);

        ICurrentUser? seenInChild = null;
        await Task.Run(() => seenInChild = _sut.Get());

        seenInChild.ShouldBe(user);
    }

    [Fact]
    public async Task Set_AfterTaskRunScheduled_IsNotVisibleInChildTask()
    {
        // ExecutionContext is captured at Task.Run() call site — not at Set() call time.
        // A Set() made after scheduling writes into the parent's context only;
        // the child task already holds a snapshot where the user was absent.
        var user = FakeCurrentUserBuilder.Create().Build();
        ICurrentUser? seenInChild = null;

        var task = Task.Run(() => seenInChild = _sut.Get()); // snapshot taken here — user is null
        _sut.Set(user);                                       // written after the snapshot
        await task;

        seenInChild.ShouldBeNull();
    }
}
