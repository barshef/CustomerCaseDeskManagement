using CaseDesk.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // Worker -> Application Insights (APPLICATIONINSIGHTS_CONNECTION_STRING).
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        services.AddCaseClosedForwarding(context.Configuration);
        services.AddExternalStub(context.Configuration);
    })
    .ConfigureLogging(logging =>
    {
        // The AI SDK adds a default filter that only sends Warning+ from ILogger. Remove it so the
        // Information-level "CaseClosedForwarded" / "CaseClosedReceived" events reach Application Insights.
        logging.Services.Configure<LoggerFilterOptions>(options =>
        {
            var defaultRule = options.Rules.FirstOrDefault(rule =>
                rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
            if (defaultRule is not null) options.Rules.Remove(defaultRule);
        });
    })
    .Build();

host.Run();
