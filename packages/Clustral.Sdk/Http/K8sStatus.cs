using System.Text.Json.Serialization;

namespace Clustral.Sdk.Http;

/// <summary>
/// Wire-level POCO for a Kubernetes <c>v1.Status</c> error object. This is
/// the shape <c>kubectl</c> natively parses — it reads <see cref="Message"/>
/// and renders <c>"error: &lt;message&gt;"</c> to the user.
///
/// Used exclusively on the <c>/api/proxy/*</c> path. Every other HTTP
/// surface returns RFC 7807 Problem Details instead. See the "Error Response
/// Shapes" section of the root README for the full rationale behind the
/// path-aware split.
/// </summary>
public sealed class K8sStatus
{
    [JsonPropertyName("kind")]       public string Kind       { get; set; } = "Status";
    [JsonPropertyName("apiVersion")] public string ApiVersion { get; set; } = "v1";
    [JsonPropertyName("metadata")]   public object Metadata   { get; set; } = new { };

    /// <summary>Either "Success" or "Failure".</summary>
    [JsonPropertyName("status")]  public string Status  { get; set; } = "Failure";

    /// <summary>One-line human-readable message — what kubectl prints.</summary>
    [JsonPropertyName("message")] public string Message { get; set; } = "";

    /// <summary>k8s-standard enum: Unauthorized, Forbidden, NotFound, Invalid,
    /// AlreadyExists, Timeout, BadRequest, InternalError, ServiceUnavailable, etc.</summary>
    [JsonPropertyName("reason")]  public string Reason  { get; set; } = "";

    /// <summary>HTTP status code.</summary>
    [JsonPropertyName("code")]    public int    Code    { get; set; }

    [JsonPropertyName("details"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public K8sStatusDetails? Details { get; set; }
}

public sealed class K8sStatusDetails
{
    [JsonPropertyName("group"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Group { get; set; }

    [JsonPropertyName("kind"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    [JsonPropertyName("causes"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<K8sStatusCause>? Causes { get; set; }
}

public sealed class K8sStatusCause
{
    /// <summary>
    /// Clustral-specific error code (e.g., <c>CLUSTER_MISMATCH</c>,
    /// <c>AGENT_NOT_CONNECTED</c>). Machine-parseable so programmatic
    /// clients can branch on it.
    /// </summary>
    [JsonPropertyName("reason")]  public string Reason  { get; set; } = "";

    [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonPropertyName("field"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { get; set; }
}
