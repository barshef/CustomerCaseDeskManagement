using System.Net;
using System.Text.Json.Nodes;
using CaseDesk.Functions.CaseClosed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CaseDesk.Functions.Tests;

public class CaseClosedHandlerTests
{
    private const string StubUrl = "https://casedesk-fn.example/api/external/caseclosed";
    private const string StubKey = "test-key-not-a-secret";

    private static CaseClosedHandler CreateHandler(HttpMessageHandler fake) =>
        new(new HttpClient(fake),
            Options.Create(new ExternalStubOptions { Url = StubUrl, Key = StubKey }),
            NullLogger<CaseClosedHandler>.Instance);

    [Theory]
    [InlineData("ServiceBus")]
    [InlineData("Webhook")]
    public async Task Posts_mapped_payload_with_function_key_header(string transport)
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.Created);
        var result = await CreateHandler(fake).HandleAsync(Fixture.ClosedJson(), transport);

        Assert.Equal(HandleOutcome.Forwarded, result.Outcome);
        Assert.Equal($"{Fixture.CaseId}:{Fixture.ClosedOnIso}", result.MessageId);

        var (request, body) = Assert.Single(fake.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(StubUrl, request.RequestUri!.ToString());
        Assert.Equal(StubKey, Assert.Single(request.Headers.GetValues("x-functions-key")));
        Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);

        var json = JsonNode.Parse(body)!;
        Assert.Equal(transport, (string?)json["transport"]);
        Assert.Equal("CD-00042", (string?)json["case"]!["caseNumber"]);
        Assert.Equal("Refund", (string?)json["case"]!["category"]);
        CaseClosedMapperTests.AssertValid(body);
    }

    [Fact]
    public async Task Duplicate_at_receiver_is_still_forwarded()
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.OK);
        var result = await CreateHandler(fake).HandleAsync(Fixture.ClosedJson(), "ServiceBus");
        Assert.Equal(HandleOutcome.Forwarded, result.Outcome);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public async Task Throws_when_external_system_returns_500_so_the_transport_retries()
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => CreateHandler(fake).HandleAsync(Fixture.ClosedJson(), "ServiceBus"));
        Assert.Equal(HttpStatusCode.InternalServerError, ex.StatusCode);
    }

    [Fact]
    public async Task Ignored_context_does_not_call_external_system_or_throw()
    {
        var node = Fixture.ClosedNode().With("statuscode", Fixture.OptionSet(417200006));
        var fake = new FakeHttpMessageHandler(HttpStatusCode.Created);

        var result = await CreateHandler(fake).HandleAsync(node.ToJsonString(), "ServiceBus");

        Assert.Equal(HandleOutcome.Ignored, result.Outcome);
        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Malformed_body_is_invalid_and_not_forwarded(string body)
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.Created);
        var result = await CreateHandler(fake).HandleAsync(body, "Webhook");
        Assert.Equal(HandleOutcome.Invalid, result.Outcome);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Handles_utf8_bom_prefix()
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.Created);
        var result = await CreateHandler(fake).HandleAsync("﻿" + Fixture.ClosedJson(), "ServiceBus");
        Assert.Equal(HandleOutcome.Forwarded, result.Outcome);
    }

    // --- Resilience pipeline as registered in Program.cs (delays shortened for the test) ---

    private static CaseClosedHandler CreateResilientHandler(FakeHttpMessageHandler fake)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ExternalStubUrl"] = StubUrl,
            ["ExternalStubKey"] = StubKey,
        }).Build();

        var services = new ServiceCollection().AddLogging();
        services.AddCaseClosedForwarding(configuration, o =>
            {
                o.Retry.Delay = TimeSpan.FromMilliseconds(1);
                o.Retry.UseJitter = false;
            })
            .ConfigurePrimaryHttpMessageHandler(() => fake);
        return services.BuildServiceProvider().GetRequiredService<CaseClosedHandler>();
    }

    [Fact]
    public async Task Retries_three_times_then_throws()
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.InternalServerError);
        await Assert.ThrowsAsync<HttpRequestException>(() => CreateResilientHandler(fake).HandleAsync(Fixture.ClosedJson(), "ServiceBus"));
        Assert.Equal(4, fake.Requests.Count); // 1 attempt + 3 retries
        Assert.All(fake.Requests, r => Assert.Equal(StubKey, r.Request.Headers.GetValues("x-functions-key").Single()));
    }

    [Fact]
    public async Task Recovers_when_external_system_comes_back()
    {
        var fake = new FakeHttpMessageHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.Created);
        var result = await CreateResilientHandler(fake).HandleAsync(Fixture.ClosedJson(), "Webhook");
        Assert.Equal(HandleOutcome.Forwarded, result.Outcome);
        Assert.Equal(2, fake.Requests.Count);
    }
}
