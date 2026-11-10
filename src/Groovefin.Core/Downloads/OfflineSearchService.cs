using Groovefin.Core.Api;

namespace Groovefin.Core.Downloads;

/// <summary>
/// <see cref="ISearchService"/> for the search box and page: the server's, but when it cannot be reached the
/// downloaded music is searched (#400), as forgiving of typos as the server's search (#391).
/// </summary>
public sealed class OfflineSearchService(ISearchService server, OfflineAnswers answers) : ISearchService
{
    public Task<SearchResults> SearchAsync(string query, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return answers.AskAsync(
            () => server.SearchAsync(query, limit, cancellationToken),
            index => OfflineQueries.Search(index, query, limit),
            cancellationToken);
    }
}
