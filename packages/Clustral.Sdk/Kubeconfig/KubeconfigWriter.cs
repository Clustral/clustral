using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Reads and patches <c>~/.kube/config</c> (or a custom path) to inject or
/// update a Clustral cluster, user, and context entry.
/// </summary>
/// <remarks>
/// <para>
/// Uses dictionary-based YAML round-tripping to preserve all existing entries
/// and fields (certificate-authority-data, exec auth, etc.) that the typed
/// model doesn't know about. Only the Clustral-managed entries are touched.
/// </para>
/// </remarks>
public sealed class KubeconfigWriter
{
    private static readonly IDeserializer RawDeserializer =
        new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

    private static readonly ISerializer RawSerializer =
        new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

    private readonly string _kubeconfigPath;

    public KubeconfigWriter() : this(DefaultKubeconfigPath()) { }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Dictionary<object, object>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(List<object>))]
    public KubeconfigWriter(string kubeconfigPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(kubeconfigPath);
        _kubeconfigPath = kubeconfigPath;
    }

    /// <summary>
    /// Returns <c>$KUBECONFIG</c> when set, otherwise <c>~/.kube/config</c>.
    /// </summary>
    public static string DefaultKubeconfigPath() =>
        Environment.GetEnvironmentVariable("KUBECONFIG")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kube",
            "config");

    /// <summary>
    /// Upserts cluster, user, and context entries for <paramref name="entry"/>
    /// into the kubeconfig file. All pre-existing entries and fields are preserved.
    /// </summary>
    public void WriteClusterEntry(ClustralKubeconfigEntry entry, bool setCurrentContext = true)
    {
        var doc = ReadRawDocument();

        var clusterData = new Dictionary<object, object>
        {
            ["server"] = entry.ServerUrl,
        };
        // Only emit insecure-skip-tls-verify when true — kubectl and Lens
        // don't expect the field to be present when false.
        if (entry.InsecureSkipTlsVerify)
            clusterData["insecure-skip-tls-verify"] = true;

        UpsertNamedEntry(doc, "clusters", entry.ContextName, clusterData);

        // Write the token as a static field. kubectl v1.32+ requires HTTPS
        // to send bearer tokens, so the server URL must use https://.
        // Also remove any stale exec credential plugin from previous versions.
        var users = GetList(doc, "users");
        var existingUser = users.OfType<Dictionary<object, object>>()
            .FirstOrDefault(e => e.TryGetValue("name", out var n) && n is string s && s == entry.ContextName);
        if (existingUser?.TryGetValue("user", out var raw) == true && raw is Dictionary<object, object> existingUserData)
        {
            existingUserData.Remove("exec");
        }

        UpsertNamedEntry(doc, "users", entry.ContextName, new Dictionary<object, object>
        {
            ["token"] = entry.Token,
        });

        UpsertNamedEntry(doc, "contexts", entry.ContextName, new Dictionary<object, object>
        {
            ["cluster"] = entry.ContextName,
            ["user"] = entry.ContextName,
        });

        if (setCurrentContext)
            doc["current-context"] = entry.ContextName;

        WriteRawDocument(doc);
    }

    /// <summary>
    /// Removes the cluster, user, and context entries whose name matches
    /// <paramref name="contextName"/>. All other entries are preserved.
    /// </summary>
    public void RemoveClusterEntry(string contextName)
    {
        ArgumentException.ThrowIfNullOrEmpty(contextName);

        var doc = ReadRawDocument();

        RemoveNamedEntry(doc, "clusters", contextName);
        RemoveNamedEntry(doc, "users", contextName);
        RemoveNamedEntry(doc, "contexts", contextName);

        // Fall back current-context if it was the removed one.
        if (doc.TryGetValue("current-context", out var cc) &&
            cc is string current && current == contextName)
        {
            var contexts = GetList(doc, "contexts");
            var first = contexts.OfType<Dictionary<object, object>>()
                .FirstOrDefault()?["name"] as string;
            doc["current-context"] = first ?? "";
        }

        WriteRawDocument(doc);
    }

    // -------------------------------------------------------------------------
    // Raw YAML helpers — preserve all unknown fields
    // -------------------------------------------------------------------------

    private static void UpsertNamedEntry(
        Dictionary<object, object> doc,
        string listKey,
        string name,
        Dictionary<object, object> data)
    {
        var list = GetList(doc, listKey);

        // Find existing entry by name.
        var existing = list.OfType<Dictionary<object, object>>()
            .FirstOrDefault(e => e.TryGetValue("name", out var n) && n is string s && s == name);

        // The data key is the singular form: clusters→cluster, users→user, contexts→context.
        var dataKey = listKey.TrimEnd('s');

        if (existing is not null)
        {
            // Merge into existing data dict — only overwrite Clustral fields.
            if (existing.TryGetValue(dataKey, out var raw) && raw is Dictionary<object, object> existingData)
            {
                foreach (var kv in data)
                    existingData[kv.Key] = kv.Value;
            }
            else
            {
                existing[dataKey] = data;
            }
        }
        else
        {
            list.Add(new Dictionary<object, object>
            {
                ["name"] = name,
                [dataKey] = data,
            });
        }
    }

    private static void RemoveNamedEntry(
        Dictionary<object, object> doc,
        string listKey,
        string name)
    {
        var list = GetList(doc, listKey);
        list.RemoveAll(item =>
            item is Dictionary<object, object> d &&
            d.TryGetValue("name", out var n) &&
            n is string s && s == name);
    }

    private static List<object> GetList(Dictionary<object, object> doc, string key)
    {
        if (doc.TryGetValue(key, out var raw) && raw is List<object> list)
            return list;

        var newList = new List<object>();
        doc[key] = newList;
        return newList;
    }

    // -------------------------------------------------------------------------

    private Dictionary<object, object> ReadRawDocument()
    {
        if (!File.Exists(_kubeconfigPath))
            return NewEmptyDocument();

        var yaml = File.ReadAllText(_kubeconfigPath);
        if (string.IsNullOrWhiteSpace(yaml))
            return NewEmptyDocument();

        var result = RawDeserializer.Deserialize<Dictionary<object, object>>(yaml);
        return result ?? NewEmptyDocument();
    }

    private void WriteRawDocument(Dictionary<object, object> doc)
    {
        var dir = Path.GetDirectoryName(_kubeconfigPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var yaml = RawSerializer.Serialize(doc);
        File.WriteAllText(_kubeconfigPath, yaml);
    }

    private static Dictionary<object, object> NewEmptyDocument() => new()
    {
        ["apiVersion"] = "v1",
        ["kind"] = "Config",
        ["preferences"] = new Dictionary<object, object>(),
        ["clusters"] = new List<object>(),
        ["users"] = new List<object>(),
        ["contexts"] = new List<object>(),
        ["current-context"] = "",
    };
}

/// <summary>
/// The data needed to write a Clustral entry into a kubeconfig file.
/// </summary>
/// <param name="ContextName">
/// Name used for the cluster, user, and context entries (e.g. <c>clustral-prod</c>).
/// </param>
/// <param name="ServerUrl">
/// ControlPlane kubectl proxy URL (e.g. <c>https://cp.example.com/proxy/prod</c>).
/// </param>
/// <param name="Token">Short-lived Clustral bearer token from <c>AuthService</c>.</param>
/// <param name="ExpiresAt">When the token expires — stored for CLI display only.</param>
/// <param name="InsecureSkipTlsVerify">
/// <c>true</c> only for local dev (kind). Never use in production.
/// </param>
public sealed record ClustralKubeconfigEntry(
    string         ContextName,
    string         ServerUrl,
    string         Token,
    DateTimeOffset ExpiresAt,
    bool           InsecureSkipTlsVerify = false);
