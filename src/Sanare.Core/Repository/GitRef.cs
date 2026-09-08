namespace Sanare.Core.Repository;

/// <summary>
/// An explicit git reference used to read a plan at other than the default branch's <c>HEAD</c>. See
/// docs/features/script-repository.md ("Interface", <c>GetPlanAsync</c>'s <c>reference</c> parameter).
/// </summary>
public sealed record GitRef
{
    private GitRef(string value)
    {
        Value = value;
    }

    /// <summary>The raw ref/branch/tag/commit-id text passed to the git backend.</summary>
    public string Value { get; }

    public static GitRef Branch(string name) => new(name);

    public static GitRef Tag(string name) => new(name);

    public static GitRef Commit(string commitId) => new(commitId);

    public override string ToString() => Value;
}
