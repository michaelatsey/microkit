namespace MicroKit.Auth.UnitTests.Testing;

public sealed class FakeCurrentUserAccessorTests
{
    [Fact]
    public void Get_WhenNothingSet_ReturnsNull()
    {
        var accessor = new FakeCurrentUserAccessor();
        accessor.Get().ShouldBeNull();
    }

    [Fact]
    public void Set_ThenGet_ReturnsUser()
    {
        var accessor = new FakeCurrentUserAccessor();
        var user = new FakeCurrentUser();

        accessor.Set(user);

        accessor.Get().ShouldBe(user);
    }

    [Fact]
    public void Clear_AfterSet_ReturnsNull()
    {
        var accessor = new FakeCurrentUserAccessor();
        accessor.Set(new FakeCurrentUser());

        accessor.Clear();

        accessor.Get().ShouldBeNull();
    }

    [Fact]
    public void CreateScope_RestoresPreviousUserOnDispose()
    {
        var accessor = new FakeCurrentUserAccessor();
        var original = new FakeCurrentUser();
        var scoped = new FakeCurrentUser();

        accessor.Set(original);

        using (accessor.CreateScope(scoped))
        {
            accessor.Get().ShouldBe(scoped);
        }

        accessor.Get().ShouldBe(original);
    }

    [Fact]
    public void CreateScope_NestedScopes_RestoresCorrectlyLIFO()
    {
        var accessor = new FakeCurrentUserAccessor();
        var first = new FakeCurrentUser();
        var second = new FakeCurrentUser();
        var third = new FakeCurrentUser();

        accessor.Set(first);

        var scope1 = accessor.CreateScope(second);
        var scope2 = accessor.CreateScope(third);

        // While in scope2, should see third user
        accessor.Get().ShouldBe(third);

        // After disposing scope2, should see second user
        scope2.Dispose();
        accessor.Get().ShouldBe(second);

        // After disposing scope1, should see first user
        scope1.Dispose();
        accessor.Get().ShouldBe(first);
    }

    [Fact]
    public void CreateScope_Dispose_WhenCalledTwice_IsIdempotent()
    {
        var accessor = new FakeCurrentUserAccessor();
        var original = new FakeCurrentUser();
        var scoped = new FakeCurrentUser();

        accessor.Set(original);

        var scope = accessor.CreateScope(scoped);
        scope.Dispose();
        scope.Dispose(); // second dispose must not throw or change state

        accessor.Get().ShouldBe(original);
    }
}
