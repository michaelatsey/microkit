using MicroKit.Result.Analyzers.Tests.Stubs;

namespace MicroKit.Result.Analyzers.Tests;

public sealed class DefaultErrorCategoryAnalyzerTests
{
    private static Task Verify(string code) =>
        new CSharpAnalyzerTest<DefaultErrorCategoryAnalyzer, CompatXUnitVerifier> { TestCode = ResultStubs.All + code }.RunAsync();

    [Fact]
    public Task MKR002_WhenRecordDoesNotOverrideCategory_Reports() => Verify("""
        sealed record {|MKR002:UserNotFoundError|}(int Id) : Error("USER.NOT_FOUND", "User not found");
        """);

    [Fact]
    public Task MKR002_WhenClassDerivesFromAnAbstractBaseWithoutOverride_ReportsTheConcreteType() => Verify("""
        abstract record AuthError(string Code, string Message) : Error(Code, Message);
        sealed record {|MKR002:TokenExpiredError|}() : AuthError("AUTH.TOKEN.EXPIRED", "The token has expired");
        """);

    [Fact]
    public Task MKR002_WhenRecordOverridesCategory_DoesNotReport() => Verify("""
        sealed record UserNotFoundError(int Id) : Error("USER.NOT_FOUND", "User not found")
        {
            public override ErrorCategory Category => ErrorCategory.NotFound;
        }
        """);

    [Fact]
    public Task MKR002_WhenABaseOverridesCategory_DoesNotReport() => Verify("""
        abstract record ConflictError(string Code, string Message) : Error(Code, Message)
        {
            public override ErrorCategory Category => ErrorCategory.Conflict;
        }
        sealed record EmailTakenError() : ConflictError("USER.EMAIL.TAKEN", "The email is taken");
        """);

    [Fact]
    public Task MKR002_WhenOverrideIsExplicitlyTechnical_DoesNotReport() => Verify("""
        sealed record DatabaseError() : Error("INFRA.DATABASE.FAILURE", "A database error occurred.")
        {
            public override ErrorCategory Category => ErrorCategory.Technical;
        }
        """);

    [Fact]
    public Task MKR002_WhenTypeIsAbstractOrUnrelated_DoesNotReport() => Verify("""
        abstract record DomainError(string Code, string Message) : Error(Code, Message);
        sealed class NotAnError { public ErrorCategory Category => ErrorCategory.Technical; }
        """);

    [Fact]
    public Task MKR002_WhenAGenericBaseOverridesCategory_DoesNotReport() => Verify("""
        abstract record EntityError<TEntity>(string Code, string Message) : Error(Code, Message)
        {
            public override ErrorCategory Category => ErrorCategory.NotFound;
        }
        sealed record UserMissingError() : EntityError<int>("USER.MISSING", "User missing");
        """);

    [Fact]
    public Task MKR002_WhenCategoryIsTakenFromAPositionalParameter_DoesNotReport() => Verify("""
        sealed record KindedError(string Code, string Message, ErrorCategory Category) : Error(Code, Message)
        {
            public override ErrorCategory Category { get; } = Category;
        }
        """);

    [Fact]
    public Task MKR002_WhenTypeIsGeneric_ReportsItWithItsTypeParameters() => new CSharpAnalyzerTest<DefaultErrorCategoryAnalyzer, CompatXUnitVerifier>
    {
        TestCode = ResultStubs.All + """
            sealed record {|#0:NotFoundError|}<TEntity>(int Id) : Error("ENTITY.NOT_FOUND", "Not found");
            """,
        ExpectedDiagnostics = { new DiagnosticResult(DefaultErrorCategoryAnalyzer.DiagnosticId, Microsoft.CodeAnalysis.DiagnosticSeverity.Warning).WithLocation(0).WithArguments("NotFoundError<TEntity>") },
    }.RunAsync();
}
