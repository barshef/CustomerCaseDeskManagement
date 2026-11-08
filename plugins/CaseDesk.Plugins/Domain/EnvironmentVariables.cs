using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CaseDesk.Plugins.Domain
{
    /// <summary>Reads Dataverse environment variables: current value, else default, else fallback.</summary>
    public static class EnvironmentVariables
    {
        public const string CaseClosedTransport = "cd_CaseClosedTransport";
        public const string CaseClosedServiceBusEndpointName = "cd_CaseClosedServiceBusEndpointName";
        public const string CaseClosedWebhookEndpointName = "cd_CaseClosedWebhookEndpointName";
        public const string SupervisorTeamName = "cd_SupervisorTeamName";

        public static string Get(IOrganizationService service, string schemaName, string fallback)
        {
            var query = new QueryExpression("environmentvariabledefinition")
            {
                ColumnSet = new ColumnSet("defaultvalue"),
                TopCount = 1,
                Criteria = { Conditions = { new ConditionExpression("schemaname", ConditionOperator.Equal, schemaName) } },
            };
            var valueLink = query.AddLink("environmentvariablevalue", "environmentvariabledefinitionid",
                "environmentvariabledefinitionid", JoinOperator.LeftOuter);
            valueLink.EntityAlias = "v";
            valueLink.Columns = new ColumnSet("value");

            var definition = service.RetrieveMultiple(query).Entities.FirstOrDefault();
            if (definition == null) return fallback;

            var current = definition.GetAttributeValue<AliasedValue>("v.value")?.Value as string;
            if (!string.IsNullOrWhiteSpace(current)) return current;

            var defaultValue = definition.GetAttributeValue<string>("defaultvalue");
            return string.IsNullOrWhiteSpace(defaultValue) ? fallback : defaultValue;
        }
    }
}
