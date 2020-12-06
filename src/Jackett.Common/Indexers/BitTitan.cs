using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Jackett.Common.Models;
using Jackett.Common.Models.IndexerConfig;
using Jackett.Common.Services.Interfaces;
using Jackett.Common.Utils.Clients;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;

namespace Jackett.Common.Indexers
{
    [ExcludeFromCodeCoverage]
    public class BitTitan : BaseWebIndexer
    {
        // Using 
        private new ConfigurationDataAPIKey configData => (ConfigurationDataAPIKey)base.configData;

        private string ApiLink => SiteLink + $"api.php?apiKey={configData.Key.Value}";

        public BitTitan(IIndexerConfigurationService configService, WebClient client, Logger logger, IProtectionService p) :
            base(id: "bit-titan", name: "BiT-TiTAN", description: "BiT-TiTAN is a GERMAN Private Torrent Tracker for MOVIES / TV / GENERAL",
                 link: "https://bit-titan.net/",
                 caps: new TorznabCapabilities
                 {
                     TvSearchParams =
                         new List<TvSearchParam>
                         {
                             TvSearchParam.Q, TvSearchParam.Season, TvSearchParam.Ep
                         },
                     MovieSearchParams = new List<MovieSearchParam> { MovieSearchParam.Q, MovieSearchParam.ImdbId },
                     MusicSearchParams = new List<MusicSearchParam> { MusicSearchParam.Q },
                     BookSearchParams = new List<BookSearchParam> { BookSearchParam.Q }
                 }, configService: configService, client: client, logger: logger, p: p,
                 configData: new ConfigurationDataAPIKey())
        {
            Type = "private";
            Language = "de-DE";
            AddCategoryMappings();
        }

        public override async Task<IndexerConfigurationStatus> ApplyConfiguration(JToken configJson)
        {
            LoadValuesFromJson(configJson);

            if (string.IsNullOrWhiteSpace(configData.Key.Value))
            {
                throw new ExceptionWithConfigData("Please provide an API-Key. You can find the control panel at https://bit-titan.net/api_cp.php " +
                                                  "There you can then generate your API key.", configData);
            }

            var webResult = await RequestWithCookiesAsync(BuildUri(new TorznabQuery()));
            if(webResult.ContentString == "Forbidden: Ungueltiger API-Key")
            {
                throw new ExceptionWithConfigData("You provided an invalid API-Key. You can find the control panel at https://bit-titan.net/api_cp.php " +
                                                  "There you can then generate your API key.", configData);
            }

            var responseDto = JsonConvert.DeserializeObject<BitTitanResponse>(webResult.ContentString);
            if (!responseDto.results.Any())
            {
                throw new ExceptionWithConfigData(
                    $"Failed to retrieve any results. Check this response: {webResult.ContentString}", configData);
            }

            SaveConfig();
            return IndexerConfigurationStatus.Completed;
        }

        protected override async Task<IEnumerable<ReleaseInfo>> PerformQuery(TorznabQuery query)
        {
            var webResult = await RequestWithCookiesAsync(BuildUri(query));
            var response = JsonConvert.DeserializeObject<BitTitanResponse>(webResult.ContentString);

            var releaseInfos = new List<ReleaseInfo>();
            releaseInfos.AddRange(ParseReleaseInfos(response));

            // Results are split up into pages.
            // So if multiple pages are available we have to request each of them.
            for (var pageNum = 2; pageNum <= response.pages; pageNum++) // starting with 2 as we have already requested the first page.
            {
                webResult = await RequestWithCookiesAsync(BuildUri(query, pageNum));
                response = JsonConvert.DeserializeObject<BitTitanResponse>(webResult.ContentString);
                releaseInfos.AddRange(ParseReleaseInfos(response));
            }

            return releaseInfos;
        }

        private static IEnumerable<ReleaseInfo> ParseReleaseInfos(BitTitanResponse response)
        {
            foreach (var result in response.results)
            {
                yield return new ReleaseInfo()
                {
                    Title = result.name,
                    Category = new List<int> {result.category},
                    Link = new Uri(result.download),
                    Size = result.size,
                    Grabs = result.snatchers,
                    Seeders = result.seeds,
                    Peers = result.seeds + result.leechers,
                    UploadVolumeFactor = result.uploadFactor,
                    DownloadVolumeFactor = result.downloadFactor,
                    PublishDate = result.added
                };
            }
        }

        private string BuildUri(TorznabQuery query, int? page = null)
        {
            var uriBuilder = new StringBuilder(ApiLink);
            uriBuilder.Append($"&search={Uri.EscapeUriString(query.GetQueryString())}");
            uriBuilder.Append("&downloadLink=1"); // Output download link.
            uriBuilder.Append("&timeFormat=1"); // Unix-Timestamp
            if (query.Categories.Any())
            {
                uriBuilder.Append($"categories={string.Join(",", query.Categories)}");
            }

            // Not using limit for now. API does not return anything if limit is larger than available results.
            // uriBuilder.Append($"&limit={ResolveLimit(query.Limit)}");

            if (page != null)
            {
                uriBuilder.Append($"&page={page}");
            }

            return uriBuilder.ToString();
        }


        /// <summary>
        /// Unused at the moment.
        /// Not using limit for now. API does not return anything if limit is larger than available results.
        /// </summary>
        private int ResolveLimit(int queryLimit)
        {
            /*
               limit ->
               1 = get 25 items (DEFAULT VALUE)
               2 = get 50 items
               3 = get 75 items
               4 = get 100 items
             */
            if (queryLimit == 0)
            {
                return 4;
            }
            return Math.Min(1, Math.Max(4, queryLimit / 25));
        }

#pragma warning disable IDE1006 // Naming Styles

