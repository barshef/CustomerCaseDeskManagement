using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CaseDesk.Functions.Dataverse;

/// <summary>
/// The subset of the Dataverse <c>RemoteExecutionContext</c> we need, as Dataverse serialises it
/// for service endpoints with message format JSON (Service Bus) and for webhooks.
/// It is WCF DataContract JSON: collections are arrays of <c>{"key":..,"value":..}</c>, complex
/// attribute values carry a <c>"__type"</c> discriminator and dates look like <c>"/Date(ms)/"</c>.
/// </summary>
public sealed class RemoteExecutionContext
{
    public string? MessageName { get; set; }
    public string? PrimaryEntityName { get; set; }
    public Guid PrimaryEntityId { get; set; }
    public Guid CorrelationId { get; set; }
    public string? OrganizationName { get; set; }
    public int Stage { get; set; }
    public int Depth { get; set; }
    public List<KeyValueItem> InputParameters { get; set; } = new();
    public List<KeyValueItem<DataverseEntity>> PreEntityImages { get; set; } = new();
    public List<KeyValueItem<DataverseEntity>> PostEntityImages { get; set; } = new();

    public DataverseEntity? GetPostImage(string name) =>
        PostEntityImages.FirstOrDefault(i => string.Equals(i.Key, name, StringComparison.OrdinalIgnoreCase))?.Value;

    public DataverseEntity? GetPreImage(string name) =>
        PreEntityImages.FirstOrDefault(i => string.Equals(i.Key, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>Parses the JSON body. Throws <see cref="JsonException"/> on malformed input.</summary>
    public static RemoteExecutionContext Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        // Some transports prepend a UTF-8 BOM to the body.
        json = json.TrimStart('﻿', ' ', '\r', '\n', '\t');
        return JsonSerializer.Deserialize<RemoteExecutionContext>(json, Options)
               ?? throw new JsonException("Body is empty or 'null'.");
    }
}

public class KeyValueItem
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public JsonElement Value { get; set; }
}

public sealed class KeyValueItem<T>
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("value")] public T? Value { get; set; }
}

/// <summary>An <c>Entity</c> (Target or image) with typed attribute accessors.</summary>
public sealed class DataverseEntity
{
    public string? LogicalName { get; set; }
    public Guid Id { get; set; }
    public List<KeyValueItem> Attributes { get; set; } = new();

    public bool TryGet(string name, out JsonElement value)
    {
        foreach (var a in Attributes)
        {
            if (string.Equals(a.Key, name, StringComparison.OrdinalIgnoreCase)
                && a.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                value = a.Value;
                return true;
            }
        }
        value = default;
        return false;
    }

    public string? GetString(string name) =>
        TryGet(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : null;

    /// <summary>OptionSetValue (<c>{"__type":"OptionSetValue:..","Value":2}</c>) or a bare number.</summary>
    public int? GetOptionSetValue(string name)
    {
        if (!TryGet(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Object && TryGetProperty(v, "Value", out var inner)) v = inner;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32(),
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null,
        };
    }

    /// <summary>Money (<c>{"__type":"Money:..","Value":50.0}</c>) or a bare number.</summary>
    public decimal? GetMoney(string name)
    {
        if (!TryGet(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Object && TryGetProperty(v, "Value", out var inner)) v = inner;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null,
        };
    }

    /// <summary>EntityReference (<c>{"__type":"EntityReference:..","Id":"..","LogicalName":"account","Name":".."}</c>).</summary>
    public EntityReferenceValue? GetEntityReference(string name)
    {
        if (!TryGet(name, out var v) || v.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetProperty(v, "Id", out var id) || !Guid.TryParse(id.GetString(), out var guid)) return null;
        string? logicalName = TryGetProperty(v, "LogicalName", out var ln) && ln.ValueKind == JsonValueKind.String ? ln.GetString() : null;
        string? display = TryGetProperty(v, "Name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        return new EntityReferenceValue(guid, logicalName, display);
    }

    /// <summary>DateTime as WCF JSON (<c>/Date(ms)/</c>, <c>/Date(ms+0000)/</c>) or ISO-8601. Returned in UTC.</summary>
    public DateTime? GetDateTime(string name) =>
        TryGet(name, out var v) && v.ValueKind == JsonValueKind.String ? DataverseDate.ParseUtc(v.GetString()) : null;

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}

public sealed record EntityReferenceValue(Guid Id, string? LogicalName, string? Name);

public static partial class DataverseDate
{
    // "/Date(1700000000000)/", "/Date(1700000000000+0000)/", "/Date(-62135596800000-0500)/".
    // The milliseconds are always since the Unix epoch in UTC; the offset is informational only.
    [GeneratedRegex(@"^/Date\((?<ms>-?\d+)(?<offset>[+-]\d{4})?\)/$", RegexOptions.CultureInvariant)]
    private static partial Regex WcfDate();

    public static DateTime? ParseUtc(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = WcfDate().Match(text.Trim());
        if (m.Success)
        {
            var ms = long.Parse(m.Groups["ms"].Value, CultureInfo.InvariantCulture);
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto))
        {
            return dto.UtcDateTime;
        }
        throw new FormatException($"Unrecognised Dataverse date value '{text}'.");
    }
}
