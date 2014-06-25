using System;
using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexer : IProvider
    {
        IParseFeed Parser { get; }
        DownloadProtocol Protocol { get; }
        Int32 SupportedPageSize { get; }
        Boolean SupportsPaging { get; }
        Boolean SupportsSearching { get; }

        IEnumerable<string> RecentFeed { get; }
        IEnumerable<string> GetEpisodeSearchUrls(string seriesTitle, int tvRageId, int seasonNumber, int episodeNumber);
        IEnumerable<string> GetDailyEpisodeSearchUrls(string seriesTitle, int tvRageId, DateTime date);
        IEnumerable<string> GetAnimeEpisodeSearchUrls(string seriesTitle, int tvRageId, int absoluteEpisodeNumber);
        IEnumerable<string> GetSeasonSearchUrls(string seriesTitle, int tvRageId, int seasonNumber, int offset);
        IEnumerable<string> GetSearchUrls(string query, int offset = 0);
    }
}