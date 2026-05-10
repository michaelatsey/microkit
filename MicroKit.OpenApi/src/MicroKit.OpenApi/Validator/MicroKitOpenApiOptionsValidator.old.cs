//using Asp.Versioning;
//using MicroKit.OpenApi.Options;
//using Microsoft.Extensions.Options;

//namespace MicroKit.OpenApi.Validator;

//internal sealed class MicroKitOpenApiOptionsValidator
//    : IValidateOptions<MicroKitOpenApiOptions>
//{
//    public ValidateOptionsResult Validate(
//        string? name,
//        MicroKitOpenApiOptions options)
//    {
//        var failures = new List<string>();

//        if (string.IsNullOrWhiteSpace(options.DefaultVersion))
//            failures.Add("DefaultVersion is required.");

//        if (!ApiVersionParser.Default.TryParse(options.DefaultVersion, out _))
//            failures.Add("DefaultVersion must be a valid API version (ex: 1.0).");

//        if (string.IsNullOrWhiteSpace(options.HeaderKey))
//            failures.Add("HeaderKey is required.");

//        if (string.IsNullOrWhiteSpace(options.QueryStringReaderKey))
//            failures.Add("QueryStringReaderKey is required.");

//        if (string.IsNullOrWhiteSpace(options.MediaTypeReaderKey))
//            failures.Add("MediaTypeReaderKey is required.");

//        if (string.IsNullOrWhiteSpace(options.GroupNameFormat))
//            failures.Add("GroupNameFormat is required.");

//        return failures.Count > 0
//            ? ValidateOptionsResult.Fail(failures)
//            : ValidateOptionsResult.Success;
//    }
//}
