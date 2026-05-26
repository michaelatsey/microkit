namespace MicroKit.Logging;

/// <summary>
/// Marks a <see langword="partial"/> class or record for <c>BeginLogScope</c> source generation.
/// </summary>
/// <remarks>
/// The <c>MicroKit.Logging.Generators</c> source generator emits a
/// <c>BeginLogScope(ILogger logger)</c> method on the decorated type that creates a structured
/// log scope containing all readable instance properties. Use property names matching
/// <see cref="LogPropertyNames"/> constants so that structured log sinks can index them correctly.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class MicroKitLogScopeAttribute : Attribute { }
