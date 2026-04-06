namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Provides canonical ordering for kubeconfig documents to produce
/// deterministic, enterprise-readable YAML output.
/// </summary>
public static class KubeconfigOrdering
{
    /// <summary>Top-level keys in the canonical order defined by the Kubernetes kubeconfig spec.</summary>
    internal static readonly string[] TopLevelKeyOrder =
    [
        "apiVersion", "kind", "preferences",
        "clusters", "users", "contexts",
        "current-context",
    ];

    /// <summary>Canonical field order within a <c>cluster</c> data block.</summary>
    internal static readonly string[] ClusterFieldOrder =
    [
        "server", "tls-server-name",
        "certificate-authority", "certificate-authority-data",
        "insecure-skip-tls-verify", "proxy-url",
    ];

    /// <summary>Canonical field order within a <c>context</c> data block.</summary>
    internal static readonly string[] ContextFieldOrder =
    [
        "cluster", "user", "namespace",
    ];

    /// <summary>Canonical field order within a <c>user</c> data block.</summary>
    internal static readonly string[] UserFieldOrder =
    [
        "client-certificate", "client-certificate-data",
        "client-key", "client-key-data",
        "token", "username", "password",
        "auth-provider", "exec",
    ];

    /// <summary>
    /// Returns a new dictionary with all keys and nested structures ordered
    /// canonically. The original document is not modified.
    /// </summary>
    public static Dictionary<object, object> OrderDocument(Dictionary<object, object> doc)
    {
        var ordered = OrderKeys(doc, TopLevelKeyOrder);

        // Sort entries inside clusters, users, contexts alphabetically by name.
        SortListByName(ordered, "clusters");
        SortListByName(ordered, "users");
        SortListByName(ordered, "contexts");

        // Order fields within each entry's data section.
        OrderEntryFields(ordered, "clusters", "cluster", ClusterFieldOrder);
        OrderEntryFields(ordered, "users", "user", UserFieldOrder);
        OrderEntryFields(ordered, "contexts", "context", ContextFieldOrder);

        return ordered;
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a new dictionary with keys ordered: known keys first (in the
    /// specified order), then any unknown keys in their original order.
    /// </summary>
    internal static Dictionary<object, object> OrderKeys(
        Dictionary<object, object> source,
        string[] canonicalOrder)
    {
        var result = new Dictionary<object, object>();

        // Insert known keys in canonical order.
        foreach (var key in canonicalOrder)
        {
            if (source.TryGetValue(key, out var value))
                result[key] = value;
        }

        // Append unknown keys in their original order.
        var knownSet = new HashSet<string>(canonicalOrder);
        foreach (var kv in source)
        {
            if (kv.Key is string s && knownSet.Contains(s))
                continue;
            result[kv.Key] = kv.Value;
        }

        return result;
    }

    /// <summary>
    /// Sorts a named list inside the document alphabetically by each entry's
    /// <c>name</c> field.
    /// </summary>
    internal static void SortListByName(Dictionary<object, object> doc, string listKey)
    {
        if (!doc.TryGetValue(listKey, out var raw) || raw is not List<object> list)
            return;

        list.Sort((a, b) =>
        {
            var nameA = GetName(a);
            var nameB = GetName(b);
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Orders fields within each entry's data section (e.g. cluster, user, context)
    /// according to the canonical field order.
    /// </summary>
    private static void OrderEntryFields(
        Dictionary<object, object> doc,
        string listKey,
        string dataKey,
        string[] fieldOrder)
    {
        if (!doc.TryGetValue(listKey, out var raw) || raw is not List<object> list)
            return;

        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not Dictionary<object, object> entry)
                continue;

            if (!entry.TryGetValue(dataKey, out var dataRaw) || dataRaw is not Dictionary<object, object> data)
                continue;

            // Rebuild the entry with ordered fields: name first, then ordered data.
            var orderedData = OrderKeys(data, fieldOrder);
            var orderedEntry = new Dictionary<object, object>();

            // name always first
            if (entry.TryGetValue("name", out var name))
                orderedEntry["name"] = name;

            orderedEntry[dataKey] = orderedData;

            // Append any unknown entry-level keys.
            foreach (var kv in entry)
            {
                if (kv.Key is string s && (s == "name" || s == dataKey))
                    continue;
                orderedEntry[kv.Key] = kv.Value;
            }

            list[i] = orderedEntry;
        }
    }

    private static string GetName(object item)
    {
        if (item is Dictionary<object, object> dict &&
            dict.TryGetValue("name", out var n) &&
            n is string name)
            return name;
        return string.Empty;
    }
}
