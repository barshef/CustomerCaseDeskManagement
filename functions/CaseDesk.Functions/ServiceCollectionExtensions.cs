using CaseDesk.Functions.CaseClosed;
using CaseDesk.Functions.ExternalStub;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace CaseDesk.Functions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="CaseClosedHandler"/> as a typed HttpClient with the standard resilience
    /// pipeline: 3 retries with exponential back-off + jitter on 5xx/408/429/transient errors,
    /// a circuit breaker and timeouts. <paramref name="configureResilience"/> lets tests shorten delays.
    /// </summary>
    public static IHttpClientBuilder AddCaseClosedForwarding(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        services.AddOptions<ExternalStubOptions>().Configure(o =>
        {
            o.Url = configuration["ExternalStubUrl"] ?? "";
            o.Key = configuration["ExternalStubKey"] ?? "";
        });

        var builder = services.AddHttpClient<CaseClosedHandler>();
        builder.AddStandardResilienceHandler(o =>
        {
            o.Retry.MaxRetryAttempts = 3;
            o.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            configureResilience?.Invoke(o);
        });
        return builder;
    }

    /// <summary>Registers the external-system stub with its Table Storage de-dup store.</summary>
    public static IServiceCollection AddExternalStub(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ICaseClosedReceivedStore>(_ =>
        {
            // Local / connection-string mode: "UseDevelopmentStorage=true" or a full connection string.
            var connectionString = configuration["AzureWebJobsStorage"];
            if (!string.IsNullOrWhiteSpace(connectionString))
                return new TableCaseClosedReceivedStore(connectionString);

            // Azure (Flex Consumption) identity-based mode: AzureWebJobsStorage__accountName + managed identity
            // with "Storage Table Data Contributor" on the account (see infra/main.bicep).
            var accountName = configuration["AzureWebJobsStorage:accountName"] ?? configuration["AzureWebJobsStorage__accountName"];
            if (!string.IsNullOrWhiteSpace(accountName))
                return TableCaseClosedReceivedStore.ForAccount(accountName);

            throw new InvalidOperationException("Neither AzureWebJobsStorage nor AzureWebJobsStorage__accountName is configured.");
        });
        services.AddSingleton<ExternalStubService>();
        return services;
    }
}
