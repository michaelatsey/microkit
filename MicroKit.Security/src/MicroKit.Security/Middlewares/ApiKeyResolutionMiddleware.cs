//using MicroKit.Abstractions.Contexts;
//using MicroKit.MultiTenancy;
//using MicroKit.Security.Contexts;
//using Microsoft.AspNetCore.Builder;
//using Microsoft.AspNetCore.Http;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Logging;

//namespace MicroKit.Security.Middlewares;

///// <summary>
///// Middleware for resolving and validating API keys from HTTP headers.
///// </summary>
//public sealed class ApiKeyResolutionMiddleware
//{
//    private readonly RequestDelegate _next;
//    private readonly ILogger<ApiKeyResolutionMiddleware> _logger;

//    public ApiKeyResolutionMiddleware(RequestDelegate next, ILogger<ApiKeyResolutionMiddleware> logger)
//    {
//        _next = next;
//        _logger = logger;
//    }

//    public async Task InvokeAsync(
//        HttpContext context,
//        ApiKeyContext apiKeyContext,
//        TenantContext tenantContext,
//        ClientContext clientContext,
//        IApiKeyValidator apiKeyValidator)
//    {
//        // Skip API key validation for health checks and OpenAPI endpoints
//        if (ShouldSkipValidation(context.Request.Path))
//        {
//            await _next(context);
//            return;
//        }

//        var apiKey = context.Request.Headers[HeaderNames.ApiKey].FirstOrDefault();

//        if (string.IsNullOrWhiteSpace(apiKey))
//        {
//            _logger.LogWarning("Request missing {Header} header", HeaderNames.ApiKey);
            
//            await WriteProblemDetails(context, StatusCodes.Status401Unauthorized,
//                "ApiKey.Missing", $"The {HeaderNames.ApiKey} header is required.");
//            return;
//        }

//        try
//        {
//            apiKeyContext.SetApiKey(apiKey);

//            // Validate the API key against tenant and client context
//            var tenantId = tenantContext.IsResolved && tenantContext.Tenant?.Id is not null ? tenantContext.Tenant.Id : string.Empty;
//            var clientId = clientContext.IsResolved ? clientContext.ClientId : string.Empty;

//            var validationResult = await apiKeyValidator.ValidateAsync(
//                apiKey, 
//                tenantId, 
//                clientId, 
//                context.RequestAborted);

//            if (!validationResult.IsValid)
//            {
//                _logger.LogWarning(
//                    "API key validation failed for Tenant {TenantId}, Client {CustomerId}: {Error}",
//                    tenantId,
//                    clientId,
//                    validationResult.ErrorMessage);

//                await WriteProblemDetails(context, StatusCodes.Status401Unauthorized,
//                    "ApiKey.Invalid", validationResult.ErrorMessage ?? "Invalid API key.");
//                return;
//            }

//            apiKeyContext.MarkValidated(validationResult.KeyIdentifier);
            
//            await _next(context);
//        }
//        catch (InvalidOperationException ex)
//        {
//            _logger.LogError(ex, "Error setting API key context");
            
//            await WriteProblemDetails(context, StatusCodes.Status400BadRequest,
//                "ApiKey.Error", "An error occurred while resolving API key context.");
//        }
//    }

//    private static bool ShouldSkipValidation(PathString path)
//    {
//        return path.StartsWithSegments("/health") ||
//               path.StartsWithSegments("/swagger") ||
//               path.StartsWithSegments("/api-docs");
//    }

//    private static async Task WriteProblemDetails(HttpContext context, int statusCode, string errorCode, string detail)
//    {
//        context.Response.StatusCode = statusCode;
//        context.Response.ContentType = "application/problem+json";

//        var problemDetails = new ProblemDetails
//        {
//            Status = statusCode,
//            Title = statusCode == 401 ? "Unauthorized" : "Request Validation Failed",
//            Detail = detail,
//            Type = $"https://httpstatuses.com/{statusCode}",
//            Extensions = { ["errorCode"] = errorCode }
//        };

//        await context.Response.WriteAsJsonAsync(problemDetails);
//    }
//}

///// <summary>
///// Extension methods for ApiKeyResolutionMiddleware.
///// </summary>
//public static class ApiKeyResolutionMiddlewareExtensions
//{
//    public static IApplicationBuilder UseApiKeyResolution(this IApplicationBuilder builder)
//    {
//        return builder.UseMiddleware<ApiKeyResolutionMiddleware>();
//    }
//}
