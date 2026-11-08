using System;
using System.Linq;
using CaseDesk.Plugins.Domain;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using C = CaseDesk.Plugins.Domain.Schema.Case;

namespace CaseDesk.Plugins
{
    /// <summary>
    /// Step S3 — Update of cd_case, PostOperation, ASYNCHRONOUS; filtering statecode, statuscode;
    /// images PreImage and PostImage. Publishes CaseClosed (FR-010) through the service endpoint
    /// chosen by environment variable cd_CaseClosedTransport: ServiceBus (default) | Webhook | None.
    /// Never runs in the user's transaction, so closing a case never waits on Azure.
    /// </summary>
    public sealed class PublishCaseClosedPlugin : PluginBase
    {
        public const string ServiceBus = "ServiceBus";
        public const string Webhook = "Webhook";
        public const string None = "None";
        public const string DefaultServiceBusEndpoint = "CaseDesk CaseClosed ServiceBus";
        public const string DefaultWebhookEndpoint = "CaseDesk CaseClosed Webhook";

        public static string MessageId(Guid caseId, DateTime closedOnUtc) =>
            caseId + ":" + DateTime.SpecifyKind(closedOnUtc, DateTimeKind.Utc).ToString("O");

        protected override void ExecuteCaseDesk(LocalPluginContext context)
        {
            var pre = context.PreImage();
            var post = context.PostImage();
            if (pre == null || post == null) return;

            var before = pre.GetAttributeValue<OptionSetValue>(C.StatusCode)?.Value;
            var after = post.GetAttributeValue<OptionSetValue>(C.StatusCode)?.Value;
            if (after != CaseStatus.Closed || before == CaseStatus.Closed)
            {
                context.Trace("Not a transition into Closed ({0} -> {1}); nothing to publish.", before, after);
                return;
            }

            var service = context.SystemService;
            var transport = EnvironmentVariables.Get(service, EnvironmentVariables.CaseClosedTransport, ServiceBus).Trim();
            if (transport.Equals(None, StringComparison.OrdinalIgnoreCase))
            {
                context.Trace("cd_CaseClosedTransport = None; publishing disabled.");
                return;
            }

            var useWebhook = transport.Equals(Webhook, StringComparison.OrdinalIgnoreCase);
            var endpointName = useWebhook
                ? EnvironmentVariables.Get(service, EnvironmentVariables.CaseClosedWebhookEndpointName, DefaultWebhookEndpoint)
                : EnvironmentVariables.Get(service, EnvironmentVariables.CaseClosedServiceBusEndpointName, DefaultServiceBusEndpoint);
            var endpoint = FindEndpoint(service, endpointName)
                ?? throw new InvalidPluginExecutionException(
                    $"CaseDesk: service endpoint '{endpointName}' is not registered in this environment. " +
                    "Register it (docs/caseclosed-endpoints.md) or set cd_CaseClosedTransport to None, then resubmit this system job.");

            var closedOn = post.Contains(C.ClosedOn) ? post.GetAttributeValue<DateTime>(C.ClosedOn) : DateTime.UtcNow;
            var ctx = context.ExecutionContext;
            ctx.SharedVariables["CaseDesk.MessageId"] = MessageId(post.Id, closedOn);
            ctx.SharedVariables["CaseDesk.Transport"] = useWebhook ? Webhook : ServiceBus;

            var notifier = (IServiceEndpointNotificationService)context.ServiceProvider.GetService(typeof(IServiceEndpointNotificationService));
            var response = notifier.Execute(endpoint.ToEntityReference(), ctx);
            context.Trace("Published CaseClosed {0} via {1} ('{2}'); response: {3}",
                ctx.SharedVariables["CaseDesk.MessageId"], ctx.SharedVariables["CaseDesk.Transport"], endpointName, response);
        }

        private static Entity FindEndpoint(IOrganizationService service, string name)
        {
            var query = new QueryExpression("serviceendpoint")
            {
                ColumnSet = new ColumnSet("name"),
                TopCount = 1,
                Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, name) } },
            };
            return service.RetrieveMultiple(query).Entities.FirstOrDefault();
        }
    }
}
