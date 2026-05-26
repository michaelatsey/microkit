// Polyfill required for `record` types and `init` accessors when targeting netstandard2.0.
// The C# compiler emits uses of this type for `init`-only setters and record positional parameters;
// it must be present in the compilation.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
