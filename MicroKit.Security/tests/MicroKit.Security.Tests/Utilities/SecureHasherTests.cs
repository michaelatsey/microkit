using MicroKit.Security.Abstractions.Enums;
using MicroKit.Security.Core.Utilities;
using Xunit;

namespace MicroKit.Security.Tests.Utilities;

public sealed class SecureHasherTests
{
    [Fact]
    public void TryComputeSha256_WritesExactly64HexChars()
    {
        Span<char> dest = stackalloc char[64];
        bool result = SecureHasher.TryComputeSha256("mk_test_key".AsSpan(), dest);

        Assert.True(result);
        Assert.Equal(64, dest.Length);
        Assert.All(dest.ToArray(), c => Assert.Contains(c, "0123456789abcdefABCDEF"));
    }

    [Fact]
    public void TryComputeSha512_WritesExactly128HexChars()
    {
        Span<char> dest = stackalloc char[128];
        bool result = SecureHasher.TryComputeSha512("mk_test_key".AsSpan(), dest);

        Assert.True(result);
        Assert.Equal(128, dest.Length);
        Assert.All(dest.ToArray(), c => Assert.Contains(c, "0123456789abcdefABCDEF"));
    }

    [Fact]
    public void TryComputeHash_WithSha256Algorithm_ProducesSameResultAsTryComputeSha256()
    {
        Span<char> dest1 = stackalloc char[64];
        Span<char> dest2 = stackalloc char[64];

        SecureHasher.TryComputeSha256("key".AsSpan(), dest1);
        SecureHasher.TryComputeHash("key".AsSpan(), dest2, ApiKeyHashAlgorithms.SHA256);

        Assert.True(dest1.SequenceEqual(dest2));
    }

    [Fact]
    public void TryComputeHash_WithSha512Algorithm_ProducesSameResultAsTryComputeSha512()
    {
        Span<char> dest1 = stackalloc char[128];
        Span<char> dest2 = stackalloc char[128];

        SecureHasher.TryComputeSha512("key".AsSpan(), dest1);
        SecureHasher.TryComputeHash("key".AsSpan(), dest2, ApiKeyHashAlgorithms.SHA512);

        Assert.True(dest1.SequenceEqual(dest2));
    }

    [Fact]
    public void TryComputeSha256_IsDeterministic()
    {
        Span<char> dest1 = stackalloc char[64];
        Span<char> dest2 = stackalloc char[64];

        SecureHasher.TryComputeSha256("same_input".AsSpan(), dest1);
        SecureHasher.TryComputeSha256("same_input".AsSpan(), dest2);

        Assert.True(dest1.SequenceEqual(dest2));
    }

    [Fact]
    public void TryComputeSha256_DifferentInputs_ProduceDifferentHashes()
    {
        Span<char> dest1 = stackalloc char[64];
        Span<char> dest2 = stackalloc char[64];

        SecureHasher.TryComputeSha256("key_a".AsSpan(), dest1);
        SecureHasher.TryComputeSha256("key_b".AsSpan(), dest2);

        Assert.False(dest1.SequenceEqual(dest2));
    }

    [Fact]
    public void TryComputeSha256_DestinationTooSmall_ReturnsFalse()
    {
        Span<char> tooSmall = stackalloc char[32]; // SHA-256 needs 64
        bool result = SecureHasher.TryComputeSha256("key".AsSpan(), tooSmall);
        Assert.False(result);
    }

    [Fact]
    public void TryComputeSha512_DestinationTooSmall_ReturnsFalse()
    {
        Span<char> tooSmall = stackalloc char[64]; // SHA-512 needs 128
        bool result = SecureHasher.TryComputeSha512("key".AsSpan(), tooSmall);
        Assert.False(result);
    }
}
