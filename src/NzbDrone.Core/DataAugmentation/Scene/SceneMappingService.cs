using System;
using System.Linq;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using System.Collections.Generic;

namespace NzbDrone.Core.DataAugmentation.Scene
{
    public interface ISceneMappingService
    {
        List<String> GetSceneNames(int tvdbId, IEnumerable<Int32> seasonNumbers);
        Nullable<int> GetTvDbId(string title);
        List<SceneMapping> FindByTvdbid(int tvdbId);
        Nullable<Int32> GetSeasonNumber(string title);
    }

    public class SceneMappingService : ISceneMappingService,
                                       IHandleAsync<ApplicationStartedEvent>,
                                       IExecute<UpdateSceneMappingCommand>
    {
        private readonly ISceneMappingRepository _repository;
        private readonly IEnumerable<ISceneMappingProvider> _sceneMappingProviders;
        private readonly Logger _logger;
        private readonly ICached<SceneMapping> _gettvdbIdCache;
        private readonly ICached<List<SceneMapping>> _findbytvdbIdCache;

        public SceneMappingService(ISceneMappingRepository repository,
                                   ICacheManager cacheManager,
                                   IEnumerable<ISceneMappingProvider> sceneMappingProviders,
                                   Logger logger)
        {
            _repository = repository;
            _sceneMappingProviders = sceneMappingProviders;

            _gettvdbIdCache = cacheManager.GetCache<SceneMapping>(GetType(), "tvdb_id");
            _findbytvdbIdCache = cacheManager.GetCache<List<SceneMapping>>(GetType(), "find_tvdb_id");
            _logger = logger;
        }

        public List<String> GetSceneNames(int tvdbId, IEnumerable<Int32> seasonNumbers)
        {
            var names = _findbytvdbIdCache.Find(tvdbId.ToString());

            if (names == null)
            {
                return new List<String>();
            }

            return FilterNonEnglish(names.Where(s => seasonNumbers.Contains(s.SeasonNumber) ||
                                                     s.SeasonNumber == -1)
                                         .Select(m => m.SearchTerm).Distinct().ToList());
        }

        public Nullable<Int32> GetTvDbId(string title)
        {
            var mapping = _gettvdbIdCache.Find(title.CleanSeriesTitle());

            if (mapping == null)
                return null;

            return mapping.TvdbId;
        }

        public List<SceneMapping> FindByTvdbid(int tvdbId)
        {
            var mappings = _findbytvdbIdCache.Find(tvdbId.ToString());

            if (mappings == null)
            {
                return new List<SceneMapping>();
            }

            return mappings;
        }

        public Nullable<Int32> GetSeasonNumber(string title)
        {
            //TODO: we should be able to override xem aliases with ones from services
            //Example Fairy Tail - Alias is assigned to season 2 (anidb), but we're still using tvdb for everything

            var mapping = _gettvdbIdCache.Find(title.CleanSeriesTitle());

            if (mapping == null)
                return null;

            return mapping.SeasonNumber;
        }

        private void UpdateMappings()
        {
            _logger.Info("Updating Scene mappings");

            foreach (var sceneMappingProvider in _sceneMappingProviders)
            {
                try
                {
                    var mappings = sceneMappingProvider.GetSceneMappings();

                    if (mappings.Any())
                    {
                        _repository.Clear(sceneMappingProvider.GetType().Name);

                        foreach (var sceneMapping in mappings)
                        {
                            sceneMapping.ParseTerm = sceneMapping.Title.CleanSeriesTitle();
                            sceneMapping.Type = sceneMappingProvider.GetType().Name;
                        }

                        _repository.InsertMany(mappings.DistinctBy(s => s.ParseTerm).ToList());
                    }
                    else
                    {
                        _logger.Warn("Received empty list of mapping. will not update.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Failed to Update Scene Mappings:", ex);
                }
            }
            
            RefreshCache();
        }

        private void RefreshCache()
        {
            var mappings = _repository.All().ToList();

            _gettvdbIdCache.Clear();
            _findbytvdbIdCache.Clear();

            foreach (var sceneMapping in mappings)
            {
                _gettvdbIdCache.Set(sceneMapping.ParseTerm.CleanSeriesTitle(), sceneMapping);
            }

            foreach (var sceneMapping in mappings.GroupBy(x => x.TvdbId))
            {
                _findbytvdbIdCache.Set(sceneMapping.Key.ToString(), sceneMapping.ToList());
            }
        }

        private List<String> FilterNonEnglish(List<String> titles)
        {
            return titles.Where(title => title.All(c => c <= 255)).ToList();
        }

        public void HandleAsync(ApplicationStartedEvent message)
        {
            UpdateMappings();
        }

        public void Execute(UpdateSceneMappingCommand message)
        {
            UpdateMappings();
        }
    }
}
