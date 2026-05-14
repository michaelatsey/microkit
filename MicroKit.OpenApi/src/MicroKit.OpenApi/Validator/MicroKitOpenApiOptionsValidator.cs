using MicroKit.OpenApi.Options;

namespace MicroKit.OpenApi.Validator;

/// <summary>
/// Validates <see cref="MicroKitOpenApiOptions"/> at application startup.
/// </summary>
internal sealed class MicroKitOpenApiOptionsValidator : IValidateOptions<MicroKitOpenApiOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MicroKitOpenApiOptions options)
    {
        var failures = new List<string>();

        // 1. Validations de base
        if (string.IsNullOrWhiteSpace(options.Title))
            failures.Add("Title is required and cannot be empty.");

        if (string.IsNullOrWhiteSpace(options.DefaultVersion))
            failures.Add("DefaultVersion is required and cannot be empty.");

        if (options.SupportedVersions.Count == 0 && options.DeprecatedVersions.Count == 0)
            failures.Add("At least one API version must be specified in SupportedVersions or DeprecatedVersions.");

        if (!options.SupportedVersions.Contains(options.DefaultVersion) &&
            !options.DeprecatedVersions.Contains(options.DefaultVersion))
        {
            failures.Add($"DefaultVersion '{options.DefaultVersion}' must be in SupportedVersions or DeprecatedVersions.");
        }

        // 2. Validation du Contact
        if (options.Contact?.Email is not null && !IsValidEmail(options.Contact.Email))
            failures.Add("Contact email format is invalid.");

        // 3. Validation Polymorphique de la Sécurité
        if (options.Securities != null)
        {
            foreach (var security in options.Securities)
            {
                ValidateSecurityScheme(security, failures);
            }

            // Validation optionnelle : vérifier l'unicité des SchemeNames
            var duplicateNames = options.Securities
                .GroupBy(s => s.SchemeName)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key);

            foreach (var duplicate in duplicateNames)
            {
                failures.Add($"Security SchemeName '{duplicate}' is defined multiple times. Each scheme must have a unique name.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateSecurityScheme(SecuritySchemeOptions security, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(security.SchemeName))
            failures.Add($"Security scheme of type {security.Type} must have a SchemeName.");

        switch (security)
        {
            case OAuth2SecurityOptions oauth2:
                ValidateOAuth2(oauth2, failures);
                break;

            case ApiKeySecurityOptions apiKey:
                if (string.IsNullOrWhiteSpace(apiKey.Name))
                    failures.Add($"ApiKey scheme '{apiKey.SchemeName}' must have a 'Name' (header or query parameter name).");
                break;

            case BearerSecurityOptions bearer:
                // Le bearer est généralement auto-suffisant avec ses valeurs par défaut
                break;
        }
    }

    private static void ValidateOAuth2(OAuth2SecurityOptions oauth2, List<string> failures)
    {
        var prefix = $"OAuth2 scheme '{oauth2.SchemeName}': ";

        if (oauth2.FlowType is OAuth2FlowType.AuthorizationCode or OAuth2FlowType.Implicit)
        {
            if (string.IsNullOrWhiteSpace(oauth2.AuthorizationUrl))
                failures.Add($"{prefix}AuthorizationUrl is required for {oauth2.FlowType} flow.");
        }

        if (oauth2.FlowType is OAuth2FlowType.AuthorizationCode or OAuth2FlowType.Password or OAuth2FlowType.ClientCredentials)
        {
            if (string.IsNullOrWhiteSpace(oauth2.TokenUrl))
                failures.Add($"{prefix}TokenUrl is required for {oauth2.FlowType} flow.");
        }
    }

    private static bool IsValidEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}