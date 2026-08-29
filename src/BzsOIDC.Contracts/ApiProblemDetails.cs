using System.Text.Json.Serialization;

namespace BzsOIDC.Contracts;

/// <summary>Browser-safe RFC 9457 problem payload used by BzsOIDC APIs.</summary>
public class ApiProblemDetails
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("errors")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    public ApiProblemDetails()
    {
    }

    public ApiProblemDetails(int status, string code, string traceId, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        Status = status;
        Code = code;
        TraceId = traceId;
        Errors = errors;
    }
}

public static class ApiProblemDetailsExtensions
{
    public const string CodePropertyName = "code";
    public const string TraceIdPropertyName = "traceId";
    public const string ErrorsPropertyName = "errors";
}

/// <summary>Short transport name retained for clients that model RFC problem details directly.</summary>
public sealed class ProblemDetails : ApiProblemDetails
{
    public ProblemDetails()
    {
    }

    public ProblemDetails(int status, string code, string traceId, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(status, code, traceId, errors)
    {
    }
}

public static class ProblemDetailsExtensions
{
    public const string CodePropertyName = ApiProblemDetailsExtensions.CodePropertyName;
    public const string TraceIdPropertyName = ApiProblemDetailsExtensions.TraceIdPropertyName;
    public const string ErrorsPropertyName = ApiProblemDetailsExtensions.ErrorsPropertyName;
}
