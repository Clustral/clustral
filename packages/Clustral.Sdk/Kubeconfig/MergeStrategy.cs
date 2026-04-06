namespace Clustral.Sdk.Kubeconfig;

/// <summary>
/// Controls how <see cref="KubeconfigWriter"/> handles name collisions
/// when writing an entry that already exists.
/// </summary>
public enum MergeStrategy
{
    /// <summary>
    /// Default. Overwrites the existing entry with the new data.
    /// This is the current behavior of <see cref="KubeconfigWriter.WriteClusterEntry(ClustralKubeconfigEntry, bool)"/>.
    /// </summary>
    Replace,

    /// <summary>
    /// Keeps the existing entry unchanged if a name collision occurs.
    /// </summary>
    SkipExisting,

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if a name collision occurs.
    /// </summary>
    FailOnConflict,
}
