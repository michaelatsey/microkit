using MicroKit.Security.Abstractions.Extraction;
using MicroKit.Security.Abstractions.Identity;

namespace MicroKit.Security.Abstractions.Authentication;

public interface IAuthenticationService
{
    ValueTask<SecurityAuthResult> AuthenticateAsync(
        IEnumerable<ExtractionResult> extractions,
        CancellationToken cancellationToken = default);
}