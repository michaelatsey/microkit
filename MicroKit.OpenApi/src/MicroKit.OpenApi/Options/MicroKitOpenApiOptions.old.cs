//using MicroKit.OpenApi.Abstractions;
//using System.Collections.Generic;
//using System.ComponentModel.DataAnnotations;
//using System.Text;

//namespace MicroKit.OpenApi.Options;

//public class MicroKitOpenApiOptions
//{
//    public const string SectionName = "MicroKit:OpenApi";

//    // --- Configuration Générale de l'API ---
//    [Required]
//    public string Title { get; set; } = "MicroKit API";
//    public string? Summary { get; set; }
//    public string Description { get; set; } = "Documentation de service haute performance.";
//    public Uri? TermsOfService { get; set; }
//    public MicroKitOpenApiContact Contact { get; set; } = new();
//    public MicroKitOpenApiLicense? License { get; set; }

//    // --- Configuration du Versioning (Logique HTTP) ---
//    [Required]
//    public string DefaultVersion { get; set; } = "1.0";

//    // Versions actives et obsolètes
//    public string[] SupportedVersions { get; set; } = ["1.0"];
//    public string[]? DeprecatedVersions { get; set; } = [];

//    // Comportement du versioning
//    public bool AssumeDefaultVersionWhenUnspecified { get; set; } = true;
//    public bool ReportApiVersions { get; set; } = true;
//    public string DeprecatedMessage { get; set; } = "This service version has been deprecated.";

//    // Lecteurs de version (Clés utilisées dans l'URL/Header/Query)
//    public string ApiVersionHeaderKey { get; set; } = "x-api-version";
//    public string ApiVersionQueryKey { get; set; } = "api-version";
//    public string ApiVersionMediaTypeKey { get; set; } = "v";

//    // --- Configuration UI (Scalar) ---
//    public bool EnableScalar { get; set; } = true;
//    public string ScalarRoutePrefix { get; set; } = "scalar";

//    // Format des groupes dans l'explorateur (ex: 'v'1.0)
//    public string GroupNameFormat { get; set; } = "'v'VVV";
//}

//public class MicroKitOpenApiContact
//{
//    public string Name { get; set; } = "Support Team";
//    public string Email { get; set; } = "support@microkit.io";
//    public Uri? Url { get; set; }
//}

//public class MicroKitOpenApiLicense
//{
//    public string Name { get; set; } = "MIT";
//    public Uri? Url { get; set; }
//}