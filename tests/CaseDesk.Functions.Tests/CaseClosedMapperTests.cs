using System.Text.Json;
using System.Text.Json.Nodes;
using CaseDesk.Functions.CaseClosed;
using CaseDesk.Functions.Dataverse;
using Json.Schema;

namespace CaseDesk.Functions.Tests;

public class CaseClosedMapperTests
{
    private static CaseClosedEvent MapOk(RemoteExecutionContext ctx, string transport = "ServiceBus")
    {
        var result = CaseClosedMapper.Map(ctx, transport);
        Assert.True(result.Status == MapStatus.Mapped, result.Reason);
        return result.Event!;
    }

    [Fact]
    public void Maps_fixture_to_CaseClosed_v1_with_labels()
    {
        var evt = MapOk(Fixture.Closed());

        Assert.Equal("CaseClosed", evt.EventType);
        Assert.Equal("1.0", evt.SchemaVersion);
        Assert.Equal("ServiceBus", evt.Transport);
        Assert.Equal("b1e2c3d4-a5f6-4789-8abc-def012345678", evt.CorrelationId);
        Assert.Equal("unq0123456789abcdef", evt.Organization);
        Assert.Equal(Fixture.ClosedOnIso, evt.OccurredAt);

        var c = evt.Case;
        Assert.Equal(Fixture.CaseId.ToString(), c.Id);
        Assert.Equal("CD-00042", c.CaseNumber);
        Assert.Equal("Refund not received for order 1187", c.Title);
        Assert.Equal("Refund", c.Category);
        Assert.Equal("High", c.Priority);
        Assert.Equal("aa11bb22-cc33-4d44-8e55-ff6677889900", c.Customer!.AccountId);
        Assert.Equal("Contoso Ltd", c.Customer.Name);
        Assert.Equal("12345678-90ab-4cde-8f01-234567890abc", c.RequesterContactId);
        Assert.Equal("2026-11-18T09:00:00.0000000Z", c.CreatedOn);
        Assert.Equal("2026-11-19T09:00:00.0000000Z", c.DueDate);
        Assert.Equal("2026-11-18T15:30:00.0000000Z", c.ResolvedOn);
        Assert.Equal(Fixture.ClosedOnIso, c.ClosedOn);
        Assert.True(c.SlaMet);
        Assert.Equal(49.99m, c.RefundAmount);
    }

    [Theory]
    [InlineData(417200000, "Billing")]
    [InlineData(417200001, "Delivery")]
    [InlineData(417200002, "Product")]
    [InlineData(417200003, "Refund")]
    [InlineData(417200004, "Other")]
    public void Maps_category_labels(int value, string label) =>
        Assert.Equal(label, MapOk(Fixture.Parse(Fixture.ClosedNode().With("cd_category", Fixture.OptionSet(value)))).Case.Category);

    [Theory]
    [InlineData(417200000, "Low")]
    [InlineData(417200001, "Medium")]
    [InlineData(417200002, "High")]
    [InlineData(417200003, "Critical")]
    public void Maps_priority_labels(int value, string label) =>
        Assert.Equal(label, MapOk(Fixture.Parse(Fixture.ClosedNode().With("cd_priority", Fixture.OptionSet(value)))).Case.Priority);

    [Fact]
    public void MessageId_is_case_id_and_closed_on_round_trip_and_stable_across_retries_and_transports()
    {
        var expected = $"{Fixture.CaseId}:{Fixture.ClosedOnIso}";
        var first = MapOk(Fixture.Closed(), "ServiceBus");
        var retry = MapOk(Fixture.Closed(), "ServiceBus");
        var webhook = MapOk(Fixture.Closed(), "Webhook");

        Assert.Equal(expected, first.MessageId);
        Assert.Equal(first.MessageId, retry.MessageId);
        Assert.Equal(first.MessageId, webhook.MessageId);
        Assert.Equal("Webhook", webhook.Transport);
    }

