using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Instrumentation.Extensions;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using System.Linq;
using NzbDrone.Common.TPL;

namespace NzbDrone.Core.IndexerSearch
{
    public interface ISearchForNzb
    {
        List<DownloadDecision> EpisodeSearch(int episodeId);
        List<DownloadDecision> EpisodeSearch(Episode episode);
        List<DownloadDecision> SeasonSearch(int seriesId, int seasonNumber);
    }

    public class NzbSearchService : ISearchForNzb
    {
        private readonly IIndexerFactory _indexerFactory;
        private readonly IFetchFeedFromIndexers _feedFetcher;
        private readonly ISceneMappingService _sceneMapping;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly IMakeDownloadDecision _makeDownloadDecision;
        private readonly Logger _logger;

        public NzbSearchService(IIndexerFactory indexerFactory,
                                IFetchFeedFromIndexers feedFetcher,
                                ISceneMappingService sceneMapping,
                                ISeriesService seriesService,
                                IEpisodeService episodeService,
                                IMakeDownloadDecision makeDownloadDecision,
                                Logger logger)
        {
            _indexerFactory = indexerFactory;
            _feedFetcher = feedFetcher;
            _sceneMapping = sceneMapping;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _makeDownloadDecision = makeDownloadDecision;
            _logger = logger;
        }

        public List<DownloadDecision> EpisodeSearch(int episodeId)
        {
            var episode = _episodeService.GetEpisode(episodeId);

            return EpisodeSearch(episode);
        }

        public List<DownloadDecision> EpisodeSearch(Episode episode)
        {
            var series = _seriesService.GetSeries(episode.SeriesId);

            if (series.SeriesType == SeriesTypes.Daily)
            {
                if (string.IsNullOrWhiteSpace(episode.AirDate))
                {
                    throw new InvalidOperationException("Daily episode is missing AirDate. Try to refresh series info.");
                }

                return SearchDaily(series, episode);
            }
            if (series.SeriesType == SeriesTypes.Anime)
            {
                return SearchAnime(series, episode);
            }

            if (episode.SeasonNumber == 0)
            {
                // search for special episodes in season 0 
                return SearchSpecial(series, new List<Episode> { episode });
            }

            return SearchSingle(series, episode);
        }

        private List<DownloadDecision> SearchSingle(Series series, Episode episode)
        {
            var searchSpec = Get<SingleEpisodeSearchCriteria>(series, new List<Episode>{episode});

            if (series.UseSceneNumbering)
            {
                if (episode.SceneSeasonNumber > 0 && episode.SceneEpisodeNumber > 0)
                {
                    searchSpec.EpisodeNumber = episode.SceneEpisodeNumber;
                    searchSpec.SeasonNumber = episode.SceneSeasonNumber;
                }

                else
                {
                    searchSpec.EpisodeNumber = episode.EpisodeNumber;
                    searchSpec.SeasonNumber = episode.SeasonNumber;
                }
            }
            else
            {
                searchSpec.EpisodeNumber = episode.EpisodeNumber;
                searchSpec.SeasonNumber = episode.SeasonNumber;
            }

            return Dispatch(indexer => _feedFetcher.Fetch(indexer, searchSpec), searchSpec);
        }

        private List<DownloadDecision> SearchDaily(Series series, Episode episode)
        {
            var airDate = DateTime.ParseExact(episode.AirDate, Episode.AIR_DATE_FORMAT, CultureInfo.InvariantCulture);
            var searchSpec = Get<DailyEpisodeSearchCriteria>(series, new List<Episode>{ episode });
            searchSpec.AirDate = airDate;

            return Dispatch(indexer => _feedFetcher.Fetch(indexer, searchSpec), searchSpec);
        }

        private List<DownloadDecision> SearchAnime(Series series, Episode episode)
        {
            var searchSpec = Get<AnimeEpisodeSearchCriteria>(series, new List<Episode> { episode });
            // TODO: Get the scene title from TheXEM
            searchSpec.SceneTitle = series.Title;
            // TODO: Calculate the Absolute Episode Number on the fly (if I have to)
            searchSpec.AbsoluteEpisodeNumber = episode.AbsoluteEpisodeNumber.GetValueOrDefault(0);

            return Dispatch(indexer => _feedFetcher.Fetch(indexer, searchSpec), searchSpec);
        }

