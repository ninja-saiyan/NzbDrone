using System;
using System.Collections.Generic;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public abstract class IndexerBase<TSettings> : IIndexer where TSettings : IProviderConfig, new()
    {
        public Type ConfigContract
        {
            get
            {
                return typeof(TSettings);
            }
        }

        public virtual IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                var config = (IProviderConfig)new TSettings();

                yield return new IndexerDefinition
                {
                    Name = GetType().Name,
                    Enable = config.Validate().IsValid,
                    Implementation = GetType().Name,
                    Settings = config
                };
            }
        }

        public virtual ProviderDefinition Definition { get; set; }

        public abstract DownloadProtocol Protocol { get; }

        public virtual Boolean SupportsFeed { get { return true; } }
        public virtual Int32 SupportedPageSize { get { return 0; } }
        public bool SupportsPaging { get { return SupportedPageSize > 0; } }
        public virtual Boolean SupportsSearching { get { return true; } }

        protected TSettings Settings
        {
            get
            {
                return (TSettings)Definition.Settings;
            }
        }

        public virtual IParseFeed Parser { get; private set; }
        
        public abstract IEnumerable<string> RecentFeed { get; }
        public abstract IEnumerable<string> GetEpisodeSearchUrls(List<String> titles, int tvRageId, int seasonNumber, int episodeNumber);
        public abstract IEnumerable<string> GetDailyEpisodeSearchUrls(List<String> titles, int tvRageId, DateTime date);
        public abstract IEnumerable<string> GetAnimeEpisodeSearchUrls(List<String> titles, int tvRageId, int absoluteEpisodeNumber);
        public abstract IEnumerable<string> GetSeasonSearchUrls(List<String> titles, int tvRageId, int seasonNumber, int offset);
        public abstract IEnumerable<string> GetSearchUrls(string query, int offset);

        public override string ToString()
        {
            return Definition.Name;
        }
    }
}