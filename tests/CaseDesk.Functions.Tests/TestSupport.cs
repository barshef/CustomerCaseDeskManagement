using System.Net;
using System.Text.Json.Nodes;
using CaseDesk.Functions.CaseClosed;
using CaseDesk.Functions.Dataverse;
using CaseDesk.Functions.ExternalStub;

namespace CaseDesk.Functions.Tests;

/// <summary>Loads and tweaks the RemoteExecutionContext fixture.</summary>
internal static class Fixture
{
    public static readonly Guid CaseId = Guid.Parse("7d3f2a10-5c4b-4e8e-9b1a-2f6c8d9e0a11");
    public const string ClosedOnIso = "2026-11-20T10:15:00.0000000Z";

    public static string Path(string name) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string ClosedJson() => File.ReadAllText(Path("remote-execution-context-closed.json"));

    public static JsonNode ClosedNode() => JsonNode.Parse(ClosedJson())!;

    public static RemoteExecutionContext Closed() => RemoteExecutionContext.Parse(ClosedJson());

    public static RemoteExecutionContext Parse(JsonNode node) => RemoteExecutionContext.Parse(node.ToJsonString());

    public static JsonArray PostImageAttributes(JsonNode root) =>
        root["PostEntityImages"]![0]!["value"]!["Attributes"]!.AsArray();

    public static JsonNode With(this JsonNode root, string attribute, JsonNode? value)
    {
        var attributes = PostImageAttributes(root);
        var existing = attributes.FirstOrDefault(a => (string?)a!["key"] == attribute);
        if (existing is not null) attributes.Remove(existing);
        if (value is not null) attributes.Add(new JsonObject { ["key"] = attribute, ["value"] = value });
        return root;
    }

    public static JsonNode Without(this JsonNode root, string attribute) => root.With(attribute, null);

    public static JsonNode OptionSet(int value) => new JsonObject
    {
        ["__type"] = "OptionSetValue:http://schemas.microsoft.com/xrm/2011/Contracts",
        ["Value"] = value,
    };

    public static JsonNode WcfDate(DateTime utc) =>
        JsonValue.Create($"/Date({new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeMilliseconds()})/");
}

/// <summary>Captures requests and replies with a scripted sequence of status codes.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpStatusCode> _responses;
    private readonly HttpStatusCode _default;
    private readonly Func<HttpRequestMessage, string, Task<HttpResponseMessage>>? _responder;

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    public FakeHttpMessageHandler(params HttpStatusCode[] responses)
    {
        _responses = new Queue<HttpStatusCode>(responses);
        _default = responses.Length > 0 ? responses[^1] : HttpStatusCode.Created;
    }

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string, Task<HttpResponseMessage>> responder) : this()
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        if (_responder is not null) return await _responder(request, body);
        var status = _responses.Count > 0 ? _responses.Dequeue() : _default;
        return new HttpResponseMessage(status) { Content = new StringContent("{}") };
    }
}

/// <summary>In-memory de-dup store with Table Storage key semantics (PK + escaped RK must be unique).</summary>
internal sealed class InMemoryCaseClosedReceivedStore : ICaseClosedReceivedStore
{
    private readonly Dictionary<(string Pk, string Rk), CaseClosedReceivedRecord> _rows = new();
    private readonly object _gate = new();

    public IReadOnlyCollection<(string Pk, string Rk)> Keys
    {
        get { lock (_gate) return _rows.Keys.ToList(); }
    }

    public Task<bool> TryAddAsync(CaseClosedReceivedRecord record, CancellationToken cancellationToken = default)
    {
        var key = (record.CaseId, RowKeys.Escape(record.MessageId));
        Assert.True(RowKeys.IsValidKey(key.Item1), "PartitionKey must be a valid Table Storage key");
        Assert.True(RowKeys.IsValidKey(key.Item2), "RowKey must be a valid Table Storage key");
        lock (_gate) return Task.FromResult(_rows.TryAdd(key, record));
    }

    public Task<IReadOnlyList<CaseClosedReceivedRecord>> GetByCaseNumberAsync(string caseNumber, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult<IReadOnlyList<CaseClosedReceivedRecord>>(
                _rows.Values.Where(r => r.CaseNumber == caseNumber).ToList());
    }
}