        /// <summary>
        /// API Response as DTO for simple parsing.
        /// </summary>
        private class BitTitanResponse
        {
            public int pages { get; set; }
            public Result[] results { get; set; }
        }

        public class Result
        {
            public string id { get; set; }
            public string name { get; set; }
            public long size { get; set; }
            public int category { get; set; }
            public int seeds { get; set; }
            public int leechers { get; set; }
            public int snatchers { get; set; }
            public int downloadFactor { get; set; }
            public int uploadFactor { get; set; }
            public string download { get; set; }
            public DateTime added { get; set; }
        }

#pragma warning restore IDE1006 // Naming Styles

        private void AddCategoryMappings()
        {
            AddCategoryMapping(1010, TorznabCatType.MoviesUHD);
            AddCategoryMapping(1020, TorznabCatType.MoviesHD);
            AddCategoryMapping(1030, TorznabCatType.MoviesHD);
            AddCategoryMapping(1040, TorznabCatType.MoviesHD);
            AddCategoryMapping(1050, TorznabCatType.MoviesHD);
            AddCategoryMapping(1060, TorznabCatType.MoviesSD);
            AddCategoryMapping(1070, TorznabCatType.Movies3D);
            AddCategoryMapping(1080, TorznabCatType.MoviesDVD);
            AddCategoryMapping(1090, TorznabCatType.MoviesBluRay);
            AddCategoryMapping(1100, TorznabCatType.MoviesDVD);
            AddCategoryMapping(1110, TorznabCatType.MoviesForeign);
            AddCategoryMapping(1120, TorznabCatType.MoviesHD);
            AddCategoryMapping(1130, TorznabCatType.MoviesSD);
            AddCategoryMapping(2010, TorznabCatType.TVUHD);
            AddCategoryMapping(2020, TorznabCatType.TVHD);
            AddCategoryMapping(2030, TorznabCatType.TVHD);
            AddCategoryMapping(2040, TorznabCatType.TVHD);
            AddCategoryMapping(2050, TorznabCatType.TVHD);
            AddCategoryMapping(2060, TorznabCatType.TVSD);
            AddCategoryMapping(2070, TorznabCatType.TVHD);
            AddCategoryMapping(2080, TorznabCatType.TVSD);
            AddCategoryMapping(2090, TorznabCatType.TVForeign);
            AddCategoryMapping(3010, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3020, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3030, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3040, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3050, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3060, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3070, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3080, TorznabCatType.TVDocumentary);
            AddCategoryMapping(3090, TorznabCatType.TVDocumentary);
            AddCategoryMapping(4010, TorznabCatType.TVSport);
            AddCategoryMapping(4020, TorznabCatType.TVSport);
            AddCategoryMapping(4030, TorznabCatType.TVSport);
            AddCategoryMapping(4040, TorznabCatType.TVSport);
            AddCategoryMapping(4050, TorznabCatType.TVSport);
            AddCategoryMapping(4060, TorznabCatType.TVSport);
            AddCategoryMapping(5010, TorznabCatType.XXX);
            AddCategoryMapping(5020, TorznabCatType.XXX);
            AddCategoryMapping(5030, TorznabCatType.XXX);
            AddCategoryMapping(5040, TorznabCatType.XXX);
            AddCategoryMapping(5050, TorznabCatType.XXX);
            AddCategoryMapping(5060, TorznabCatType.XXX);
            AddCategoryMapping(5070, TorznabCatType.XXX);
            AddCategoryMapping(5080, TorznabCatType.XXX);
            AddCategoryMapping(5090, TorznabCatType.XXX);
            AddCategoryMapping(6010, TorznabCatType.PCGames);
            AddCategoryMapping(6020, TorznabCatType.Console);
            AddCategoryMapping(6030, TorznabCatType.PCMac);
            AddCategoryMapping(6040, TorznabCatType.PCMobileAndroid);
            AddCategoryMapping(6050, TorznabCatType.ConsoleXBox);
            AddCategoryMapping(6060, TorznabCatType.ConsolePSP);
            AddCategoryMapping(6070, TorznabCatType.ConsoleNDS);
            AddCategoryMapping(6080, TorznabCatType.Console);
            AddCategoryMapping(7010, TorznabCatType.PC0day);
            AddCategoryMapping(7020, TorznabCatType.PC);
            AddCategoryMapping(7030, TorznabCatType.PCMac);
            AddCategoryMapping(7040, TorznabCatType.PCMobileAndroid);
            AddCategoryMapping(8010, TorznabCatType.AudioMP3);
            AddCategoryMapping(8020, TorznabCatType.AudioMP3);
            AddCategoryMapping(8030, TorznabCatType.AudioMP3);
            AddCategoryMapping(8040, TorznabCatType.AudioMP3);
            AddCategoryMapping(8050, TorznabCatType.AudioLossless);
            AddCategoryMapping(8060, TorznabCatType.AudioLossless);
            AddCategoryMapping(8070, TorznabCatType.AudioLossless);
            AddCategoryMapping(8080, TorznabCatType.AudioLossless);
            AddCategoryMapping(8090, TorznabCatType.AudioVideo);
            AddCategoryMapping(9010, TorznabCatType.AudioAudiobook);
            AddCategoryMapping(9020, TorznabCatType.BooksEBook);
            AddCategoryMapping(9030, TorznabCatType.Books);
            AddCategoryMapping(9040, TorznabCatType.Books);
            AddCategoryMapping(9050, TorznabCatType.TV);
            AddCategoryMapping(9060, TorznabCatType.TVAnime);
            AddCategoryMapping(9070, TorznabCatType.TVAnime);
            AddCategoryMapping(9080, TorznabCatType.TVAnime);
            AddCategoryMapping(9999, TorznabCatType.Other);
        }
    }
}
