namespace Groovefin.Core.Models;

/// <summary>One page of a larger result set.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int StartIndex)
{
    /// <summary>Gets a value indicating whether more items exist after this page.</summary>
    public bool HasMore => StartIndex + Items.Count < TotalCount;
}
