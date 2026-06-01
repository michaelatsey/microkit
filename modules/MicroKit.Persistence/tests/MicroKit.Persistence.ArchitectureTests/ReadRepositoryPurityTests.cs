namespace MicroKit.Persistence.ArchitectureTests;

public sealed class ReadRepositoryPurityTests
{
    private static bool ExposesIQueryable(Type type)
    {
        var queryableType = typeof(IQueryable<>);
        return type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                   .Select(m => UnwrapReturnType(m.ReturnType))
                   .Any(t => t.IsGenericType && t.GetGenericTypeDefinition() == queryableType);

        static Type UnwrapReturnType(Type t)
        {
            if (!t.IsGenericType) return t;
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(Task<>) || def == typeof(ValueTask<>))
                return t.GetGenericArguments()[0];
            return t;
        }
    }

    // NOTE 1: The Abstractions marker IReadRepository<T> is an empty interface with no methods.
    [Fact]
    public void IReadRepository_AbstractionsMarker_HasZeroDeclaredMethods()
    {
        typeof(MicroKit.Persistence.Abstractions.IReadRepository<>).GetMethods().Length.ShouldBe(0);
    }

    // NOTE 1: Core's IReadRepository<T> must not declare any mutation methods.
    [Fact]
    public void IReadRepository_Core_HasNoMutationMethods()
    {
        var forbiddenNames = new[] { "CommitAsync", "AddAsync", "UpdateAsync", "DeleteAsync", "SaveChangesAsync" };
        var methods = typeof(MicroKit.Persistence.IReadRepository<>).GetMethods();
        var mutationMethods = methods.Where(m => forbiddenNames.Contains(m.Name)).ToList();
        mutationMethods.ShouldBeEmpty();
    }

    [Fact]
    public void InMemoryRepository_ExposesNoIQueryable()
    {
        ExposesIQueryable(typeof(MicroKit.Persistence.Testing.InMemoryRepository<>)).ShouldBeFalse();
    }

    // NOTE 12: InMemoryReadRepository must not leak IQueryable on any public method.
    [Fact]
    public void InMemoryReadRepository_ExposesNoIQueryable()
    {
        ExposesIQueryable(typeof(MicroKit.Persistence.Testing.InMemoryReadRepository<>)).ShouldBeFalse();
    }

    // Open generic form typeof(EfReadRepository<,>) used — two type params.
    [Fact]
    public void EfReadRepository_ExposesNoIQueryable()
    {
        ExposesIQueryable(typeof(MicroKit.Persistence.EntityFrameworkCore.EfReadRepository<,>)).ShouldBeFalse();
    }
}
