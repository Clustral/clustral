namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Replaces sensitive values in kubeconfig documents with redacted placeholders.
/// Used for diagnostic output, logging, and error messages.
/// </summary>
public static class KubeconfigRedactor
{
    /// <summary>Placeholder text used in place of sensitive values.</summary>
    public const string RedactedPlaceholder = "***REDACTED***";

    /// <summary>
    /// Sensitive field names within <c>user</c> data sections that must be redacted.
    /// </summary>
    private static readonly HashSet<string> SensitiveUserFields = new(StringComparer.Ordinal)
    {
        "token",
        "password",
        "client-key",
        "client-key-data",
        "client-certificate-data",
    };

    /// <summary>
    /// Returns a deep copy of <paramref name="doc"/> with all sensitive
    /// values replaced by <see cref="RedactedPlaceholder"/>.
    /// </summary>
    public static Dictionary<object, object> RedactDocument(Dictionary<object, object> doc)
    {
        var copy = DeepCopy(doc);

        if (copy.TryGetValue("users", out var usersRaw) && usersRaw is List<object> users)
        {
            foreach (var item in users)
            {
                if (item is not Dictionary<object, object> userEntry)
                    continue;
                if (!userEntry.TryGetValue("user", out var userData) || userData is not Dictionary<object, object> ud)
                    continue;

                RedactFields(ud, SensitiveUserFields);
                RedactExecEnv(ud);
            }
        }

        return copy;
    }

    /// <summary>
    /// Returns a YAML string with sensitive values replaced by <see cref="RedactedPlaceholder"/>.
    /// </summary>
    public static string RedactYaml(string yaml)
    {
        // Line-based redaction for simple cases.
        var lines = yaml.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            foreach (var field in SensitiveUserFields)
            {
                if (trimmed.StartsWith($"{field}:", StringComparison.Ordinal))
                {
                    var colonIdx = lines[i].IndexOf(':', StringComparison.Ordinal);
                    lines[i] = lines[i][..(colonIdx + 1)] + $" {RedactedPlaceholder}";
                    break;
                }
            }
        }
        return string.Join('\n', lines);
    }

    // -------------------------------------------------------------------------

    private static void RedactFields(Dictionary<object, object> data, HashSet<string> sensitiveKeys)
    {
        var keysToRedact = data.Keys
            .OfType<string>()
            .Where(sensitiveKeys.Contains)
            .ToList();

        foreach (var key in keysToRedact)
            data[key] = RedactedPlaceholder;
    }

    private static void RedactExecEnv(Dictionary<object, object> userData)
    {
        if (!userData.TryGetValue("exec", out var execRaw) || execRaw is not Dictionary<object, object> exec)
            return;
        if (!exec.TryGetValue("env", out var envRaw) || envRaw is not List<object> envList)
            return;

        foreach (var item in envList)
        {
            if (item is Dictionary<object, object> envEntry && envEntry.ContainsKey("value"))
                envEntry["value"] = RedactedPlaceholder;
        }
    }

    private static Dictionary<object, object> DeepCopy(Dictionary<object, object> source)
    {
        var copy = new Dictionary<object, object>(source.Count);
        foreach (var kv in source)
        {
            copy[kv.Key] = kv.Value switch
            {
                Dictionary<object, object> dict => DeepCopy(dict),
                List<object> list => DeepCopyList(list),
                _ => kv.Value,
            };
        }
        return copy;
    }

    private static List<object> DeepCopyList(List<object> source)
    {
        var copy = new List<object>(source.Count);
        foreach (var item in source)
        {
            copy.Add(item switch
            {
                Dictionary<object, object> dict => DeepCopy(dict),
                List<object> list => DeepCopyList(list),
                _ => item,
            });
        }
        return copy;
    }
}
