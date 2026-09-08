namespace Sanare.Core.Repository;

/// <summary>
/// Raised for script-repository failures. See docs/features/script-repository.md's error codes
/// (<c>SNR-GIT-*</c>): dirty working tree (003), lock timeout (004), branch divergence (005), approval
/// tag conflict (006), backend unavailable (001).
/// </summary>
public sealed class ScriptRepositoryException : Exception
{
    public ScriptRepositoryException(string code, string message, bool retryable = false)
        : base(message)
    {
        Code = code;
        Retryable = retryable;
    }

    /// <summary>The <c>SNR-GIT-*</c> error code.</summary>
    public string Code { get; }

    /// <summary>Whether the caller may retry the same operation (e.g. a lock timeout).</summary>
    public bool Retryable { get; }
}
