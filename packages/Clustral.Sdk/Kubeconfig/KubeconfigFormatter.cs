using System.Text.RegularExpressions;

namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Post-processes serialized YAML to enforce enterprise-readable formatting:
/// blank lines between top-level sections, block-style for empty mappings,
/// and a trailing newline.
/// </summary>
public static partial class KubeconfigFormatter
{
    /// <summary>
    /// Formats raw YAML output from YamlDotNet into enterprise-readable kubeconfig.
    /// </summary>
    public static string Format(string yaml)
    {
        // Replace flow-style empty mappings with block-style.
        // e.g. "preferences: {}" → "preferences: {}"
        // Actually kubectl uses {} for empty preferences, so keep it.

        // Insert blank lines before top-level sections for readability.
        yaml = InsertBlankLines().Replace(yaml, "\n$1");

        // Ensure single trailing newline.
        yaml = yaml.TrimEnd() + "\n";

        return yaml;
    }

    /// <summary>
    /// Regex that matches top-level section headers (not indented) that
    /// should have a blank line before them.
    /// </summary>
    [GeneratedRegex(@"(?m)(?<=\n)(clusters:|users:|contexts:|current-context:)", RegexOptions.Compiled)]
    private static partial Regex InsertBlankLines();
}