        private List<DownloadDecision> SearchSpecial(Series series, List<Episode> episodes)
        {
            var searchSpec = Get<SpecialEpisodeSearchCriteria>(series, episodes);
            // build list of queries for each episode in the form: "<series> <episode-title>"
            searchSpec.EpisodeQueryTitles = episodes.Where(e => !String.IsNullOrWhiteSpace(e.Title))
                                                    .Select(e => searchSpec.QueryTitle + " " + SearchCriteriaBase.GetQueryTitle(e.Title))
                                                    .ToArray();

            return Dispatch(indexer => _feedFetcher.Fetch(indexer, searchSpec), searchSpec);
        }

        public List<DownloadDecision> SeasonSearch(int seriesId, int seasonNumber)
        {
            var series = _seriesService.GetSeries(seriesId);
            var episodes = _episodeService.GetEpisodesBySeason(seriesId, seasonNumber);

            if (seasonNumber == 0)
            {
                // search for special episodes in season 0 
                return SearchSpecial(series, episodes);
            }

            List<DownloadDecision> downloadDecisions = new List<DownloadDecision>();

            if (series.UseSceneNumbering)
            {
                var sceneSeasonGroups = episodes.GroupBy(v => 
                    {
                        if (v.SceneSeasonNumber == 0 && v.SceneEpisodeNumber == 0)
                            return v.SeasonNumber;
                        else
                            return v.SceneSeasonNumber;
                    }).Distinct();

                foreach (var sceneSeasonEpisodes in sceneSeasonGroups)
                {
                    if (sceneSeasonEpisodes.Count() == 1)
                    {
                        var episode = sceneSeasonEpisodes.First();
                        var searchSpec = Get<SingleEpisodeSearchCriteria>(series, sceneSeasonEpisodes.ToList());
                        searchSpec.SeasonNumber = sceneSeasonEpisodes.Key;
                        if (episode.SceneSeasonNumber == 0 && episode.SceneEpisodeNumber == 0)
                            searchSpec.EpisodeNumber = episode.EpisodeNumber;
                        else
                            searchSpec.EpisodeNumber = episode.SceneEpisodeNumber;

                        var decisions = Dispatch(indexer => _feedFetcher.Fetch(indexer, searchSpec), searchSpec);
                        downloadDecisions.AddRange(decisions);
                    }
                    else
                    {
                        var searchSpec = Get<SeasonSearchCriteria>(series, sceneSeasonEpisodes.ToList());
                        searchSpec.SeasonNumber = sceneSeasonEpisodes.Key;

                        var decisions = Dispatch(indexer => _feedFetcher.Fetch(indexer, searchSpec), searchSpec);
                        downloadDecisions.AddRange(decisions);
                    }
                }
            }
            else
            {
                var searchSpec = Get<SeasonSearchCriteria>(series, episodes);
                searchSpec.SeasonNumber = seasonNumber;

                var decisions = Dispatch(indexer => _feedFetcher.Fetch(indexer, searchSpec), searchSpec);
                downloadDecisions.AddRange(decisions);
            }

            return downloadDecisions;
        }

        private TSpec Get<TSpec>(Series series, List<Episode> episodes) where TSpec : SearchCriteriaBase, new()
        {
            var spec = new TSpec();

            spec.Series = series;
            spec.SceneTitle = _sceneMapping.GetSceneName(series.TvdbId);
            spec.Episodes = episodes;

            if (string.IsNullOrWhiteSpace(spec.SceneTitle))
            {
                spec.SceneTitle = series.Title;
            }

            return spec;
        }

        private List<DownloadDecision> Dispatch(Func<IIndexer, IEnumerable<ReleaseInfo>> searchAction, SearchCriteriaBase criteriaBase)
        {
            var indexers = _indexerFactory.GetAvailableProviders().ToList();
            var reports = new List<ReleaseInfo>();

            _logger.ProgressInfo("Searching {0} indexers for {1}", indexers.Count, criteriaBase);

            var taskList = new List<Task>();
            var taskFactory = new TaskFactory(TaskCreationOptions.LongRunning, TaskContinuationOptions.None);

            foreach (var indexer in indexers)
            {
                var indexerLocal = indexer;

                taskList.Add(taskFactory.StartNew(() =>
                {
                    try
                    {
                        var indexerReports = searchAction(indexerLocal);

                        lock (reports)
                        {
                            reports.AddRange(indexerReports);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.ErrorException("Error while searching for " + criteriaBase, e);
                    }
                }).LogExceptions());
            }

            Task.WaitAll(taskList.ToArray());

            _logger.Debug("Total of {0} reports were found for {1} from {2} indexers", reports.Count, criteriaBase, indexers.Count);

            return _makeDownloadDecision.GetSearchDecision(reports, criteriaBase).ToList();
        }
    }
}
