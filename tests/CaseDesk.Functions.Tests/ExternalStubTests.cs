using System.Net;
using System.Text.Json.Nodes;
using CaseDesk.Functions.CaseClosed;
using CaseDesk.Functions.ExternalStub;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CaseDesk.Functions.Tests;

public class ExternalStubTests
{
    private readonly InMemoryCaseClosedReceivedStore _store = new();
    private ExternalStubService Service() => new(_store, NullLogger<ExternalStubService>.Instance);

    private static string Payload(string transport = "ServiceBus") =>
        CaseClosedMapper.Map(Fixture.Closed(), transport).Event!.ToJson();

    [Fact]
    public async Task First_delivery_is_recorded_and_repeat_is_a_duplicate()
    {
        var service = Service();

        var first = await service.ReceiveAsync(Payload());
        var second = await service.ReceiveAsync(Payload());

        Assert.Equal(ReceiveStatus.Created, first.Status);
        Assert.False(first.Duplicate);
        Assert.Equal(ReceiveStatus.Duplicate, second.Status);
        Assert.True(second.Duplicate);
        Assert.Single(_store.Keys);
    }

    [Fact]
    public async Task Same_message_over_the_other_transport_is_still_a_duplicate()
    {
        var service = Service();
        Assert.Equal(ReceiveStatus.Created, (await service.ReceiveAsync(Payload("ServiceBus"))).Status);
        Assert.Equal(ReceiveStatus.Duplicate, (await service.ReceiveAsync(Payload("Webhook"))).Status);
    }

    [Fact]
    public async Task Case_closed_again_later_is_a_new_message()
    {
        var reclosed = Fixture.ClosedNode().With("cd_closedon", Fixture.WcfDate(new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Utc)));
        var second = CaseClosedMapper.Map(Fixture.Parse(reclosed), "ServiceBus").Event!.ToJson();
        var service = Service();

        Assert.Equal(ReceiveStatus.Created, (await service.ReceiveAsync(Payload())).Status);
        Assert.Equal(ReceiveStatus.Created, (await service.ReceiveAsync(second)).Status);

        var keys = _store.Keys.ToList();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.Equal(Fixture.CaseId.ToString(), k.Pk)); // PartitionKey = case id
    }

    [Fact]
    public async Task Concurrent_duplicates_record_exactly_once()
    {
        var service = Service();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => service.ReceiveAsync(Payload())));
        Assert.Single(results, r => r.Status == ReceiveStatus.Created);
        Assert.Equal(19, results.Count(r => r.Duplicate));
    }

    [Fact]
    public async Task Get_by_case_number_returns_recorded_messages()
    {
        var service = Service();
        await service.ReceiveAsync(Payload());

        var records = await service.GetByCaseNumberAsync("CD-00042");
        var record = Assert.Single(records);
        Assert.Equal($"{Fixture.CaseId}:{Fixture.ClosedOnIso}", record.MessageId);
        Assert.Equal("ServiceBus", record.Transport);
        Assert.Empty(await service.GetByCaseNumberAsync("CD-99999"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"messageId":"","eventType":"CaseClosed","schemaVersion":"1.0","occurredAt":"x","transport":"Webhook","case":{"id":"a","caseNumber":"CD-00001","title":"t","category":"Other","priority":"Low","createdOn":"x","closedOn":"x","slaMet":true}}""")]
    public async Task Rejects_bodies_that_are_not_CaseClosed_v1(string body)
    {
        var result = await Service().ReceiveAsync(body);
        Assert.Equal(ReceiveStatus.BadRequest, result.Status);
        Assert.Empty(_store.Keys);
    }

    [Fact]
    public async Task Rejects_other_schema_versions()
    {
        var node = JsonNode.Parse(Payload())!;
        node["schemaVersion"] = "2.0";
        Assert.Equal(ReceiveStatus.BadRequest, (await Service().ReceiveAsync(node.ToJsonString())).Status);
    }

    [Theory]
    [InlineData("7d3f2a10-5c4b-4e8e-9b1a-2f6c8d9e0a11:2026-11-20T10:15:00.0000000Z")]
    [InlineData("a/b\\c#d?e")]
    [InlineData("tab\tnewline\ncontrol\u0001")]
    [InlineData("unicodé ✓")]
    public void Row_key_escape_is_valid_and_reversible(string messageId)
    {
        var escaped = RowKeys.Escape(messageId);
        Assert.True(RowKeys.IsValidKey(escaped), escaped);
        Assert.Equal(messageId, RowKeys.Unescape(escaped));
    }

    [Fact]
    public void Row_key_escape_does_not_collide_for_ids_that_a_naive_replace_would_merge()
    {
        Assert.NotEqual(RowKeys.Escape("a/b"), RowKeys.Escape("a_b"));
        Assert.NotEqual(RowKeys.Escape("a#b"), RowKeys.Escape("a?b"));
    }

    [Theory]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a#b", false)]
    [InlineData("a?b", false)]
    [InlineData("a\u0007b", false)]
    [InlineData("7d3f2a10-5c4b-4e8e-9b1a-2f6c8d9e0a11", true)]
    public void Detects_invalid_table_keys(string key, bool valid) => Assert.Equal(valid, RowKeys.IsValidKey(key));

    [Fact]
    public async Task End_to_end_handler_retry_is_recognised_as_duplicate_by_the_stub()
    {
        var service = Service();
        var bridge = new FakeHttpMessageHandler(async (_, body) =>
        {
            var r = await service.ReceiveAsync(body);
            return new HttpResponseMessage(r.Status switch
            {
                ReceiveStatus.Created => HttpStatusCode.Created,
                ReceiveStatus.Duplicate => HttpStatusCode.OK,
                _ => HttpStatusCode.BadRequest,
            });
        });
        var handler = new CaseClosedHandler(new HttpClient(bridge),
            Options.Create(new ExternalStubOptions { Url = "https://stub.example/api/external/caseclosed", Key = "k" }),
            NullLogger<CaseClosedHandler>.Instance);

        var first = await handler.HandleAsync(Fixture.ClosedJson(), "ServiceBus");
        var redelivered = await handler.HandleAsync(Fixture.ClosedJson(), "ServiceBus");

        Assert.Null(first.Reason);
        Assert.NotNull(redelivered.Reason); // duplicate at receiver
        Assert.Single(_store.Keys);
    }
}
