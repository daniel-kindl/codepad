using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Codepad.Domain.Operations;
using Codepad.Domain.Results;

namespace Codepad.Application.Json;

/// <summary>
/// Checks and formats JSON with <c>System.Text.Json</c>. It is deterministic and side-effect-free. It first checks the
/// size, then parses with a depth limit, then builds the formatted text and a tree that respects the
/// <see cref="ResultPolicy"/>. Numbers stay as the user wrote them, so the provider never rounds a value.
/// </summary>
public sealed class JsonEvaluator : IJsonEvaluator
{
    private static readonly JsonWriterOptions s_writerOptions = new JsonWriterOptions
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonLimits limits;
    private readonly ResultPolicy policy;

    /// <summary>Initializes a new instance of the <see cref="JsonEvaluator"/> class with the default limits.</summary>
    public JsonEvaluator()
        : this(JsonLimits.Default, ResultPolicy.Default)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="JsonEvaluator"/> class.</summary>
    /// <param name="limits">The document limits.</param>
    /// <param name="policy">The limits of the tree. Null uses <see cref="ResultPolicy.Default"/>.</param>
    public JsonEvaluator(JsonLimits limits, ResultPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(limits);
        this.limits = limits;
        this.policy = policy ?? ResultPolicy.Default;
    }

    /// <inheritdoc />
    public Task<JsonEvaluation> EvaluateAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Task.Run(() => Evaluate(text), cancellationToken);
    }

    /// <summary>Evaluates one text on the calling thread.</summary>
    /// <param name="text">The JSON text.</param>
    /// <returns>The result. An invalid text is a result, not an exception.</returns>
    public JsonEvaluation Evaluate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Fail("JSON_EMPTY", "Type or paste JSON to see it formatted.", null);
        }

        // A string that has more chars than the limit has more bytes too, so the cheap check runs first.
        if (text.Length > limits.MaxBytes || Encoding.UTF8.GetByteCount(text) > limits.MaxBytes)
        {
            return Fail("JSON_TOO_LARGE", string.Create(CultureInfo.InvariantCulture, $"The document is larger than the limit of {limits.MaxBytes:N0} bytes. CodePad does not check it."), null);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = limits.MaxDepth });
            return JsonEvaluation.Valid(Format(document.RootElement), ToNode(document.RootElement, 0, new Budget(policy.MaxNodes - 1)));
        }
        catch (JsonException exception)
        {
            return Describe(exception, text);
        }
    }

    private static string Format(JsonElement root)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, s_writerOptions))
        {
            root.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private ResultNode ToNode(JsonElement element, int depth, Budget budget)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return ToObject(element, depth, budget);
            case JsonValueKind.Array:
                return ToSequence(element, depth, budget);
            case JsonValueKind.String:
                string value = element.GetString() ?? string.Empty;
                return value.Length <= policy.MaxTextLength ? new TextNode(value, null) : new TextNode(value[..policy.MaxTextLength], value.Length);
            case JsonValueKind.Number:
                string number = element.GetRawText();
                return number.Length <= policy.MaxTextLength ? new ScalarNode("number", number) : new TextNode(number[..policy.MaxTextLength], number.Length);
            case JsonValueKind.True:
                return new ScalarNode("boolean", "true");
            case JsonValueKind.False:
                return new ScalarNode("boolean", "false");
            default:
                return new NullNode();
        }
    }

    private ResultNode ToObject(JsonElement element, int depth, Budget budget)
    {
        if (depth >= policy.MaxDepth)
        {
            return new TruncatedNode("object", TruncationReason.DepthLimit);
        }

        // A duplicate name keeps the last value, as most parsers do. The formatted text still shows both.
        Dictionary<string, JsonElement> members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        List<string> order = [];
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (members.TryAdd(property.Name, property.Value))
            {
                order.Add(property.Name);
            }
            else
            {
                members[property.Name] = property.Value;
            }
        }

        int take = Math.Min(Math.Min(policy.MaxItems, budget.Remaining), order.Count);
        TruncationReason? truncation = order.Count > take ? (take == policy.MaxItems ? TruncationReason.ItemLimit : TruncationReason.NodeLimit) : null;
        budget.Remaining -= take;
        List<ResultProperty> properties = [];
        foreach (string name in order.Take(take))
        {
            properties.Add(new ResultProperty(name, ToNode(members[name], depth + 1, budget)));
        }

        return new ObjectNode("object", properties, truncation);
    }

    private ResultNode ToSequence(JsonElement element, int depth, Budget budget)
    {
        if (depth >= policy.MaxDepth)
        {
            return new TruncatedNode("array", TruncationReason.DepthLimit);
        }

        int length = element.GetArrayLength();
        int take = Math.Min(Math.Min(policy.MaxItems, budget.Remaining), length);
        TruncationReason? truncation = length > take ? (take == policy.MaxItems ? TruncationReason.ItemLimit : TruncationReason.NodeLimit) : null;
        budget.Remaining -= take;
        List<ResultNode> items = [];
        foreach (JsonElement item in element.EnumerateArray().Take(take))
        {
            items.Add(ToNode(item, depth + 1, budget));
        }

        return new SequenceNode("array", items, truncation);
    }

    private JsonEvaluation Describe(JsonException exception, string text)
    {
        if (exception.Message.StartsWith("The maximum configured depth", StringComparison.Ordinal))
        {
            return Fail("JSON_TOO_DEEP", string.Create(CultureInfo.InvariantCulture, $"The document nests deeper than the limit of {limits.MaxDepth} levels. CodePad does not check it."), Locate(exception, text));
        }

        // The framework message ends with the path and the position in its own format. CodePad shows the position itself.
        string message = exception.Message;
        foreach (string marker in new[] { " Path:", " LineNumber:" })
        {
            int index = message.IndexOf(marker, StringComparison.Ordinal);
            if (index >= 0)
            {
                message = message[..index];
            }
        }

        return Fail("JSON_SYNTAX", message.TrimEnd(), Locate(exception, text));
    }

    private static SourceLocation? Locate(JsonException exception, string text)
    {
        if (exception.LineNumber is not long zeroLine || exception.BytePositionInLine is not long bytes)
        {
            return null;
        }

        // The framework counts lines from 0 and columns in UTF-8 bytes. The user sees lines and characters from 1.
        string[] lines = text.Split('\n');
        if (zeroLine < 0 || zeroLine >= lines.Length)
        {
            return null;
        }

        byte[] line = Encoding.UTF8.GetBytes(lines[zeroLine]);
        int count = (int)Math.Min(Math.Max(bytes, 0), line.Length);
        return new SourceLocation((int)zeroLine + 1, Encoding.UTF8.GetString(line, 0, count).Length + 1);
    }

    private static JsonEvaluation Fail(string code, string message, SourceLocation? location) =>
        JsonEvaluation.Invalid(new Diagnostic(DiagnosticSeverity.Error, code, message, location));

    private sealed class Budget(int remaining)
    {
        public int Remaining { get; set; } = Math.Max(0, remaining);
    }
}
