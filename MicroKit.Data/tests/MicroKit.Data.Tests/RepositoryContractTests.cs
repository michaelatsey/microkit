using MicroKit.Data.Abstractions;
using System.Linq.Expressions;

namespace MicroKit.Data.Tests;

public sealed class RepositoryContractTests
{
    private sealed class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
    }

    /// <summary>Simple in-memory fake — no infrastructure required.</summary>
    private sealed class FakeProductRepository : IRepository<Product>
    {
        private readonly List<Product> _store = [];

        public Task<Product?> FindByIdAsync(object id, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(p => p.Id == (int)id));

        public Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<Product>>(_store.AsReadOnly());

        public Task<IReadOnlyCollection<Product>> FindAsync(
            Expression<Func<Product, bool>> predicate, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyCollection<Product>>(
                _store.Where(predicate.Compile()).ToList().AsReadOnly());

        public Task<bool> ExistsAsync(
            Expression<Func<Product, bool>> predicate, CancellationToken ct = default) =>
            Task.FromResult(_store.Any(predicate.Compile()));

        public Task AddAsync(Product entity, CancellationToken ct = default)
        {
            _store.Add(entity);
            return Task.CompletedTask;
        }

        public Task AddRangeAsync(IEnumerable<Product> entities, CancellationToken ct = default)
        {
            _store.AddRange(entities);
            return Task.CompletedTask;
        }

        public void Update(Product entity)
        {
            var idx = _store.FindIndex(p => p.Id == entity.Id);
            if (idx >= 0) _store[idx] = entity;
        }

        public void Remove(Product entity) => _store.Remove(entity);

        public void RemoveRange(IEnumerable<Product> entities)
        {
            foreach (var e in entities.ToList()) _store.Remove(e);
        }
    }

    private readonly FakeProductRepository _repo = new();

    [Fact]
    public async Task FindByIdAsync_ExistingId_ReturnsEntity()
    {
        var product = new Product { Id = 1, Name = "Widget", Price = 9.99m };
        await _repo.AddAsync(product);

        var result = await _repo.FindByIdAsync(1);

        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
    }

    [Fact]
    public async Task FindByIdAsync_MissingId_ReturnsNull()
    {
        var result = await _repo.FindByIdAsync(99);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllEntities()
    {
        await _repo.AddRangeAsync([
            new Product { Id = 1, Name = "A" },
            new Product { Id = 2, Name = "B" }
        ]);

        var result = await _repo.GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task FindAsync_WithPredicate_ReturnsMatchingEntities()
    {
        await _repo.AddRangeAsync([
            new Product { Id = 1, Name = "Match" },
            new Product { Id = 2, Name = "Other" }
        ]);

        var result = await _repo.FindAsync(p => p.Name == "Match");

        Assert.Single(result);
        Assert.Equal("Match", result.First().Name);
    }

    [Fact]
    public async Task ExistsAsync_MatchingEntity_ReturnsTrue()
    {
        await _repo.AddAsync(new Product { Id = 1, Name = "Existing" });

        Assert.True(await _repo.ExistsAsync(p => p.Id == 1));
        Assert.False(await _repo.ExistsAsync(p => p.Id == 99));
    }

    [Fact]
    public async Task Update_ModifiesEntity()
    {
        await _repo.AddAsync(new Product { Id = 1, Name = "Old" });

        _repo.Update(new Product { Id = 1, Name = "New" });
        var result = await _repo.FindByIdAsync(1);

        Assert.Equal("New", result!.Name);
    }

    [Fact]
    public async Task Remove_DeletesEntity()
    {
        var product = new Product { Id = 1, Name = "ToDelete" };
        await _repo.AddAsync(product);

        _repo.Remove(product);

        Assert.Null(await _repo.FindByIdAsync(1));
    }

    [Fact]
    public async Task RemoveRange_DeletesAll()
    {
        var products = new List<Product>
        {
            new() { Id = 1, Name = "A" },
            new() { Id = 2, Name = "B" }
        };
        await _repo.AddRangeAsync(products);

        _repo.RemoveRange(products);

        Assert.Empty(await _repo.GetAllAsync());
    }
}
