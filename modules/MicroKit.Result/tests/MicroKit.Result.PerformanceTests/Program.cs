using BenchmarkDotNet.Running;

namespace MicroKit.Result.PerformanceTests;

// Run from modules/MicroKit.Result:
//   dotnet run -c Release --project tests/MicroKit.Result.PerformanceTests -- --filter '*'
public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
