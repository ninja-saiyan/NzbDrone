using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public abstract class SearchCriteriaBase
    {
        private static readonly Regex NonWord = new Regex(@"[\W]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BeginningThe = new Regex(@"^the\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public Series Series { get; set; }
        public List<String> SceneTitles { get; set; }
        public List<Episode> Episodes { get; set; }

        public List<String> QueryTitles
        {
            get
            {
                return SceneTitles.Select(GetQueryTitle).ToList();
            }
        }

        public static string GetQueryTitle(string title)
        {
            Ensure.That(title,() => title).IsNotNullOrWhiteSpace();

            var cleanTitle = BeginningThe.Replace(title, String.Empty);

            cleanTitle = cleanTitle
                .Replace("&", "and")
                .Replace("`", "")
                .Replace("'", "");

            cleanTitle = NonWord.Replace(cleanTitle, "+");

            //remove any repeating +s
            cleanTitle = Regex.Replace(cleanTitle, @"\+{2,}", "+");
            return cleanTitle.Trim('+', ' ');
        }
    }
}