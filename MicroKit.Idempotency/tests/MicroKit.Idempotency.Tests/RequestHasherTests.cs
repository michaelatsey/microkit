using System.Text.Json;
using MicroKit.Abstractions.Serialization;
using MicroKit.Idempotency.Abstractions.Contracts;
using MicroKit.Idempotency.Core.Hashing;
using MicroKit.Idempotency.Tests.Commands;
using Moq;

namespace MicroKit.Idempotency.Tests;

/// <summary>Unit tests for <see cref="RequestHasher"/>.</summary>
public sealed class RequestHasherTests
{
    private readonly Mock<IMicroKitSerializer> _serializerMock = new();
    private readonly IRequestHasher _hasher;

    /// <summary>Initializes a new instance.</summary>
    public RequestHasherTests()
    {
        _serializerMock.Setup(s => s.Serialize(It.IsAny<TestCommand>()))
            .Returns((TestCommand cmd) => JsonSerializer.Serialize(cmd));

        _hasher = new RequestHasher(_serializerMock.Object);
    }

    /// <summary>Verifies that hashing the same input twice yields identical results.</summary>
    [Fact]
    public void ComputeHash_SameInput_ReturnsSameHash()
    {
        var cmd = new TestCommand { IdempotencyKey = "k1", Data = "hello" };

        var hash1 = _hasher.ComputeHash(cmd);
        var hash2 = _hasher.ComputeHash(cmd);

        Assert.Equal(hash1, hash2);
    }

    /// <summary>Verifies that different inputs produce different hashes.</summary>
    [Fact]
    public void ComputeHash_DifferentInputs_ReturnsDifferentHashes()
    {
        var cmd1 = new TestCommand { IdempotencyKey = "k1", Data = "aaa" };
        var cmd2 = new TestCommand { IdempotencyKey = "k1", Data = "bbb" };

        var hash1 = _hasher.ComputeHash(cmd1);
        var hash2 = _hasher.ComputeHash(cmd2);

        Assert.NotEqual(hash1, hash2);
    }

    /// <summary>Verifies that the hash output is a valid Base64-encoded SHA-256 value.</summary>
    [Fact]
    public void ComputeHash_ReturnsBase64String()
    {
        var cmd = new TestCommand { IdempotencyKey = "k1", Data = "data" };

        var hash = _hasher.ComputeHash(cmd);
        var bytes = Convert.FromBase64String(hash);

        Assert.Equal(32, bytes.Length); // SHA-256 = 32 bytes
    }

    /// <summary>Verifies that the normalizer function output is used as the hash input.</summary>
    [Fact]
    public void ComputeHash_WithNormalizer_UsesNormalizerOutput()
    {
        var cmd = new TestCommand { IdempotencyKey = "k1", Data = "data" };

        var hash1 = _hasher.ComputeHash(cmd, _ => "canonical");
        var hash2 = _hasher.ComputeHash(cmd, _ => "canonical");
        var hash3 = _hasher.ComputeHash(cmd, _ => "different");

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, hash3);
    }
}
