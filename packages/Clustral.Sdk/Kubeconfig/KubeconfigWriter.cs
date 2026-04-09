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
    /// Writes a cluster entry with the specified merge strategy.
    /// </summary>
    public void WriteClusterEntry(
        ClustralKubeconfigEntry entry,
        MergeStrategy strategy,
        bool setCurrentContext = true)
    {
        if (strategy == MergeStrategy.Replace)
        {
            WriteClusterEntry(entry, setCurrentContext);
            return;
        }

        var doc = ReadRawDocument();
        var exists = EntryExists(doc, "contexts", entry.ContextName);

        if (exists && strategy == MergeStrategy.FailOnConflict)
            throw new InvalidOperationException(
                $"Context '{entry.ContextName}' already exists and merge strategy is FailOnConflict.");

        if (exists && strategy == MergeStrategy.SkipExisting)
            return;

        WriteClusterEntry(entry, setCurrentContext);
    }

    /// <summary>
    /// Writes a certificate-based kubeconfig entry.
    /// </summary>
    public void WriteClusterEntry(CertificateKubeconfigEntry entry, bool setCurrentContext = true)
    {
        var doc = ReadRawDocument();

        var clusterData = new Dictionary<object, object> { ["server"] = entry.ServerUrl };
        if (entry.CertificateAuthorityPath is not null)
            clusterData["certificate-authority"] = entry.CertificateAuthorityPath;
        if (entry.CertificateAuthorityData is not null)
            clusterData["certificate-authority-data"] = entry.CertificateAuthorityData;
        if (entry.InsecureSkipTlsVerify)
            clusterData["insecure-skip-tls-verify"] = true;

        UpsertNamedEntry(doc, "clusters", entry.ContextName, clusterData);

        var userData = new Dictionary<object, object>();
        if (entry.ClientCertificatePath is not null)
            userData["client-certificate"] = entry.ClientCertificatePath;
        if (entry.ClientCertificateData is not null)
            userData["client-certificate-data"] = entry.ClientCertificateData;
        if (entry.ClientKeyPath is not null)
            userData["client-key"] = entry.ClientKeyPath;
        if (entry.ClientKeyData is not null)
            userData["client-key-data"] = entry.ClientKeyData;

        UpsertNamedEntry(doc, "users", entry.ContextName, userData);

        var contextData = new Dictionary<object, object>
        {
            ["cluster"] = entry.ContextName,
            ["user"] = entry.ContextName,
        };
        if (entry.Namespace is not null)
            contextData["namespace"] = entry.Namespace;

        UpsertNamedEntry(doc, "contexts", entry.ContextName, contextData);

        if (setCurrentContext)
            doc["current-context"] = entry.ContextName;

        WriteRawDocument(doc);
    }

    /// <summary>
    /// Writes an exec-based kubeconfig entry (preferred for enterprise human access).
    /// </summary>
    public void WriteClusterEntry(ExecKubeconfigEntry entry, bool setCurrentContext = true)
    {
        var doc = ReadRawDocument();

        var clusterData = new Dictionary<object, object> { ["server"] = entry.ServerUrl };
        if (entry.CertificateAuthorityPath is not null)
            clusterData["certificate-authority"] = entry.CertificateAuthorityPath;
        if (entry.CertificateAuthorityData is not null)
            clusterData["certificate-authority-data"] = entry.CertificateAuthorityData;
        if (entry.InsecureSkipTlsVerify)
            clusterData["insecure-skip-tls-verify"] = true;

        UpsertNamedEntry(doc, "clusters", entry.ContextName, clusterData);

        var execData = new Dictionary<object, object>
        {
            ["apiVersion"] = entry.ExecApiVersion,
            ["command"] = entry.ExecCommand,
        };
        if (entry.ExecArgs is { Length: > 0 })
            execData["args"] = new List<object>(entry.ExecArgs);
        if (entry.ExecEnv is { Count: > 0 })
        {
            execData["env"] = entry.ExecEnv
                .Select(kv => (object)new Dictionary<object, object>
                {
                    ["name"] = kv.Key,
                    ["value"] = kv.Value,
                })
                .ToList();
        }
        if (entry.InstallHint is not null)
            execData["installHint"] = entry.InstallHint;

        var userData = new Dictionary<object, object> { ["exec"] = execData };
        UpsertNamedEntry(doc, "users", entry.ContextName, userData);

        var contextData = new Dictionary<object, object>
        {
            ["cluster"] = entry.ContextName,
            ["user"] = entry.ContextName,
        };
        if (entry.Namespace is not null)
            contextData["namespace"] = entry.Namespace;

        UpsertNamedEntry(doc, "contexts", entry.ContextName, contextData);

        if (setCurrentContext)
            doc["current-context"] = entry.ContextName;

        WriteRawDocument(doc);
    }

    // -------------------------------------------------------------------------

    private static bool EntryExists(Dictionary<object, object> doc, string listKey, string name)
    {
        if (!doc.TryGetValue(listKey, out var raw) || raw is not List<object> list)
            return false;
        return list.OfType<Dictionary<object, object>>()
            .Any(e => e.TryGetValue("name", out var n) && n is string s && s == name);
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
    // Inspection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the names of all contexts defined in the kubeconfig file.
    /// Returns an empty list if the file does not exist or has no contexts.
    /// </summary>
    public IReadOnlyList<string> ListContextNames()
    {
        if (!File.Exists(_kubeconfigPath))
            return [];

        var doc = ReadRawDocument();
        var contexts = GetList(doc, "contexts");

        var names = new List<string>(contexts.Count);
        foreach (var item in contexts)
        {
            if (item is Dictionary<object, object> ctx &&
                ctx.TryGetValue("name", out var nameObj) &&
                nameObj is string name &&
                !string.IsNullOrEmpty(name))
            {
                names.Add(name);
            }
        }
        return names;
    }

    /// <summary>
    /// Returns the value of <c>current-context</c> from the kubeconfig file,
    /// or <c>null</c> if the file does not exist or no current context is set.
    /// </summary>
    public string? GetCurrentContext()
    {
        if (!File.Exists(_kubeconfigPath))
            return null;

        var doc = ReadRawDocument();
        if (doc.TryGetValue("current-context", out var cc) && cc is string s && !string.IsNullOrEmpty(s))
            return s;

        return null;
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates the current kubeconfig for structural integrity:
    /// reference consistency and duplicate names.
    /// </summary>
    public KubeconfigValidationResult Validate() =>
        Validate(KubeconfigSecurityPolicy.Permissive);

    /// <summary>
    /// Validates the current kubeconfig for structural integrity
    /// and security policy compliance.
    /// </summary>
    public KubeconfigValidationResult Validate(KubeconfigSecurityPolicy policy)
    {
        var doc = ReadRawDocument();
        return ValidateDocument(doc, policy);
    }

    /// <summary>
    /// Validates a raw kubeconfig YAML string.
    /// </summary>
    public static KubeconfigValidationResult ValidateYaml(string yaml) =>
        ValidateYaml(yaml, KubeconfigSecurityPolicy.Permissive);

    /// <summary>
    /// Validates a raw kubeconfig YAML string with security policy.
    /// </summary>
    public static KubeconfigValidationResult ValidateYaml(string yaml, KubeconfigSecurityPolicy policy)
    {
        var doc = RawDeserializer.Deserialize<Dictionary<object, object>>(yaml);
        return ValidateDocument(doc ?? new Dictionary<object, object>(), policy);
    }

    internal static KubeconfigValidationResult ValidateDocument(
        Dictionary<object, object> doc, KubeconfigSecurityPolicy policy)
    {
        var errors = new List<KubeconfigValidationError>();

        var clusterNames = CollectNames(doc, "clusters");
        var userNames = CollectNames(doc, "users");
        var contextNames = CollectNames(doc, "contexts");

        // ── Duplicate names ──────────────────────────────────────────────
        CheckDuplicates(errors, doc, "clusters");
        CheckDuplicates(errors, doc, "users");
        CheckDuplicates(errors, doc, "contexts");

        // ── Reference integrity ──────────────────────────────────────────
        if (doc.TryGetValue("contexts", out var ctxRaw) && ctxRaw is List<object> contexts)
        {
            foreach (var item in contexts.OfType<Dictionary<object, object>>())
            {
                var name = item.TryGetValue("name", out var n) && n is string s ? s : "(unknown)";
                if (item.TryGetValue("context", out var dataRaw) && dataRaw is Dictionary<object, object> data)
                {
                    if (data.TryGetValue("cluster", out var cr) && cr is string clusterRef && !clusterNames.Contains(clusterRef))
                    {
                        errors.Add(new KubeconfigValidationError(
                            KubeconfigValidationSeverity.Error, "contexts", name, "cluster",
                            "DANGLING_CONTEXT_CLUSTER",
                            $"Context '{name}' references cluster '{clusterRef}' which does not exist."));
                    }
                    if (data.TryGetValue("user", out var ur) && ur is string userRef && !userNames.Contains(userRef))
                    {
                        errors.Add(new KubeconfigValidationError(
                            KubeconfigValidationSeverity.Error, "contexts", name, "user",
                            "DANGLING_CONTEXT_USER",
                            $"Context '{name}' references user '{userRef}' which does not exist."));
                    }
                }
            }
        }

        // ── current-context reference ────────────────────────────────────
        if (doc.TryGetValue("current-context", out var cc) && cc is string currentCtx &&
            !string.IsNullOrEmpty(currentCtx) && !contextNames.Contains(currentCtx))
        {
            errors.Add(new KubeconfigValidationError(
                KubeconfigValidationSeverity.Error, "current-context", currentCtx, "current-context",
                "DANGLING_CURRENT_CONTEXT",
                $"current-context '{currentCtx}' references a context that does not exist."));
        }

        // ── Security policy ──────────────────────────────────────────────
        if (doc.TryGetValue("clusters", out var clRaw) && clRaw is List<object> clusters)
        {
            foreach (var item in clusters.OfType<Dictionary<object, object>>())
            {
                var name = item.TryGetValue("name", out var nn) && nn is string sn ? sn : "(unknown)";
                if (item.TryGetValue("cluster", out var dataRaw) && dataRaw is Dictionary<object, object> data)
                {
                    if (policy.ForbidInsecureSkipTls &&
                        data.TryGetValue("insecure-skip-tls-verify", out var insVal) &&
                        (insVal is true || (insVal is string insStr && insStr.Equals("true", StringComparison.OrdinalIgnoreCase))))
                    {
                        errors.Add(new KubeconfigValidationError(
                            KubeconfigValidationSeverity.Error, "clusters", name, "insecure-skip-tls-verify",
                            "INSECURE_TLS_FORBIDDEN",
                            $"Cluster '{name}' has insecure-skip-tls-verify: true, which is forbidden by security policy."));
                    }
                    if (policy.ForbidPlaintextServer &&
                        data.TryGetValue("server", out var srv) && srv is string srvStr &&
                        srvStr.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add(new KubeconfigValidationError(
                            KubeconfigValidationSeverity.Error, "clusters", name, "server",
                            "PLAINTEXT_SERVER_FORBIDDEN",
                            $"Cluster '{name}' uses plaintext HTTP server URL, which is forbidden by security policy."));
                    }
                }
            }
        }

        if (doc.TryGetValue("users", out var usRaw) && usRaw is List<object> users)
        {
            foreach (var item in users.OfType<Dictionary<object, object>>())
            {
                var name = item.TryGetValue("name", out var nn) && nn is string sn ? sn : "(unknown)";
                if (item.TryGetValue("user", out var dataRaw) && dataRaw is Dictionary<object, object> data)
                {
                    if (policy.ForbidStaticPasswords && data.ContainsKey("password"))
                    {
                        errors.Add(new KubeconfigValidationError(
                            KubeconfigValidationSeverity.Error, "users", name, "password",
                            "STATIC_PASSWORD_FORBIDDEN",
                            $"User '{name}' has a static password, which is forbidden by security policy."));
                    }
                    if (policy.ForbidStaticTokens && data.ContainsKey("token"))
                    {
                        errors.Add(new KubeconfigValidationError(
                            KubeconfigValidationSeverity.Error, "users", name, "token",
                            "STATIC_TOKEN_FORBIDDEN",
                            $"User '{name}' has a static token, which is forbidden by security policy."));
                    }
                }
            }
        }

        // ── Namespace requirement ────────────────────────────────────────
        if (policy.RequireNamespaceOnContexts &&
            doc.TryGetValue("contexts", out var ctxRaw2) && ctxRaw2 is List<object> contexts2)
        {
            foreach (var item in contexts2.OfType<Dictionary<object, object>>())
            {
                var name = item.TryGetValue("name", out var nn) && nn is string sn ? sn : "(unknown)";
                if (item.TryGetValue("context", out var dataRaw) && dataRaw is Dictionary<object, object> data)
                {
                    if (!data.TryGetValue("namespace", out var ns) || ns is not string nsStr || string.IsNullOrEmpty(nsStr))
                    {
                        errors.Add(new KubeconfigValidationError(
                            KubeconfigValidationSeverity.Error, "contexts", name, "namespace",
                            "NAMESPACE_REQUIRED",
                            $"Context '{name}' is missing a namespace, which is required by security policy."));
                    }
                }
            }
        }

        return new KubeconfigValidationResult(errors.Count == 0, errors);
    }

    private static HashSet<string> CollectNames(Dictionary<object, object> doc, string listKey)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (doc.TryGetValue(listKey, out var raw) && raw is List<object> list)
        {
            foreach (var item in list.OfType<Dictionary<object, object>>())
            {
                if (item.TryGetValue("name", out var n) && n is string name)
                    names.Add(name);
            }
        }
        return names;
    }

    private static void CheckDuplicates(
        List<KubeconfigValidationError> errors,
        Dictionary<object, object> doc,
        string listKey)
    {
        if (!doc.TryGetValue(listKey, out var raw) || raw is not List<object> list)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in list.OfType<Dictionary<object, object>>())
        {
            if (item.TryGetValue("name", out var n) && n is string name)
            {
                if (!seen.Add(name))
                {
                    errors.Add(new KubeconfigValidationError(
                        KubeconfigValidationSeverity.Error, listKey, name, "name",
                        "DUPLICATE_NAME",
                        $"Duplicate {listKey.TrimEnd('s')} name '{name}' in {listKey} section."));
                }
            }
        }
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

        // Apply canonical ordering for deterministic output.
        var ordered = KubeconfigOrdering.OrderDocument(doc);
        var yaml = RawSerializer.Serialize(ordered);

        // Apply enterprise formatting (blank lines between sections, trailing newline).
        yaml = KubeconfigFormatter.Format(yaml);

        // Atomic write: write to temp file, then rename.
        var tempPath = _kubeconfigPath + ".tmp";
        File.WriteAllText(tempPath, yaml);

        // Set restrictive permissions on Unix (owner-only read/write).
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(tempPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
                // Best effort — not all filesystems support this.
            }
        }

        File.Move(tempPath, _kubeconfigPath, overwrite: true);
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
