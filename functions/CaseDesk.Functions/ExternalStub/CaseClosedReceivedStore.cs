using System.Text;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;

namespace CaseDesk.Functions.ExternalStub;

/// <summary>One CaseClosed message recorded by the external stub.</summary>
public sealed record CaseClosedReceivedRecord(
    string CaseId,
    string MessageId,
    string CaseNumber,
    string Transport,
    DateTimeOffset ReceivedAt,
    string Payload);

/// <summary>De-dup store for the stub. Abstracted so the de-dup logic is testable without Azure.</summary>
public interface ICaseClosedReceivedStore
{
    /// <summary>Records the message. Returns <c>false</c> if a record with the same (case id, message id) already exists.</summary>
    Task<bool> TryAddAsync(CaseClosedReceivedRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CaseClosedReceivedRecord>> GetByCaseNumberAsync(string caseNumber, CancellationToken cancellationToken = default);
}

public static class RowKeys
{
    /// <summary>
    /// Table Storage forbids '/', '\', '#', '?' and control characters in PartitionKey/RowKey.
    /// Message ids contain ':' and '.', which are fine, but we base64url-encode the whole id so
    /// any future id format is safe and the mapping is reversible and collision-free.
    /// </summary>
    public static string Escape(string key) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(key)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Unescape(string escaped)
    {
        var s = escaped.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    private static readonly char[] Forbidden = ['/', '\\', '#', '?'];

    public static bool IsValidKey(string key) =>
        key.Length <= 1024 && key.IndexOfAny(Forbidden) < 0 && !key.Any(char.IsControl);
}

/// <summary>Azure Table Storage implementation: table <c>CaseClosedReceived</c>, PK = case id, RK = escaped message id.</summary>
public sealed class TableCaseClosedReceivedStore : ICaseClosedReceivedStore
{
    public const string TableName = "CaseClosedReceived";

    private readonly TableClient _table;
    private readonly Lazy<Task> _ensureTable;

    /// <param name="connectionString">A storage connection string (local: <c>UseDevelopmentStorage=true</c>).</param>
    public TableCaseClosedReceivedStore(string connectionString)
        : this(new TableServiceClient(connectionString)) { }

    /// <summary>Identity-based access (Flex Consumption: <c>AzureWebJobsStorage__accountName</c>).</summary>
    public static TableCaseClosedReceivedStore ForAccount(string accountName) =>
        new(new TableServiceClient(new Uri($"https://{accountName}.table.core.windows.net"), new DefaultAzureCredential()));

    private TableCaseClosedReceivedStore(TableServiceClient service)
    {
        _table = service.GetTableClient(TableName);
        _ensureTable = new Lazy<Task>(() => _table.CreateIfNotExistsAsync());
    }

    public async Task<bool> TryAddAsync(CaseClosedReceivedRecord record, CancellationToken cancellationToken = default)
    {
        await _ensureTable.Value.ConfigureAwait(false);
        var entity = new TableEntity(record.CaseId, RowKeys.Escape(record.MessageId))
        {
            ["MessageId"] = record.MessageId,
            ["CaseNumber"] = record.CaseNumber,
            ["Transport"] = record.Transport,
            ["ReceivedAt"] = record.ReceivedAt,
            ["Payload"] = record.Payload,
        };
        try
        {
            // AddEntity (insert, not upsert) is atomic: a concurrent duplicate gets 409 Conflict.
            await _table.AddEntityAsync(entity, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CaseClosedReceivedRecord>> GetByCaseNumberAsync(string caseNumber, CancellationToken cancellationToken = default)
    {
        await _ensureTable.Value.ConfigureAwait(false);
        var results = new List<CaseClosedReceivedRecord>();
        // Demo aid only: CaseNumber is not a key, so this is a table scan.
        await foreach (var e in _table.QueryAsync<TableEntity>(TableClient.CreateQueryFilter($"CaseNumber eq {caseNumber}"), cancellationToken: cancellationToken))
        {
            results.Add(new CaseClosedReceivedRecord(
                e.PartitionKey,
                e.GetString("MessageId") ?? RowKeys.Unescape(e.RowKey),
                e.GetString("CaseNumber") ?? "",
                e.GetString("Transport") ?? "",
                e.GetDateTimeOffset("ReceivedAt") ?? e.Timestamp ?? default,
                e.GetString("Payload") ?? ""));
        }
        return results;
    }
}
