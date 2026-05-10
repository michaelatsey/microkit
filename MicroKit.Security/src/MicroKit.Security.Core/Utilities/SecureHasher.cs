
using MicroKit.Security.Abstractions.Enums;
using System.Buffers; // Pour ArrayPool
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace MicroKit.Security.Core.Utilities;
public static class SecureHasher
{
    public static bool TryComputeHash(ReadOnlySpan<char> input, Span<char> destination, ApiKeyHashAlgorithms algorithm)
    {
        return algorithm switch
        {
            ApiKeyHashAlgorithms.SHA512 => TryComputeSha512(input, destination),
            _ => TryComputeSha256(input, destination)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryComputeSha256(ReadOnlySpan<char> input, Span<char> destination)
    {
        // SHA256 = 32 bytes -> 64 chars en hex
        if (destination.Length < 64) return false;

        Span<byte> hashBytes = stackalloc byte[32];
        int byteCount = Encoding.UTF8.GetByteCount(input);

        // Zero-allocation buffer management
        byte[]? arrayFromPool = null;
        Span<byte> inputBytes = byteCount <= 256
            ? stackalloc byte[byteCount]
            : (arrayFromPool = ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            Encoding.UTF8.GetBytes(input, inputBytes);
            SHA256.HashData(inputBytes[..byteCount], hashBytes);

            // Écrit l'hexadécimal directement dans le Span destination (Zero-allocation)
            return Convert.TryToHexString(hashBytes, destination, out _);
        }
        finally
        {
            if (arrayFromPool != null) ArrayPool<byte>.Shared.Return(arrayFromPool);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryComputeSha512(ReadOnlySpan<char> input, Span<char> destination)
    {
        // SHA512 = 64 bytes -> 128 chars en hex
        if (destination.Length < 128) return false;

        Span<byte> hashBytes = stackalloc byte[64];
        int byteCount = Encoding.UTF8.GetByteCount(input);

        byte[]? arrayFromPool = null;
        Span<byte> inputBytes = byteCount <= 256
            ? stackalloc byte[byteCount]
            : (arrayFromPool = ArrayPool<byte>.Shared.Rent(byteCount));

        try
        {
            Encoding.UTF8.GetBytes(input, inputBytes);
            SHA512.HashData(inputBytes[..byteCount], hashBytes);

            return Convert.TryToHexString(hashBytes, destination, out _);
        }
        finally
        {
            if (arrayFromPool != null) ArrayPool<byte>.Shared.Return(arrayFromPool);
        }
    }
}