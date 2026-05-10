using Ardalis.Result;
using MicroKit.Payments.Abstractions;
using Microsoft.Extensions.Logging;
using Stripe;

namespace MicroKit.Payments.Stripe.Customer;

internal class StripeCustomerGateway : ICustomerGateway
{
    private readonly IStripeClient _client;
    private readonly CustomerService _customerService;
    private readonly ILogger<StripeCustomerGateway> _logger;

    public StripeCustomerGateway(
        IStripeClient stripeClient,
        ILogger<StripeCustomerGateway> logger)
    {
        _client = stripeClient;
        _customerService = new CustomerService(_client);
        _logger = logger;
    }
    public async Task<Result<CustomerResponse>> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if(_logger.IsEnabled(LogLevel.Information))
                _logger.LogInformation("Création d'un client Stripe: {Email}", request.Email);

            var options = new CustomerCreateOptions
            {
                Email = request.Email,
                Name = request.Name,
                Phone = request.Phone,
                Metadata = request.Metadata ?? [],
                PreferredLocales = ["fr-FR"]
            };

            var requestOptions = string.IsNullOrEmpty(request.IdempotencyKey)
                ? null
                : new RequestOptions { IdempotencyKey = request.IdempotencyKey };

            var customer = await _customerService.CreateAsync(
                options,
                requestOptions,
                cancellationToken
            );
            var response = new CustomerResponse(
                Id: customer.Id,
                Email: customer.Email,
                Name: customer.Name,
                Phone: customer.Phone,
                CreatedAt: customer.Created,
                Metadata: customer.Metadata
            );
            if(_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Client Stripe créé avec succès: {CustomerId}", response.Id);
            }

            return Result<CustomerResponse>.Success(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Erreur Stripe lors de la création du client: {StripeCode}", ex.StripeError?.Code);

            return ex.StripeError?.Code switch
            {
                "email_already_exists" => Result<CustomerResponse>.Conflict($"Un client avec l'email {request.Email} existe déjà"),
                "invalid_email" => Result<CustomerResponse>.Invalid(new ValidationError
                {
                    Identifier = "Email",
                    ErrorMessage = "L'adresse email est invalide"
                }),
                "parameter_missing" => Result<CustomerResponse>.Error("Paramètres requis manquants"),
                _ => Result<CustomerResponse>.Error($"Erreur Stripe: {ex.Message}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur inattendue lors de la création du client");
            return Result<CustomerResponse>.CriticalError("Une erreur inattendue s'est produite");
        }
    }

    public async Task<Result<CustomerResponse>> GetAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return Result<CustomerResponse>.Invalid(new ValidationError
                {
                    Identifier = "customerId",
                    ErrorMessage = "L'identifiant du client est requis"
                });
            }

            _logger.LogInformation("Récupération du client Stripe: {CustomerId}", customerId);

            var customer = await _customerService.GetAsync(
                customerId,
                null,
                null,
                cancellationToken
            );

            var response = new CustomerResponse(
                Id: customer.Id,
                Email: customer.Email,
                Name: customer.Name,
                Phone: customer.Phone,
                CreatedAt: customer.Created,
                Metadata: customer.Metadata
            );

            return Result<CustomerResponse>.Success(response);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Client non trouvé: {CustomerId}", customerId);
            return Result<CustomerResponse>.NotFound($"Client {customerId} non trouvé");
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Erreur Stripe lors de la récupération du client: {CustomerId}", customerId);
            return Result<CustomerResponse>.Error($"Erreur Stripe: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur inattendue lors de la récupération du client");
            return Result<CustomerResponse>.CriticalError("Une erreur inattendue s'est produite");
        }
    }
}

