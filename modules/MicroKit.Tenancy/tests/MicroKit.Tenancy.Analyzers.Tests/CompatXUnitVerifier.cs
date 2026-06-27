using System.Collections.Generic;
using Microsoft.CodeAnalysis.Testing;
using Xunit.Sdk;

namespace MicroKit.Tenancy.Analyzers.Tests;

/// <summary>
/// xUnit verifier compatible with xunit 2.9.x.
/// The built-in <c>XUnitVerifier</c> in testing package 1.1.x uses the deprecated
/// <c>EqualException(object, object)</c> constructor removed in xunit 2.9.x.
/// This implementation uses <c>XunitException</c> directly.
/// </summary>
internal sealed class CompatXUnitVerifier : IVerifier
{
    public static readonly CompatXUnitVerifier Instance = new();

    private readonly string _context;

    public CompatXUnitVerifier() : this(string.Empty) { }

    private CompatXUnitVerifier(string context) => _context = context;

    public void Empty<T>(string collectionName, IEnumerable<T> collection)
    {
        var list = new List<T>(collection);
        if (list.Count != 0)
            Fail($"Expected '{collectionName}' to be empty but found {list.Count} item(s).");
    }

    public void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            Fail($"{message}{Environment.NewLine}Expected: {Format(expected)}{Environment.NewLine}Actual:   {Format(actual)}");
    }

    public void False(bool assert, string? message = null)
    {
        if (assert)
            Fail(message ?? "Expected false but was true.");
    }

    public void LanguageIsSupported(string language)
    {
        if (!string.Equals(language, "C#", StringComparison.Ordinal))
            Fail($"Language '{language}' is not supported by this verifier.");
    }

    public void NotEmpty<T>(string collectionName, IEnumerable<T> collection)
    {
        var list = new List<T>(collection);
        if (list.Count == 0)
            Fail($"Expected '{collectionName}' to not be empty.");
    }

    public void True(bool assert, string? message = null)
    {
        if (!assert)
            Fail(message ?? "Expected true but was false.");
    }

    public void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        IEqualityComparer<T>? equalityComparer = null,
        string? message = null)
    {
        var comparer     = equalityComparer ?? EqualityComparer<T>.Default;
        var expectedList = new List<T>(expected);
        var actualList   = new List<T>(actual);

        if (expectedList.Count != actualList.Count)
        {
            Fail($"{message}{Environment.NewLine}Sequence length mismatch: expected {expectedList.Count} but got {actualList.Count}.");
            return;
        }

        for (var i = 0; i < expectedList.Count; i++)
        {
            if (!comparer.Equals(expectedList[i], actualList[i]))
                Fail($"{message}{Environment.NewLine}Sequence differs at index {i}: expected {Format(expectedList[i])} but got {Format(actualList[i])}.");
        }
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    public void Fail(string? message = null)
    {
        var full = string.IsNullOrEmpty(_context)
            ? (message ?? "Test failed.")
            : $"[{_context}] {message ?? "Test failed."}";
        throw new XunitException(full);
    }

    public IVerifier PushContext(string description) =>
        new CompatXUnitVerifier(string.IsNullOrEmpty(_context) ? description : $"{_context} → {description}");

    private static string Format(object? value) =>
        value is null ? "<null>" : value.ToString() ?? "<null>";
}