    [Fact]
    public void MessageId_changes_when_the_case_is_closed_again_later()
    {
        var reclosed = Fixture.ClosedNode().With("cd_closedon", Fixture.WcfDate(new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc)));
        Assert.NotEqual(MapOk(Fixture.Closed()).MessageId, MapOk(Fixture.Parse(reclosed)).MessageId);
    }

    [Theory]
    [InlineData("ServiceBus")]
    [InlineData("Webhook")]
    public void Mapped_payload_validates_against_the_contract_schema(string transport)
    {
        AssertValid(MapOk(Fixture.Closed(), transport).ToJson());
    }

    [Theory]
    [InlineData("category", "Shipping")]
    [InlineData("caseNumber", "42")]
    [InlineData("id", "not-a-uuid")]
    [InlineData("closedOn", "yesterday")]
    public void Schema_check_rejects_payloads_that_break_the_contract(string property, string value)
    {
        var node = JsonNode.Parse(MapOk(Fixture.Closed()).ToJson())!;
        node["case"]![property] = value;
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertValid(node.ToJsonString()));
    }

    [Fact]
    public void Payload_without_optional_values_validates_against_the_schema()
    {
        var node = Fixture.ClosedNode()
            .Without("cd_accountid").Without("cd_contactid").Without("cd_duedate")
            .Without("cd_resolvedon").Without("cd_refundamount");
        var evt = MapOk(Fixture.Parse(node));

        Assert.Null(evt.Case.Customer);
        Assert.Null(evt.Case.DueDate);
        Assert.Null(evt.Case.RefundAmount);
        var json = JsonNode.Parse(evt.ToJson())!;
        Assert.True(json["case"]!.AsObject().ContainsKey("dueDate"), "nullable properties are written as null");
        AssertValid(evt.ToJson());
    }

    [Fact]
    public void Sla_met_when_resolved_before_due_even_if_closed_after()
    {
        // Fixture: resolved 18 Nov 15:30, due 19 Nov 09:00, closed 20 Nov 10:15.
        Assert.True(MapOk(Fixture.Closed()).Case.SlaMet);
    }

    [Fact]
    public void Sla_breached_when_resolved_after_due()
    {
        var node = Fixture.ClosedNode().With("cd_resolvedon", Fixture.WcfDate(new DateTime(2026, 11, 19, 9, 0, 1, DateTimeKind.Utc)));
        Assert.False(MapOk(Fixture.Parse(node)).Case.SlaMet);
    }

    [Fact]
    public void Sla_uses_closed_on_when_never_resolved()
    {
        var node = Fixture.ClosedNode().Without("cd_resolvedon");
        Assert.False(MapOk(Fixture.Parse(node)).Case.SlaMet); // closed 20 Nov > due 19 Nov
    }

    [Fact]
    public void Sla_met_when_there_is_no_due_date()
    {
        var node = Fixture.ClosedNode().Without("cd_duedate").Without("cd_resolvedon");
        Assert.True(MapOk(Fixture.Parse(node)).Case.SlaMet);
    }

    [Fact]
    public void Sla_met_exactly_on_due_date()
    {
        var due = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
        Assert.True(CaseClosedMapper.IsSlaMet(due, due, due.AddDays(1)));
    }

    [Theory]
    [InlineData(1, 417200006)] // Cancelled
    [InlineData(0, 417200005)] // Resolved
    [InlineData(0, 417200002)] // In Progress
    public void Ignores_cases_that_are_not_closed(int state, int status)
    {
        var node = Fixture.ClosedNode().With("statecode", Fixture.OptionSet(state)).With("statuscode", Fixture.OptionSet(status));
        var result = CaseClosedMapper.Map(Fixture.Parse(node), "ServiceBus");
        Assert.Equal(MapStatus.Ignored, result.Status);
        Assert.Null(result.Event);
    }

    [Theory]
    [InlineData("PrimaryEntityName", "account")]
    [InlineData("MessageName", "Create")]
    public void Ignores_other_entities_and_messages(string property, string value)
    {
        var node = Fixture.ClosedNode();
        node[property] = value;
        Assert.Equal(MapStatus.Ignored, CaseClosedMapper.Map(Fixture.Parse(node), "Webhook").Status);
    }

    [Fact]
    public void Ignores_context_without_post_image()
    {
        var node = Fixture.ClosedNode();
        node["PostEntityImages"] = new JsonArray();
        Assert.Equal(MapStatus.Ignored, CaseClosedMapper.Map(Fixture.Parse(node), "ServiceBus").Status);
    }

    [Fact]
    public void Closed_case_without_closed_on_is_invalid()
    {
        var node = Fixture.ClosedNode().Without("cd_closedon");
        Assert.Equal(MapStatus.Invalid, CaseClosedMapper.Map(Fixture.Parse(node), "ServiceBus").Status);
    }

    [Fact]
    public void Unknown_choice_value_is_invalid()
    {
        var node = Fixture.ClosedNode().With("cd_category", Fixture.OptionSet(999));
        Assert.Equal(MapStatus.Invalid, CaseClosedMapper.Map(Fixture.Parse(node), "ServiceBus").Status);
    }

    [Fact]
    public void Accepts_iso_dates_in_attribute_values()
    {
        var node = Fixture.ClosedNode()
            .With("cd_closedon", JsonValue.Create("2026-11-20T11:15:00+01:00"))
            .With("createdon", JsonValue.Create("2026-11-18T09:00:00Z"));
        var evt = MapOk(Fixture.Parse(node));
        Assert.Equal(Fixture.ClosedOnIso, evt.Case.ClosedOn);
        Assert.Equal($"{Fixture.CaseId}:{Fixture.ClosedOnIso}", evt.MessageId);
    }

    [Theory]
    [InlineData("/Date(1795169700000)/")]
    [InlineData("/Date(1795169700000+0000)/")]
    [InlineData("/Date(1795169700000-0500)/")] // offset is informational; ms are UTC
    [InlineData("2026-11-20T10:15:00Z")]
    [InlineData("2026-11-20T10:15:00.0000000Z")]
    [InlineData("2026-11-20T05:15:00-05:00")]
    public void Parses_wcf_and_iso_dates_to_utc(string text)
    {
        var parsed = DataverseDate.ParseUtc(text);
        Assert.Equal(new DateTime(2026, 11, 20, 10, 15, 0, DateTimeKind.Utc), parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.Value.Kind);
    }

    [Fact]
    public void Parses_escaped_slash_dates_as_sent_on_the_wire()
    {
        // Dataverse escapes '/' as '\/' in JSON strings; the fixture does too.
        Assert.Contains("\\/Date(", Fixture.ClosedJson());
        Assert.Equal(new DateTime(2026, 11, 20, 10, 15, 0, DateTimeKind.Utc),
            Fixture.Closed().GetPostImage("PostImage")!.GetDateTime("cd_closedon"));
    }

    [Fact]
    public void Rejects_unknown_transport()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CaseClosedMapper.Map(Fixture.Closed(), "Email"));
    }

    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(File.ReadAllText(Fixture.Path("caseclosed-event.schema.json"))));

    internal static void AssertValid(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = Schema.Value.Evaluate(doc.RootElement, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });
        var errors = string.Join(Environment.NewLine,
            (result.Details ?? []).Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Key} {e.Value}")));
        Assert.True(result.IsValid, "Schema validation failed:" + Environment.NewLine + errors + Environment.NewLine + json);
    }
}
