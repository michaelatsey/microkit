//using MicroKit.OpenApi.Abstractions;
//using MicroKit.OpenApi.Options;
//using Microsoft.Extensions.Options;

//namespace MicroKit.OpenApi;

//internal class OpenApiDescriptor: IOpenApiDescriptor
//{
//    public string DefaultVersion { get; } 
//    public string[] SupportedVersions { get; } 
//    public string[] DeprecatedVersions { get; }
//    public bool AssumeDefaultVersionWhenUnspecified { get; }
//    public string GroupNameFormat { get; set; }
//    public string QueryStringReaderKey { get; set; } 
//    public bool ReportApiVersions { get; set; }
//    public bool SubstituteApiVersionInUrl { get; set; }
//    public string HeaderKey { get; set; }
//    public string MediaTypeReaderKey { get; set; }
//    public string Deprecated { get; set; }
//    public MicroKitOpenApiInfo OpenApiInfo { get; set; }

//    public OpenApiDescriptor(IOptions<MicroKitOpenApiOptions> options)
//    {
//        DefaultVersion = options.Value.DefaultVersion;
//        SupportedVersions = options.Value.SupportedVersions;
//        DeprecatedVersions = options.Value.DeprecatedVersions;
//        AssumeDefaultVersionWhenUnspecified = options.Value.AssumeDefaultVersionWhenUnspecified;
//        GroupNameFormat = options.Value.GroupNameFormat;
//        QueryStringReaderKey = options.Value.QueryStringReaderKey;
//        ReportApiVersions = options.Value.ReportApiVersions;
//        SubstituteApiVersionInUrl = options.Value.SubstituteApiVersionInUrl;
//        HeaderKey = options.Value.HeaderKey;
//        MediaTypeReaderKey = options.Value.MediaTypeReaderKey;
//        Deprecated = options.Value.Deprecated;
//        OpenApiInfo = new MicroKitOpenApiInfo
//        {
//            Title = options.Value.OpenApiInfo.Title,
//            Version = options.Value.OpenApiInfo.Version,
//            Description = options.Value.OpenApiInfo.Description,
//            Contact = options.Value.OpenApiInfo.Contact,
//            License = options.Value.OpenApiInfo.License
//        };
//    }
//}
