using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Dsk.Weather.Models;

namespace Dsk.Weather.Services
{
    public enum AlertSourceStatus
    {
        Live,
        StaleCache,
        Fallback,
        Unsupported
    }

    public sealed record OfficialAlertFetchResult(
        bool ProviderAvailable,
        WeatherAlertInfo? Alert,
        AlertSourceStatus SourceStatus = AlertSourceStatus.Unsupported,
        DateTimeOffset? LastSuccessfulFetchUtc = null,
        string ProviderLabel = "");

    public sealed class OfficialAlertService
    {
        private const string DpcPageDataUrl = "https://mappe.protezionecivile.gov.it/page-data/it/mappe-rischi/bollettino-di-criticita/page-data.json";
        private const string DpcGithubCommitsUrl = "https://api.github.com/repos/pcm-dpc/DPC-Bollettini-Criticita-Idrogeologica-Idraulica/commits?path=files/topojson&per_page=1";
        private const string DpcTopoJsonTemplate = "https://raw.githubusercontent.com/pcm-dpc/DPC-Bollettini-Criticita-Idrogeologica-Idraulica/master/files/topojson/{0}_today.json";
        private const string IpmaWarningsUrl = "https://api.ipma.pt/open-data/forecast/warnings/warnings_www.json";

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private static readonly TimeSpan FeedCacheTtl = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan StaleGraceTtl = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan DpcIndexTtl = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan DpcBulletinTtl = TimeSpan.FromMinutes(60);
        private static readonly TimeSpan DpcStaleGraceTtl = TimeSpan.FromMinutes(240);
        private static readonly object CacheLock = new();

        private static readonly Dictionary<string, GeoRegionInfo> GeoCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, CachedPayload> PayloadCache = new(StringComparer.Ordinal);

        // MeteoAlarm mantenuto solo nei Paesi europei con buona granularità provinciale o poligonale
        private static readonly Dictionary<string, string> CountryToMeteoAlarmSlug = new(StringComparer.OrdinalIgnoreCase)
        {
            ["nl"] = "netherlands",
            ["fr"] = "france",
            ["es"] = "spain",
            ["be"] = "belgium",
            ["ch"] = "switzerland",
            ["at"] = "austria",
            ["lu"] = "luxembourg",
            ["gb"] = "united-kingdom",
            ["ie"] = "ireland",
            ["dk"] = "denmark",
            ["se"] = "sweden",
            ["fi"] = "finland",
            ["pl"] = "poland",
            ["cz"] = "czechia"
        };

        private static readonly Dictionary<string, string[]> PortugueseDistrictToIpmaArea = new(StringComparer.OrdinalIgnoreCase)
        {
            ["aveiro"] = new[] { "AVR" },
            ["beja"] = new[] { "BJA" },
            ["braga"] = new[] { "BRG" },
            ["braganca"] = new[] { "BGC" },
            ["castelo branco"] = new[] { "CBL" },
            ["coimbra"] = new[] { "CBR" },
            ["evora"] = new[] { "EVR" },
            ["faro"] = new[] { "FAR" },
            ["guarda"] = new[] { "GDA" },
            ["leiria"] = new[] { "LRA" },
            ["lisboa"] = new[] { "LSB" },
            ["lisbon"] = new[] { "LSB" },
            ["portalegre"] = new[] { "PTG" },
            ["porto"] = new[] { "PTO" },
            ["santarem"] = new[] { "STM" },
            ["setubal"] = new[] { "STB" },
            ["viana do castelo"] = new[] { "VCT" },
            ["vila real"] = new[] { "VRL" },
            ["viseu"] = new[] { "VIS" },
            ["madeira"] = new[] { "MCN", "MCS", "MRM" },
            ["acores"] = new[] { "AOR", "ACE", "AOC" },
            ["azores"] = new[] { "AOR", "ACE", "AOC" }
        };

        private static readonly Dictionary<string, string> ItalianIsoToEmma = new(StringComparer.OrdinalIgnoreCase)
        {
            ["IT-78"] = "IT001",
            ["IT-32"] = "IT002",
            ["IT-25"] = "IT003",
            ["IT-23"] = "IT004",
            ["IT-21"] = "IT005",
            ["IT-34"] = "IT006",
            ["IT-42"] = "IT007",
            ["IT-45"] = "IT008",
            ["IT-52"] = "IT009",
            ["IT-55"] = "IT010",
            ["IT-57"] = "IT011",
            ["IT-62"] = "IT012",
            ["IT-65"] = "IT013",
            ["IT-67"] = "IT014",
            ["IT-75"] = "IT015",
            ["IT-72"] = "IT016",
            ["IT-77"] = "IT017",
            ["IT-82"] = "IT018",
            ["IT-88"] = "IT019",
            ["IT-36"] = "IT020"
        };

        private static readonly Dictionary<string, string[]> DutchIsoToAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["NL-FR"] = new[] { "friesland", "fryslan" },
            ["NL-GR"] = new[] { "groningen" },
            ["NL-DR"] = new[] { "drenthe" },
            ["NL-OV"] = new[] { "overijssel" },
            ["NL-FL"] = new[] { "flevoland" },
            ["NL-GE"] = new[] { "gelderland" },
            ["NL-UT"] = new[] { "utrecht" },
            ["NL-NH"] = new[] { "noord-holland", "noord holland" },
            ["NL-ZH"] = new[] { "zuid-holland", "zuid holland" },
            ["NL-ZE"] = new[] { "zeeland" },
            ["NL-NB"] = new[] { "noord-brabant", "noord brabant" },
            ["NL-LI"] = new[] { "limburg" }
        };

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Dsk.Weather/1.4.2 (https://github.com/mauroSeu/Dsk.Weather)");
            return client;
        }

        internal static bool UsesMeteoAlarm(string countryCode)
        {
            return !string.IsNullOrWhiteSpace(countryCode) &&
                CountryToMeteoAlarmSlug.ContainsKey(countryCode.Trim());
        }

        internal static void ClearCachesForTests()
        {
            lock (CacheLock)
            {
                GeoCache.Clear();
                PayloadCache.Clear();
            }
        }

        public async Task<OfficialAlertFetchResult> FetchOfficialAlertAsync(
            double lat,
            double lon,
            string lang = "it",
            CancellationToken ct = default)
        {
            try
            {
                GeoRegionInfo? region = await ResolveRegionAsync(lat, lon, ct).ConfigureAwait(false);
                if (region is null || string.IsNullOrWhiteSpace(region.CountryCode))
                {
                    return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Unsupported, null, string.Empty);
                }

                string cc = region.CountryCode.ToLowerInvariant();
                DateTime localNow = DateTime.Now;
                DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

                if (cc == "it")
                {
                    return await FetchDpcItalyAlertAsync(region, localNow, nowUtc, lang, ct).ConfigureAwait(false);
                }

                if (cc == "de")
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "de:{0:0.##},{1:0.##}", lat, lon);
                    string url = string.Format(
                        CultureInfo.InvariantCulture,
                        "https://api.brightsky.dev/alerts?lat={0:0.####}&lon={1:0.####}",
                        lat,
                        lon);
                    PayloadFetchOutcome outcome = await FetchPayloadWithCacheAsync(key, url, nowUtc, DownloadStringAsync, ct).ConfigureAwait(false);
                    if (outcome.Body is null)
                    {
                        return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, outcome.LastSuccessfulFetchUtc, "DWD");
                    }

                    WeatherAlertInfo? dwdAlert = ParseBrightSkyJson(outcome.Body, localNow, lang);
                    return new OfficialAlertFetchResult(true, dwdAlert, outcome.Status, outcome.LastSuccessfulFetchUtc, "DWD");
                }

                if (cc == "us")
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "us:{0:0.##},{1:0.##}", lat, lon);
                    string url = string.Format(
                        CultureInfo.InvariantCulture,
                        "https://api.weather.gov/alerts/active?point={0:0.####},{1:0.####}",
                        lat,
                        lon);
                    PayloadFetchOutcome outcome = await FetchPayloadWithCacheAsync(key, url, nowUtc, DownloadStringAsync, ct).ConfigureAwait(false);
                    if (outcome.Body is null)
                    {
                        return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, outcome.LastSuccessfulFetchUtc, "NOAA NWS");
                    }

                    WeatherAlertInfo? nwsAlert = ParseNwsJson(outcome.Body, localNow, lang);
                    return new OfficialAlertFetchResult(true, nwsAlert, outcome.Status, outcome.LastSuccessfulFetchUtc, "NOAA NWS");
                }

                if (cc == "pt")
                {
                    PayloadFetchOutcome outcome = await FetchPayloadWithCacheAsync("pt:ipma", IpmaWarningsUrl, nowUtc, DownloadStringAsync, ct).ConfigureAwait(false);
                    if (outcome.Body is null)
                    {
                        return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, outcome.LastSuccessfulFetchUtc, "IPMA");
                    }

                    WeatherAlertInfo? ipmaAlert = ParseIpmaJson(outcome.Body, region, localNow, lang);
                    return new OfficialAlertFetchResult(true, ipmaAlert, outcome.Status, outcome.LastSuccessfulFetchUtc, "IPMA");
                }

                if (cc == "no")
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "no:{0:0.##},{1:0.##}", lat, lon);
                    string url = string.Format(
                        CultureInfo.InvariantCulture,
                        "https://api.met.no/weatherapi/metalerts/2.0/current.json?lat={0:0.####}&lon={1:0.####}",
                        lat,
                        lon);
                    PayloadFetchOutcome outcome = await FetchPayloadWithCacheAsync(key, url, nowUtc, DownloadStringAsync, ct).ConfigureAwait(false);
                    if (outcome.Body is null)
                    {
                        return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, outcome.LastSuccessfulFetchUtc, "MET Norway");
                    }

                    WeatherAlertInfo? noAlert = ParseMetNorwayJson(outcome.Body, localNow, lang);
                    return new OfficialAlertFetchResult(true, noAlert, outcome.Status, outcome.LastSuccessfulFetchUtc, "MET Norway");
                }

                if (CountryToMeteoAlarmSlug.TryGetValue(cc, out string? slug))
                {
                    string feedKey = $"ma:{slug}";
                    string feedUrl = $"https://feeds.meteoalarm.org/feeds/meteoalarm-legacy-atom-{slug}";
                    PayloadFetchOutcome outcome = await FetchPayloadWithCacheAsync(feedKey, feedUrl, nowUtc, DownloadStringAsync, ct).ConfigureAwait(false);
                    if (outcome.Body is null)
                    {
                        return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, outcome.LastSuccessfulFetchUtc, "MeteoAlarm");
                    }

                    List<MeteoAlarmMatch> candidates = ParseMeteoAlarmCandidates(outcome.Body, region, localNow, lat, lon);
                    if (candidates.Count == 0)
                    {
                        return new OfficialAlertFetchResult(true, null, outcome.Status, outcome.LastSuccessfulFetchUtc, "MeteoAlarm");
                    }

                    int limit = Math.Min(candidates.Count, 4);
                    AlertSourceStatus effectiveStatus = outcome.Status;
                    DateTimeOffset? effectiveFetchUtc = outcome.LastSuccessfulFetchUtc;

                    for (int i = 0; i < limit; i++)
                    {
                        MeteoAlarmMatch candidate = candidates[i];
                        string? capXml = null;
                        if (!string.IsNullOrWhiteSpace(candidate.CapUrl))
                        {
                            PayloadFetchOutcome capOutcome = await FetchPayloadWithCacheAsync(
                                $"cap:{candidate.CapUrl}",
                                candidate.CapUrl,
                                DateTimeOffset.UtcNow,
                                DownloadStringAsync,
                                ct).ConfigureAwait(false);
                            capXml = capOutcome.Body;
                            if (capOutcome.Status == AlertSourceStatus.StaleCache && effectiveStatus == AlertSourceStatus.Live)
                            {
                                effectiveStatus = AlertSourceStatus.StaleCache;
                            }
                        }

                        WeatherAlertInfo? info = TryBuildAlertFromCap(capXml, candidate, localNow, lang, lat, lon);
                        if (info is not null)
                        {
                            return new OfficialAlertFetchResult(true, info, effectiveStatus, effectiveFetchUtc, "MeteoAlarm");
                        }
                    }

                    return new OfficialAlertFetchResult(true, null, effectiveStatus, effectiveFetchUtc, "MeteoAlarm");
                }

                return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Unsupported, null, string.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, null, string.Empty);
            }
        }

        private static async Task<OfficialAlertFetchResult> FetchDpcItalyAlertAsync(
            GeoRegionInfo region,
            DateTime localNow,
            DateTimeOffset nowUtc,
            string lang,
            CancellationToken ct)
        {
            PayloadFetchOutcome indexOutcome = await FetchPayloadWithCacheAsync(
                "dpc:index",
                DpcPageDataUrl,
                nowUtc,
                DownloadStringAsync,
                ct,
                DpcIndexTtl,
                DpcStaleGraceTtl).ConfigureAwait(false);

            string? bulletinId = indexOutcome.Body is not null
                ? ExtractDpcBulletinId(indexOutcome.Body)
                : null;
            AlertSourceStatus effectiveStatus = indexOutcome.Status;
            DateTimeOffset? lastSuccess = indexOutcome.LastSuccessfulFetchUtc;

            if (string.IsNullOrWhiteSpace(bulletinId))
            {
                PayloadFetchOutcome ghCommits = await FetchPayloadWithCacheAsync(
                    "dpc:ghcommits",
                    DpcGithubCommitsUrl,
                    nowUtc,
                    DownloadStringAsync,
                    ct,
                    DpcIndexTtl,
                    DpcStaleGraceTtl).ConfigureAwait(false);

                if (ghCommits.Body is not null)
                {
                    bulletinId = ExtractDpcBulletinId(ghCommits.Body);
                    if (string.IsNullOrWhiteSpace(bulletinId))
                    {
                        string? commitSha = ExtractFirstGithubCommitSha(ghCommits.Body);
                        if (!string.IsNullOrWhiteSpace(commitSha))
                        {
                            string commitUrl = $"https://api.github.com/repos/pcm-dpc/DPC-Bollettini-Criticita-Idrogeologica-Idraulica/commits/{commitSha}";
                            PayloadFetchOutcome commitDetail = await FetchPayloadWithCacheAsync(
                                $"dpc:ghcommit:{commitSha}",
                                commitUrl,
                                nowUtc,
                                DownloadStringAsync,
                                ct,
                                DpcBulletinTtl,
                                DpcStaleGraceTtl).ConfigureAwait(false);
                            if (commitDetail.Body is not null)
                            {
                                bulletinId = ExtractDpcBulletinId(commitDetail.Body);
                                effectiveStatus = commitDetail.Status;
                                lastSuccess = commitDetail.LastSuccessfulFetchUtc;
                            }
                        }
                    }
                    else
                    {
                        effectiveStatus = ghCommits.Status;
                        lastSuccess = ghCommits.LastSuccessfulFetchUtc;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bulletinId))
            {
                return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, lastSuccess, "Protezione Civile");
            }

            string topoUrl = string.Format(CultureInfo.InvariantCulture, DpcTopoJsonTemplate, bulletinId);
            PayloadFetchOutcome topoOutcome = await FetchPayloadWithCacheAsync(
                $"dpc:today:{bulletinId}",
                topoUrl,
                nowUtc,
                DownloadStringAsync,
                ct,
                DpcBulletinTtl,
                DpcStaleGraceTtl).ConfigureAwait(false);

            if (topoOutcome.Body is null)
            {
                return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, topoOutcome.LastSuccessfulFetchUtc ?? lastSuccess, "Protezione Civile");
            }

            if (topoOutcome.Status == AlertSourceStatus.StaleCache || effectiveStatus == AlertSourceStatus.StaleCache)
            {
                effectiveStatus = AlertSourceStatus.StaleCache;
            }
            else
            {
                effectiveStatus = topoOutcome.Status;
            }

            lastSuccess = topoOutcome.LastSuccessfulFetchUtc ?? lastSuccess;

            (bool zoneFound, WeatherAlertInfo? alert) = ParseDpcTopoJson(topoOutcome.Body, region, localNow, lang);
            if (!zoneFound)
            {
                return new OfficialAlertFetchResult(false, null, AlertSourceStatus.Fallback, lastSuccess, "Protezione Civile");
            }

            return new OfficialAlertFetchResult(true, alert, effectiveStatus, lastSuccess, "Protezione Civile");
        }

        private static async Task<GeoRegionInfo?> ResolveRegionAsync(double lat, double lon, CancellationToken ct)
        {
            string coordKey = string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##}", lat, lon);
            lock (CacheLock)
            {
                if (GeoCache.TryGetValue(coordKey, out GeoRegionInfo? cached))
                {
                    return cached;
                }
            }

            string url = string.Format(
                CultureInfo.InvariantCulture,
                "https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={0:0.####}&lon={1:0.####}&zoom=10&addressdetails=1",
                lat,
                lon);

            using HttpResponseMessage resp = await SharedHttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("address", out JsonElement addr))
            {
                return null;
            }

            var info = new GeoRegionInfo(
                CountryCode: GetJsonString(addr, "country_code"),
                IsoLvl4: GetJsonString(addr, "ISO3166-2-lvl4"),
                IsoLvl6: GetJsonString(addr, "ISO3166-2-lvl6"),
                State: GetJsonString(addr, "state"),
                County: GetJsonString(addr, "county"),
                Province: GetJsonString(addr, "province"),
                Municipality: GetJsonString(addr, "municipality"),
                City: GetFirstNonEmpty(
                    GetJsonString(addr, "city"),
                    GetJsonString(addr, "town"),
                    GetJsonString(addr, "village"),
                    GetJsonString(addr, "hamlet")));

            lock (CacheLock)
            {
                GeoCache[coordKey] = info;
            }

            return info;
        }

        private static async Task<string?> DownloadStringAsync(string url, CancellationToken ct)
        {
            using HttpResponseMessage resp = await SharedHttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        internal static async Task<PayloadFetchOutcome> FetchPayloadWithCacheAsync(
            string cacheKey,
            string url,
            DateTimeOffset nowUtc,
            Func<string, CancellationToken, Task<string?>> fetcher,
            CancellationToken ct,
            TimeSpan? cacheTtl = null,
            TimeSpan? staleGraceTtl = null)
        {
            TimeSpan effectiveTtl = cacheTtl ?? FeedCacheTtl;
            TimeSpan effectiveGrace = staleGraceTtl ?? StaleGraceTtl;

            CachedPayload? cachedEntry;
            lock (CacheLock)
            {
                if (PayloadCache.TryGetValue(cacheKey, out cachedEntry) &&
                    (nowUtc - cachedEntry.FetchedUtc) < effectiveTtl)
                {
                    return new PayloadFetchOutcome(cachedEntry.Body, AlertSourceStatus.Live, cachedEntry.FetchedUtc);
                }
            }

            string? freshBody = null;
            try
            {
                freshBody = await fetcher(url, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                freshBody = null;
            }

            if (freshBody is not null)
            {
                lock (CacheLock)
                {
                    PayloadCache[cacheKey] = new CachedPayload(nowUtc, freshBody);
                }

                return new PayloadFetchOutcome(freshBody, AlertSourceStatus.Live, nowUtc);
            }

            lock (CacheLock)
            {
                PayloadCache.TryGetValue(cacheKey, out cachedEntry);
            }

            if (cachedEntry is not null && (nowUtc - cachedEntry.FetchedUtc) <= effectiveGrace)
            {
                return new PayloadFetchOutcome(cachedEntry.Body, AlertSourceStatus.StaleCache, cachedEntry.FetchedUtc);
            }

            return new PayloadFetchOutcome(null, AlertSourceStatus.Fallback, cachedEntry?.FetchedUtc);
        }

        internal static MeteoAlarmMatch? ParseMeteoAlarmAtom(
            string atomXml,
            GeoRegionInfo region,
            DateTime localNow,
            double? lat = null,
            double? lon = null)
        {
            List<MeteoAlarmMatch> candidates = ParseMeteoAlarmCandidates(atomXml, region, localNow, lat, lon);
            return candidates.Count > 0 ? candidates[0] : null;
        }

        internal static List<MeteoAlarmMatch> ParseMeteoAlarmCandidates(
            string atomXml,
            GeoRegionInfo region,
            DateTime localNow,
            double? lat = null,
            double? lon = null)
        {
            var results = new List<MeteoAlarmMatch>();
            if (string.IsNullOrWhiteSpace(atomXml) || region is null)
            {
                return results;
            }

            XDocument doc;
            try
            {
                doc = LoadXmlSafe(atomXml);
            }
            catch
            {
                return results;
            }

            DateTimeOffset nowUtc = ToLocalDateTimeOffset(localNow).ToUniversalTime();
            DateTimeOffset maxLookaheadUtc = nowUtc.AddHours(18);
            string? expectedEmma = ResolveExpectedEmmaId(region);
            List<string> candidateAreas = BuildCandidateAreaTokens(region);

            var supersededIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entries = new List<XElement>();

            foreach (XElement entry in doc.Descendants())
            {
                if (!string.Equals(entry.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                entries.Add(entry);

                string status = GetChildText(entry, "status");
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string msgType = GetFirstNonEmpty(
                    GetChildText(entry, "message_type"),
                    GetChildText(entry, "msgType"));
                if (string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(msgType, "Update", StringComparison.OrdinalIgnoreCase))
                {
                    string rawRefs = GetChildText(entry, "references");
                    foreach (string refId in ExtractReferencedIdentifiers(rawRefs))
                    {
                        supersededIds.Add(refId);
                    }
                }
            }

            var scored = new List<(MeteoAlarmMatch Match, int Rank, bool IsActiveNow, int CertRank, DateTimeOffset SortOnset)>();

            foreach (XElement entry in entries)
            {
                string status = GetChildText(entry, "status");
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string msgType = GetFirstNonEmpty(
                    GetChildText(entry, "message_type"),
                    GetChildText(entry, "msgType"));
                if (string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string entryId = GetFirstNonEmpty(
                    GetChildText(entry, "identifier"),
                    GetChildText(entry, "id"));
                if (!string.IsNullOrWhiteSpace(entryId) && supersededIds.Contains(entryId))
                {
                    continue;
                }

                DateTimeOffset? expires = TryParseOffset(GetChildText(entry, "expires"));
                if (expires.HasValue && expires.Value.ToUniversalTime() <= nowUtc)
                {
                    continue;
                }

                DateTimeOffset? onset = TryParseOffset(GetChildText(entry, "onset"))
                    ?? TryParseOffset(GetChildText(entry, "effective"));
                if (onset.HasValue && onset.Value.ToUniversalTime() > maxLookaheadUtc)
                {
                    continue;
                }

                string certaintyRaw = GetChildText(entry, "certainty");
                int certRank = CertaintyRank(certaintyRaw);
                if (certRank < 0)
                {
                    continue;
                }

                string areaDesc = GetChildText(entry, "areaDesc");
                string geocode = string.Empty;
                foreach (XElement geo in entry.Elements())
                {
                    if (string.Equals(geo.Name.LocalName, "geocode", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = GetChildText(geo, "value");
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            geocode = val.Trim();
                            break;
                        }
                    }
                }

                bool regionMatched;
                if (lat.HasValue && lon.HasValue)
                {
                    GeometryMatchState geoState = EvaluateElementGeometry(entry, lat.Value, lon.Value);
                    if (geoState == GeometryMatchState.Outside)
                    {
                        continue;
                    }

                    regionMatched = geoState == GeometryMatchState.Inside ||
                        IsRegionMatch(expectedEmma, candidateAreas, geocode, areaDesc);
                }
                else
                {
                    regionMatched = IsRegionMatch(expectedEmma, candidateAreas, geocode, areaDesc);
                }

                if (!regionMatched)
                {
                    continue;
                }

                string ev = GetChildText(entry, "event");
                string sev = GetChildText(entry, "severity");
                WeatherAlertLevel level = MapSeverityAndEvent(sev, ev);
                if (level == WeatherAlertLevel.None)
                {
                    continue;
                }

                string? capUrl = null;
                foreach (XElement link in entry.Elements())
                {
                    if (!string.Equals(link.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? typeAttr = link.Attribute("type")?.Value;
                    if (string.Equals(typeAttr, "application/cap+xml", StringComparison.OrdinalIgnoreCase))
                    {
                        capUrl = link.Attribute("href")?.Value;
                        break;
                    }
                }

                int rank = LevelRank(level);
                bool isActiveNow = !onset.HasValue || onset.Value.ToUniversalTime() <= nowUtc;
                DateTimeOffset sortOnset = onset?.ToUniversalTime() ?? nowUtc;
                string normalizedCertainty = NormalizeCertainty(certaintyRaw);

                var match = new MeteoAlarmMatch(level, ev, areaDesc, geocode, capUrl, onset, expires, normalizedCertainty, entryId);
                scored.Add((match, rank, isActiveNow, certRank, sortOnset));
            }

            scored.Sort(static (a, b) =>
            {
                int cmp = b.Rank.CompareTo(a.Rank);
                if (cmp != 0) return cmp;
                cmp = b.IsActiveNow.CompareTo(a.IsActiveNow);
                if (cmp != 0) return cmp;
                cmp = b.CertRank.CompareTo(a.CertRank);
                if (cmp != 0) return cmp;
                return a.SortOnset.CompareTo(b.SortOnset);
            });

            foreach (var item in scored)
            {
                results.Add(item.Match);
            }

            return results;
        }

        internal static WeatherAlertInfo BuildAlertFromCap(
            string? capXml,
            MeteoAlarmMatch match,
            DateTime localNow,
            string lang)
        {
            return TryBuildAlertFromCap(capXml, match, localNow, lang, null, null) ?? WeatherAlertInfo.None;
        }

        internal static WeatherAlertInfo? TryBuildAlertFromCap(
            string? capXml,
            MeteoAlarmMatch match,
            DateTime localNow,
            string lang,
            double? lat = null,
            double? lon = null)
        {
            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            string senderName = string.Empty;
            string rawDesc = string.Empty;
            string awarenessType = string.Empty;
            string eventLabel = match.EventName;
            string areaDesc = match.AreaDesc;
            string certainty = match.Certainty;
            DateTimeOffset? onset = match.Onset;
            DateTimeOffset? expires = match.Expires;

            if (!string.IsNullOrWhiteSpace(capXml))
            {
                try
                {
                    XDocument capDoc = LoadXmlSafe(capXml);
                    XElement? root = capDoc.Root;
                    if (root is not null)
                    {
                        string capStatus = GetChildText(root, "status");
                        if (!string.IsNullOrWhiteSpace(capStatus) &&
                            !string.Equals(capStatus, "Actual", StringComparison.OrdinalIgnoreCase))
                        {
                            return null;
                        }

                        string capMsgType = GetChildText(root, "msgType");
                        if (string.Equals(capMsgType, "Cancel", StringComparison.OrdinalIgnoreCase))
                        {
                            return null;
                        }
                    }

                    List<XElement> infos = new();
                    foreach (XElement el in capDoc.Descendants())
                    {
                        if (string.Equals(el.Name.LocalName, "info", StringComparison.OrdinalIgnoreCase))
                        {
                            infos.Add(el);
                        }
                    }

                    XElement? selectedInfo = SelectCapInfoByLanguage(infos, normalizedLang);
                    if (selectedInfo is not null)
                    {
                        senderName = GetChildText(selectedInfo, "senderName");
                        rawDesc = GetChildText(selectedInfo, "description");
                        string capEvent = GetChildText(selectedInfo, "event");
                        if (!string.IsNullOrWhiteSpace(capEvent))
                        {
                            eventLabel = capEvent;
                        }

                        string capCertRaw = GetChildText(selectedInfo, "certainty");
                        if (!string.IsNullOrWhiteSpace(capCertRaw))
                        {
                            if (CertaintyRank(capCertRaw) < 0)
                            {
                                return null;
                            }

                            certainty = NormalizeCertainty(capCertRaw);
                        }

                        DateTimeOffset nowUtc = ToLocalDateTimeOffset(localNow).ToUniversalTime();
                        DateTimeOffset? capOnset = TryParseOffset(GetChildText(selectedInfo, "onset"))
                            ?? TryParseOffset(GetChildText(selectedInfo, "effective"));
                        if (capOnset.HasValue)
                        {
                            if (capOnset.Value.ToUniversalTime() > nowUtc.AddHours(18))
                            {
                                return null;
                            }
                            onset = capOnset;
                        }

                        DateTimeOffset? capExpires = TryParseOffset(GetChildText(selectedInfo, "expires"));
                        if (capExpires.HasValue)
                        {
                            if (capExpires.Value.ToUniversalTime() <= nowUtc)
                            {
                                return null;
                            }
                            expires = capExpires;
                        }

                        if (lat.HasValue && lon.HasValue)
                        {
                            GeometryMatchState geo = EvaluateElementGeometry(selectedInfo, lat.Value, lon.Value);
                            if (geo == GeometryMatchState.NoGeometry && root is not null)
                            {
                                geo = EvaluateElementGeometry(root, lat.Value, lon.Value);
                            }

                            if (geo == GeometryMatchState.Outside)
                            {
                                return null;
                            }
                        }

                        foreach (XElement param in selectedInfo.Elements())
                        {
                            if (!string.Equals(param.Name.LocalName, "parameter", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string vn = GetChildText(param, "valueName");
                            if (string.Equals(vn, "awareness_type", StringComparison.OrdinalIgnoreCase))
                            {
                                awarenessType = GetChildText(param, "value");
                            }
                        }

                        foreach (XElement areaEl in selectedInfo.Elements())
                        {
                            if (string.Equals(areaEl.Name.LocalName, "area", StringComparison.OrdinalIgnoreCase))
                            {
                                string ad = GetChildText(areaEl, "areaDesc");
                                if (!string.IsNullOrWhiteSpace(ad))
                                {
                                    areaDesc = ad;
                                }
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback ai metadati Atom gia validati
                }
            }

            string title = ResolveCapAlertTitle(awarenessType, eventLabel, normalizedLang);
            string detail = FormatAlertDetail(onset, expires, localNow, normalizedLang);
            string cleanDesc = StripDisclaimer(rawDesc);
            string sourceDisplay = string.IsNullOrWhiteSpace(senderName)
                ? "MeteoAlarm"
                : $"MeteoAlarm · {senderName.Trim()}";

            string levelLabel = GetLevelLabel(match.Level, normalizedLang);
            string timeRange = FormatTimeRange(onset, expires);
            var sb = new StringBuilder();
            sb.Append(levelLabel).Append(" · ").Append(title);
            if (!string.IsNullOrWhiteSpace(areaDesc))
            {
                sb.Append(" (").Append(areaDesc).Append(')');
            }
            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                sb.Append('\n').Append(timeRange);
            }
            if (!string.IsNullOrWhiteSpace(cleanDesc))
            {
                sb.Append('\n').Append(cleanDesc);
            }
            sb.Append('\n').Append(LocalizationService.Format(normalizedLang, "alert_source_official", sourceDisplay));

            return new WeatherAlertInfo(
                match.Level,
                title,
                detail,
                sb.ToString(),
                IsOfficial: true,
                SourceName: sourceDisplay,
                Description: string.IsNullOrWhiteSpace(cleanDesc) ? areaDesc : cleanDesc,
                Certainty: certainty);
        }

        internal static WeatherAlertInfo? ParseBrightSkyJson(string json, DateTime localNow, string lang = "it")
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("alerts", out JsonElement alertsEl) ||
                alertsEl.ValueKind != JsonValueKind.Array ||
                alertsEl.GetArrayLength() == 0)
            {
                return null;
            }

            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            DateTimeOffset nowUtc = ToLocalDateTimeOffset(localNow).ToUniversalTime();
            DateTimeOffset maxLookaheadUtc = nowUtc.AddHours(18);
            bool preferGerman = normalizedLang == "de";

            WeatherAlertLevel bestLevel = WeatherAlertLevel.None;
            int bestRank = -1;
            bool bestActive = false;
            int bestCertRank = -2;
            DateTimeOffset bestOnset = DateTimeOffset.MaxValue;
            JsonElement? bestEl = null;

            foreach (JsonElement item in alertsEl.EnumerateArray())
            {
                string status = GetJsonString(item, "status");
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "actual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTimeOffset? expires = TryParseOffset(GetJsonString(item, "expires"));
                if (expires.HasValue && expires.Value.ToUniversalTime() <= nowUtc)
                {
                    continue;
                }

                DateTimeOffset? onset = TryParseOffset(GetJsonString(item, "onset"))
                    ?? TryParseOffset(GetJsonString(item, "effective"));
                if (onset.HasValue && onset.Value.ToUniversalTime() > maxLookaheadUtc)
                {
                    continue;
                }

                int certRank = CertaintyRank(GetJsonString(item, "certainty"));
                if (certRank < 0)
                {
                    continue;
                }

                string sev = GetJsonString(item, "severity");
                WeatherAlertLevel level = MapSeverityAndEvent(sev, GetJsonString(item, "event_en"));
                if (level == WeatherAlertLevel.None)
                {
                    level = WeatherAlertLevel.Yellow;
                }

                int rank = LevelRank(level);
                bool activeNow = !onset.HasValue || onset.Value.ToUniversalTime() <= nowUtc;
                DateTimeOffset sortOnset = onset?.ToUniversalTime() ?? nowUtc;

                if (bestEl is null
                    || rank > bestRank
                    || (rank == bestRank && activeNow && !bestActive)
                    || (rank == bestRank && activeNow == bestActive && certRank > bestCertRank)
                    || (rank == bestRank && activeNow == bestActive && certRank == bestCertRank && sortOnset < bestOnset))
                {
                    bestLevel = level;
                    bestRank = rank;
                    bestActive = activeNow;
                    bestCertRank = certRank;
                    bestOnset = sortOnset;
                    bestEl = item;
                }
            }

            if (bestEl is null)
            {
                return null;
            }

            JsonElement chosen = bestEl.Value;
            string evEn = GetJsonString(chosen, "event_en");
            string evDe = GetJsonString(chosen, "event_de");
            string desc = preferGerman
                ? GetFirstNonEmpty(GetJsonString(chosen, "description_de"), GetJsonString(chosen, "description_en"))
                : GetFirstNonEmpty(GetJsonString(chosen, "description_en"), GetJsonString(chosen, "description_de"));

            DateTimeOffset? bOnset = TryParseOffset(GetJsonString(chosen, "onset"))
                ?? TryParseOffset(GetJsonString(chosen, "effective"));
            DateTimeOffset? bExpires = TryParseOffset(GetJsonString(chosen, "expires"));
            string certainty = NormalizeCertainty(GetJsonString(chosen, "certainty"));

            string title = ResolveCapAlertTitle(string.Empty, GetFirstNonEmpty(evEn, evDe), normalizedLang);
            string detail = FormatAlertDetail(bOnset, bExpires, localNow, normalizedLang);
            const string source = "DWD (Deutscher Wetterdienst)";
            string cleanDesc = StripDisclaimer(desc);
            string levelLabel = GetLevelLabel(bestLevel, normalizedLang);
            string timeRange = FormatTimeRange(bOnset, bExpires);

            var sb = new StringBuilder();
            sb.Append(levelLabel).Append(" · ").Append(title);
            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                sb.Append('\n').Append(timeRange);
            }
            if (!string.IsNullOrWhiteSpace(cleanDesc))
            {
                sb.Append('\n').Append(cleanDesc);
            }
            sb.Append('\n').Append(LocalizationService.Format(normalizedLang, "alert_source_official", source));

            return new WeatherAlertInfo(
                bestLevel,
                title,
                detail,
                sb.ToString(),
                IsOfficial: true,
                SourceName: source,
                Description: cleanDesc,
                Certainty: certainty);
        }

        internal static WeatherAlertInfo? ParseNwsJson(string json, DateTime localNow, string lang = "it")
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out JsonElement featuresEl) ||
                featuresEl.ValueKind != JsonValueKind.Array ||
                featuresEl.GetArrayLength() == 0)
            {
                return null;
            }

            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            DateTimeOffset nowUtc = ToLocalDateTimeOffset(localNow).ToUniversalTime();
            DateTimeOffset maxLookaheadUtc = nowUtc.AddHours(18);

            var supersededIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonElement feat in featuresEl.EnumerateArray())
            {
                if (!feat.TryGetProperty("properties", out JsonElement props))
                {
                    continue;
                }

                string status = GetJsonString(props, "status");
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string msgType = GetFirstNonEmpty(GetJsonString(props, "messageType"), GetJsonString(props, "msgType"));
                if (!string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(msgType, "Update", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (props.TryGetProperty("references", out JsonElement refsEl))
                {
                    if (refsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement refItem in refsEl.EnumerateArray())
                        {
                            if (refItem.ValueKind == JsonValueKind.Object)
                            {
                                string refId = GetJsonString(refItem, "identifier");
                                if (!string.IsNullOrWhiteSpace(refId))
                                {
                                    supersededIds.Add(refId);
                                }
                            }
                            else if (refItem.ValueKind == JsonValueKind.String)
                            {
                                foreach (string id in ExtractReferencedIdentifiers(refItem.GetString() ?? string.Empty))
                                {
                                    supersededIds.Add(id);
                                }
                            }
                        }
                    }
                    else if (refsEl.ValueKind == JsonValueKind.String)
                    {
                        foreach (string id in ExtractReferencedIdentifiers(refsEl.GetString() ?? string.Empty))
                        {
                            supersededIds.Add(id);
                        }
                    }
                }
            }

            WeatherAlertLevel bestLevel = WeatherAlertLevel.None;
            int bestRank = -1;
            bool bestActive = false;
            int bestCertRank = -2;
            DateTimeOffset bestOnset = DateTimeOffset.MaxValue;
            JsonElement? bestProps = null;

            foreach (JsonElement feat in featuresEl.EnumerateArray())
            {
                if (!feat.TryGetProperty("properties", out JsonElement props))
                {
                    continue;
                }

                string status = GetJsonString(props, "status");
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string msgType = GetFirstNonEmpty(GetJsonString(props, "messageType"), GetJsonString(props, "msgType"));
                if (string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string id = GetJsonString(props, "id");
                if (!string.IsNullOrWhiteSpace(id) && supersededIds.Contains(id))
                {
                    continue;
                }

                DateTimeOffset? expires = TryParseOffset(GetJsonString(props, "expires"));
                if (expires.HasValue && expires.Value.ToUniversalTime() <= nowUtc)
                {
                    continue;
                }

                DateTimeOffset? onset = TryParseOffset(GetJsonString(props, "onset"))
                    ?? TryParseOffset(GetJsonString(props, "effective"));
                if (onset.HasValue && onset.Value.ToUniversalTime() > maxLookaheadUtc)
                {
                    continue;
                }

                int certRank = CertaintyRank(GetJsonString(props, "certainty"));
                if (certRank < 0)
                {
                    continue;
                }

                string sev = GetJsonString(props, "severity");
                string ev = GetJsonString(props, "event");
                WeatherAlertLevel level = MapSeverityAndEvent(sev, ev);
                if (level == WeatherAlertLevel.None)
                {
                    continue;
                }

                int rank = LevelRank(level);
                bool activeNow = !onset.HasValue || onset.Value.ToUniversalTime() <= nowUtc;
                DateTimeOffset sortOnset = onset?.ToUniversalTime() ?? nowUtc;

                if (bestProps is null
                    || rank > bestRank
                    || (rank == bestRank && activeNow && !bestActive)
                    || (rank == bestRank && activeNow == bestActive && certRank > bestCertRank)
                    || (rank == bestRank && activeNow == bestActive && certRank == bestCertRank && sortOnset < bestOnset))
                {
                    bestLevel = level;
                    bestRank = rank;
                    bestActive = activeNow;
                    bestCertRank = certRank;
                    bestOnset = sortOnset;
                    bestProps = props;
                }
            }

            if (bestProps is null)
            {
                return null;
            }

            JsonElement chosen = bestProps.Value;
            string eventName = GetJsonString(chosen, "event");
            string headline = GetJsonString(chosen, "headline");
            string desc = GetFirstNonEmpty(headline, GetJsonString(chosen, "description"));
            DateTimeOffset? nOnset = TryParseOffset(GetJsonString(chosen, "onset"))
                ?? TryParseOffset(GetJsonString(chosen, "effective"));
            DateTimeOffset? nExpires = TryParseOffset(GetJsonString(chosen, "expires"));
            string certainty = NormalizeCertainty(GetJsonString(chosen, "certainty"));

            string title = ResolveCapAlertTitle(string.Empty, eventName, normalizedLang);
            string detail = FormatAlertDetail(nOnset, nExpires, localNow, normalizedLang);
            const string source = "NOAA National Weather Service";
            string levelLabel = GetLevelLabel(bestLevel, normalizedLang);
            string timeRange = FormatTimeRange(nOnset, nExpires);

            var sb = new StringBuilder();
            sb.Append(levelLabel).Append(" · ").Append(title);
            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                sb.Append('\n').Append(timeRange);
            }
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.Append('\n').Append(desc);
            }
            sb.Append('\n').Append(LocalizationService.Format(normalizedLang, "alert_source_official", source));

            return new WeatherAlertInfo(
                bestLevel,
                title,
                detail,
                sb.ToString(),
                IsOfficial: true,
                SourceName: source,
                Description: desc,
                Certainty: certainty);
        }

        internal static string? ExtractDpcBulletinId(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            string? best = null;
            void considerMatches(MatchCollection matches)
            {
                foreach (Match m in matches)
                {
                    if (m.Success && m.Groups.Count > 1)
                    {
                        string id = m.Groups[1].Value;
                        if (best is null || string.CompareOrdinal(id, best) > 0)
                        {
                            best = id;
                        }
                    }
                }
            }

            considerMatches(Regex.Matches(
                payload,
                @"files/(?:all|pdf|shp|xml|topojson|geojson)/(\d{8}_\d{4})",
                RegexOptions.IgnoreCase));

            if (best is null)
            {
                considerMatches(Regex.Matches(
                    payload,
                    @"\b(20\d{6}_\d{4})_(?:today|tomorrow|all|shp)\b",
                    RegexOptions.IgnoreCase));
            }

            return best;
        }

        private static string? ExtractFirstGithubCommitSha(string json)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    return GetJsonString(doc.RootElement[0], "sha");
                }
            }
            catch
            {
                // Ignore malformed commit response
            }

            return null;
        }

        internal static (bool ZoneFound, WeatherAlertInfo? Alert) ParseDpcTopoJson(
            string json,
            GeoRegionInfo region,
            DateTime localNow,
            string lang = "it")
        {
            if (string.IsNullOrWhiteSpace(json) || region is null)
            {
                return (false, null);
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("objects", out JsonElement objectsEl) ||
                objectsEl.ValueKind != JsonValueKind.Object)
            {
                return (false, null);
            }

            JsonElement? geometriesEl = null;
            foreach (JsonProperty prop in objectsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object &&
                    prop.Value.TryGetProperty("geometries", out JsonElement g) &&
                    g.ValueKind == JsonValueKind.Array)
                {
                    geometriesEl = g;
                    break;
                }
            }

            if (!geometriesEl.HasValue)
            {
                return (false, null);
            }

            var cityNorms = new HashSet<string>(StringComparer.Ordinal);
            var cityCompacts = new HashSet<string>(StringComparer.Ordinal);
            void addCityToken(string? raw)
            {
                string n = NormalizeAreaToken(raw ?? string.Empty);
                if (n.Length >= 2)
                {
                    cityNorms.Add(n);
                    cityCompacts.Add(n.Replace(" ", string.Empty, StringComparison.Ordinal));
                }
            }
            addCityToken(region.City);
            addCityToken(region.Municipality);

            var countyNorms = new HashSet<string>(StringComparer.Ordinal);
            var countyCompacts = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(region.County))
            {
                foreach (string part in region.County.Split(new[] { '/', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string n = NormalizeAreaToken(part);
                    if (n.Length >= 3)
                    {
                        countyNorms.Add(n);
                        countyCompacts.Add(n.Replace(" ", string.Empty, StringComparison.Ordinal));
                    }
                }
            }

            bool matchesSet(string raw, HashSet<string> norms, HashSet<string> compacts)
            {
                string n = NormalizeAreaToken(raw);
                if (n.Length < 2) return false;
                return norms.Contains(n) || compacts.Contains(n.Replace(" ", string.Empty, StringComparison.Ordinal));
            }

            List<JsonElement> matchedProps = new();
            foreach (JsonElement geom in geometriesEl.Value.EnumerateArray())
            {
                if (!geom.TryGetProperty("properties", out JsonElement props) ||
                    props.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (cityNorms.Count > 0 &&
                    props.TryGetProperty("Comuni", out JsonElement comuniEl) &&
                    comuniEl.ValueKind == JsonValueKind.Array)
                {
                    bool cityHit = false;
                    foreach (JsonElement cEl in comuniEl.EnumerateArray())
                    {
                        if (cEl.ValueKind == JsonValueKind.String &&
                            matchesSet(cEl.GetString() ?? string.Empty, cityNorms, cityCompacts))
                        {
                            cityHit = true;
                            break;
                        }
                    }

                    if (cityHit)
                    {
                        matchedProps.Add(props);
                    }
                }
            }

            if (matchedProps.Count == 0 && countyNorms.Count > 0)
            {
                foreach (JsonElement geom in geometriesEl.Value.EnumerateArray())
                {
                    if (!geom.TryGetProperty("properties", out JsonElement props) ||
                        props.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    bool hit = false;
                    string zona = GetJsonString(props, "Nome zona");
                    if (!string.IsNullOrWhiteSpace(zona) && matchesSet(zona, countyNorms, countyCompacts))
                    {
                        hit = true;
                    }
                    else if (props.TryGetProperty("Comuni", out JsonElement comuniEl) &&
                             comuniEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement cEl in comuniEl.EnumerateArray())
                        {
                            if (cEl.ValueKind == JsonValueKind.String &&
                                matchesSet(cEl.GetString() ?? string.Empty, countyNorms, countyCompacts))
                            {
                                hit = true;
                                break;
                            }
                        }
                    }

                    if (hit)
                    {
                        matchedProps.Add(props);
                    }
                }
            }

            if (matchedProps.Count == 0)
            {
                return (false, null);
            }

            WeatherAlertLevel bestLevel = WeatherAlertLevel.None;
            JsonElement? chosenProps = null;
            WeatherAlertLevel chosenHydr = WeatherAlertLevel.None;
            WeatherAlertLevel chosenStorm = WeatherAlertLevel.None;
            WeatherAlertLevel chosenHydroGeo = WeatherAlertLevel.None;
            string chosenMapRaw = string.Empty;

            foreach (JsonElement p in matchedProps)
            {
                string mapRaw = GetJsonString(p, "Rappresentata nella mappa");
                WeatherAlertLevel mapLvl = ParseDpcAlertLevel(mapRaw);
                WeatherAlertLevel hydrLvl = ParseDpcAlertLevel(GetJsonString(p, "Per rischio idraulico"));
                WeatherAlertLevel stormLvl = ParseDpcAlertLevel(GetJsonString(p, "Per rischio temporali"));
                WeatherAlertLevel hydroLvl = ParseDpcAlertLevel(GetJsonString(p, "Per rischio idrogeologico"));

                WeatherAlertLevel maxLvl = mapLvl;
                if (LevelRank(hydrLvl) > LevelRank(maxLvl)) maxLvl = hydrLvl;
                if (LevelRank(stormLvl) > LevelRank(maxLvl)) maxLvl = stormLvl;
                if (LevelRank(hydroLvl) > LevelRank(maxLvl)) maxLvl = hydroLvl;

                if (!chosenProps.HasValue || LevelRank(maxLvl) > LevelRank(bestLevel))
                {
                    bestLevel = maxLvl;
                    chosenProps = p;
                    chosenHydr = hydrLvl;
                    chosenStorm = stormLvl;
                    chosenHydroGeo = hydroLvl;
                    chosenMapRaw = mapRaw;
                }
            }

            if (bestLevel == WeatherAlertLevel.None || !chosenProps.HasValue)
            {
                return (true, null);
            }

            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            string zonaName = GetJsonString(chosenProps.Value, "Nome zona");

            string title;
            if (chosenStorm == bestLevel)
            {
                title = LocalizationService.T(normalizedLang, "alert_cap_storm");
            }
            else if (chosenHydr == bestLevel)
            {
                title = LocalizationService.T(normalizedLang, "alert_cap_flood");
            }
            else if (chosenHydroGeo == bestLevel)
            {
                title = LocalizationService.T(normalizedLang, "alert_cap_hydro");
            }
            else
            {
                title = ResolveCapAlertTitle(string.Empty, chosenMapRaw, normalizedLang);
            }

            var riskNames = new List<string>(3);
            if (chosenStorm != WeatherAlertLevel.None) riskNames.Add(LocalizationService.T(normalizedLang, "alert_cap_storm"));
            if (chosenHydr != WeatherAlertLevel.None) riskNames.Add(LocalizationService.T(normalizedLang, "alert_cap_flood"));
            if (chosenHydroGeo != WeatherAlertLevel.None) riskNames.Add(LocalizationService.T(normalizedLang, "alert_cap_hydro"));

            string risksSummary = riskNames.Count > 0
                ? string.Join(" · ", riskNames)
                : chosenMapRaw;
            string desc = string.IsNullOrWhiteSpace(zonaName)
                ? risksSummary
                : $"{zonaName}: {risksSummary}";

            DateTime endOfDay = localNow.Date.AddDays(1).AddMinutes(-1);
            DateTimeOffset expires = ToLocalDateTimeOffset(endOfDay);
            string detail = FormatAlertDetail(null, expires, localNow, normalizedLang);
            const string source = "Protezione Civile Nazionale";
            string levelLabel = GetLevelLabel(bestLevel, normalizedLang);
            string timeRange = FormatTimeRange(null, expires);

            var sb = new StringBuilder();
            sb.Append(levelLabel).Append(" · ").Append(title);
            if (!string.IsNullOrWhiteSpace(zonaName))
            {
                sb.Append(" (").Append(zonaName).Append(')');
            }
            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                sb.Append('\n').Append(timeRange);
            }
            if (!string.IsNullOrWhiteSpace(risksSummary))
            {
                sb.Append('\n').Append(risksSummary);
            }
            sb.Append('\n').Append(LocalizationService.Format(normalizedLang, "alert_source_official", source));

            var alert = new WeatherAlertInfo(
                bestLevel,
                title,
                detail,
                sb.ToString(),
                IsOfficial: true,
                SourceName: source,
                Description: desc,
                Certainty: "Likely");

            return (true, alert);
        }

        private static WeatherAlertLevel ParseDpcAlertLevel(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return WeatherAlertLevel.None;
            }

            string upper = raw.Trim().ToUpperInvariant();
            if (upper.Contains("NESSUNA ALLERTA", StringComparison.Ordinal) ||
                upper.Contains("ASSENZA DI FENOMENI", StringComparison.Ordinal))
            {
                return WeatherAlertLevel.None;
            }

            if (upper.Contains("ROSSA", StringComparison.Ordinal) ||
                upper.Contains("ELEVATA", StringComparison.Ordinal))
            {
                return WeatherAlertLevel.Red;
            }

            if (upper.Contains("ARANCIONE", StringComparison.Ordinal) ||
                upper.Contains("MODERATA", StringComparison.Ordinal))
            {
                return WeatherAlertLevel.Orange;
            }

            if (upper.Contains("GIALLA", StringComparison.Ordinal) ||
                upper.Contains("ORDINARIA", StringComparison.Ordinal))
            {
                return WeatherAlertLevel.Yellow;
            }

            return WeatherAlertLevel.None;
        }

        internal static WeatherAlertInfo? ParseIpmaJson(
            string json,
            GeoRegionInfo region,
            DateTime localNow,
            string lang = "it")
        {
            if (string.IsNullOrWhiteSpace(json) || region is null)
            {
                return null;
            }

            HashSet<string> targetAreas = ResolveIpmaAreaIds(region);
            if (targetAreas.Count == 0)
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array ||
                doc.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            DateTimeOffset nowUtc = ToLocalDateTimeOffset(localNow).ToUniversalTime();
            DateTimeOffset maxLookaheadUtc = nowUtc.AddHours(18);

            WeatherAlertLevel bestLevel = WeatherAlertLevel.None;
            int bestRank = -1;
            bool bestActive = false;
            DateTimeOffset bestOnset = DateTimeOffset.MaxValue;
            JsonElement? bestEl = null;

            foreach (JsonElement item in doc.RootElement.EnumerateArray())
            {
                string areaCode = GetJsonString(item, "idAreaAviso");
                if (string.IsNullOrWhiteSpace(areaCode) || !targetAreas.Contains(areaCode))
                {
                    continue;
                }

                string lvlStr = GetJsonString(item, "awarenessLevelID").ToLowerInvariant();
                WeatherAlertLevel level = lvlStr switch
                {
                    "red" => WeatherAlertLevel.Red,
                    "orange" => WeatherAlertLevel.Orange,
                    "yellow" => WeatherAlertLevel.Yellow,
                    _ => WeatherAlertLevel.None
                };
                if (level == WeatherAlertLevel.None)
                {
                    continue;
                }

                DateTimeOffset? expires = TryParseOffset(GetJsonString(item, "endTime"));
                if (expires.HasValue && expires.Value.ToUniversalTime() <= nowUtc)
                {
                    continue;
                }

                DateTimeOffset? onset = TryParseOffset(GetJsonString(item, "startTime"));
                if (onset.HasValue && onset.Value.ToUniversalTime() > maxLookaheadUtc)
                {
                    continue;
                }

                int rank = LevelRank(level);
                bool activeNow = !onset.HasValue || onset.Value.ToUniversalTime() <= nowUtc;
                DateTimeOffset sortOnset = onset?.ToUniversalTime() ?? nowUtc;

                if (bestEl is null
                    || rank > bestRank
                    || (rank == bestRank && activeNow && !bestActive)
                    || (rank == bestRank && activeNow == bestActive && sortOnset < bestOnset))
                {
                    bestLevel = level;
                    bestRank = rank;
                    bestActive = activeNow;
                    bestOnset = sortOnset;
                    bestEl = item;
                }
            }

            if (bestEl is null)
            {
                return null;
            }

            JsonElement chosen = bestEl.Value;
            string typeName = GetJsonString(chosen, "awarenessTypeName");
            string text = GetJsonString(chosen, "text");
            DateTimeOffset? pOnset = TryParseOffset(GetJsonString(chosen, "startTime"));
            DateTimeOffset? pExpires = TryParseOffset(GetJsonString(chosen, "endTime"));

            string title = ResolveCapAlertTitle(string.Empty, typeName, normalizedLang);
            string detail = FormatAlertDetail(pOnset, pExpires, localNow, normalizedLang);
            const string source = "IPMA";
            string levelLabel = GetLevelLabel(bestLevel, normalizedLang);
            string timeRange = FormatTimeRange(pOnset, pExpires);

            var sb = new StringBuilder();
            sb.Append(levelLabel).Append(" · ").Append(title);
            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                sb.Append('\n').Append(timeRange);
            }
            if (!string.IsNullOrWhiteSpace(text))
            {
                sb.Append('\n').Append(text);
            }
            sb.Append('\n').Append(LocalizationService.Format(normalizedLang, "alert_source_official", source));

            return new WeatherAlertInfo(
                bestLevel,
                title,
                detail,
                sb.ToString(),
                IsOfficial: true,
                SourceName: source,
                Description: string.IsNullOrWhiteSpace(text) ? typeName : text,
                Certainty: "Likely");
        }

        private static HashSet<string> ResolveIpmaAreaIds(GeoRegionInfo region)
        {
            var areas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void tryMatch(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return;
                }

                foreach (string part in raw.Split(new[] { '/', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string norm = NormalizeAreaToken(part);
                    if (norm.Length < 3) continue;

                    if (PortugueseDistrictToIpmaArea.TryGetValue(norm, out string[]? exact))
                    {
                        foreach (string code in exact) areas.Add(code);
                        continue;
                    }

                    foreach (KeyValuePair<string, string[]> kvp in PortugueseDistrictToIpmaArea)
                    {
                        if (norm.Contains(kvp.Key, StringComparison.Ordinal))
                        {
                            foreach (string code in kvp.Value) areas.Add(code);
                        }
                    }
                }
            }

            if (string.Equals(region.IsoLvl4?.Trim(), "PT-30", StringComparison.OrdinalIgnoreCase))
            {
                tryMatch("madeira");
            }
            else if (string.Equals(region.IsoLvl4?.Trim(), "PT-20", StringComparison.OrdinalIgnoreCase))
            {
                tryMatch("acores");
            }

            tryMatch(region.State);
            tryMatch(region.Province);
            tryMatch(region.County);
            tryMatch(region.Municipality);
            tryMatch(region.City);
            return areas;
        }

        internal static WeatherAlertInfo? ParseMetNorwayJson(string json, DateTime localNow, string lang = "it")
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out JsonElement featuresEl) ||
                featuresEl.ValueKind != JsonValueKind.Array ||
                featuresEl.GetArrayLength() == 0)
            {
                return null;
            }

            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            DateTimeOffset nowUtc = ToLocalDateTimeOffset(localNow).ToUniversalTime();
            DateTimeOffset maxLookaheadUtc = nowUtc.AddHours(18);

            WeatherAlertLevel bestLevel = WeatherAlertLevel.None;
            int bestRank = -1;
            bool bestActive = false;
            int bestCertRank = -2;
            DateTimeOffset bestOnset = DateTimeOffset.MaxValue;
            JsonElement? bestFeature = null;

            foreach (JsonElement feat in featuresEl.EnumerateArray())
            {
                if (!feat.TryGetProperty("properties", out JsonElement props) ||
                    props.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string status = GetJsonString(props, "status");
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string msgType = GetFirstNonEmpty(GetJsonString(props, "msgType"), GetJsonString(props, "messageType"));
                if (string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int certRank = CertaintyRank(GetJsonString(props, "certainty"));
                if (certRank < 0)
                {
                    continue;
                }

                string awarenessLvl = GetJsonString(props, "awareness_level").ToLowerInvariant();
                if (awarenessLvl.Contains("green", StringComparison.Ordinal))
                {
                    continue;
                }

                string sev = GetJsonString(props, "severity");
                WeatherAlertLevel level = MapSeverityAndEvent(sev, awarenessLvl);
                if (level == WeatherAlertLevel.None)
                {
                    continue;
                }

                DateTimeOffset? onset = null;
                DateTimeOffset? expires = null;
                if (feat.TryGetProperty("when", out JsonElement whenEl) &&
                    whenEl.ValueKind == JsonValueKind.Object &&
                    whenEl.TryGetProperty("interval", out JsonElement intervalEl) &&
                    intervalEl.ValueKind == JsonValueKind.Array)
                {
                    int len = intervalEl.GetArrayLength();
                    if (len >= 1 && intervalEl[0].ValueKind == JsonValueKind.String)
                    {
                        onset = TryParseOffset(intervalEl[0].GetString());
                    }
                    if (len >= 2 && intervalEl[1].ValueKind == JsonValueKind.String)
                    {
                        expires = TryParseOffset(intervalEl[1].GetString());
                    }
                }

                onset ??= TryParseOffset(GetJsonString(props, "onset"))
                    ?? TryParseOffset(GetJsonString(props, "effective"));
                expires ??= TryParseOffset(GetJsonString(props, "expires"));

                if (expires.HasValue && expires.Value.ToUniversalTime() <= nowUtc)
                {
                    continue;
                }

                if (onset.HasValue && onset.Value.ToUniversalTime() > maxLookaheadUtc)
                {
                    continue;
                }

                int rank = LevelRank(level);
                bool activeNow = !onset.HasValue || onset.Value.ToUniversalTime() <= nowUtc;
                DateTimeOffset sortOnset = onset?.ToUniversalTime() ?? nowUtc;

                if (bestFeature is null
                    || rank > bestRank
                    || (rank == bestRank && activeNow && !bestActive)
                    || (rank == bestRank && activeNow == bestActive && certRank > bestCertRank)
                    || (rank == bestRank && activeNow == bestActive && certRank == bestCertRank && sortOnset < bestOnset))
                {
                    bestLevel = level;
                    bestRank = rank;
                    bestActive = activeNow;
                    bestCertRank = certRank;
                    bestOnset = sortOnset;
                    bestFeature = feat;
                }
            }

            if (bestFeature is null || !bestFeature.Value.TryGetProperty("properties", out JsonElement chosenProps))
            {
                return null;
            }

            DateTimeOffset? fOnset = null;
            DateTimeOffset? fExpires = null;
            if (bestFeature.Value.TryGetProperty("when", out JsonElement chosenWhen) &&
                chosenWhen.TryGetProperty("interval", out JsonElement chosenInterval) &&
                chosenInterval.ValueKind == JsonValueKind.Array)
            {
                if (chosenInterval.GetArrayLength() >= 1 && chosenInterval[0].ValueKind == JsonValueKind.String)
                {
                    fOnset = TryParseOffset(chosenInterval[0].GetString());
                }
                if (chosenInterval.GetArrayLength() >= 2 && chosenInterval[1].ValueKind == JsonValueKind.String)
                {
                    fExpires = TryParseOffset(chosenInterval[1].GetString());
                }
            }

            fOnset ??= TryParseOffset(GetJsonString(chosenProps, "onset"))
                ?? TryParseOffset(GetJsonString(chosenProps, "effective"));
            fExpires ??= TryParseOffset(GetJsonString(chosenProps, "expires"));

            string eventName = GetFirstNonEmpty(
                GetJsonString(chosenProps, "eventAwarenessName"),
                GetJsonString(chosenProps, "event"));
            string desc = GetFirstNonEmpty(
                GetJsonString(chosenProps, "description"),
                GetJsonString(chosenProps, "instruction"),
                GetJsonString(chosenProps, "area"));
            string certainty = NormalizeCertainty(GetJsonString(chosenProps, "certainty"));

            string title = ResolveCapAlertTitle(string.Empty, eventName, normalizedLang);
            string detail = FormatAlertDetail(fOnset, fExpires, localNow, normalizedLang);
            const string source = "MET Norway";
            string levelLabel = GetLevelLabel(bestLevel, normalizedLang);
            string timeRange = FormatTimeRange(fOnset, fExpires);

            var sb = new StringBuilder();
            sb.Append(levelLabel).Append(" · ").Append(title);
            if (!string.IsNullOrWhiteSpace(timeRange))
            {
                sb.Append('\n').Append(timeRange);
            }
            if (!string.IsNullOrWhiteSpace(desc))
            {
                sb.Append('\n').Append(desc);
            }
            sb.Append('\n').Append(LocalizationService.Format(normalizedLang, "alert_source_official", source));

            return new WeatherAlertInfo(
                bestLevel,
                title,
                detail,
                sb.ToString(),
                IsOfficial: true,
                SourceName: source,
                Description: desc,
                Certainty: certainty);
        }

        private static string? ResolveExpectedEmmaId(GeoRegionInfo region)
        {
            if (!string.IsNullOrWhiteSpace(region.IsoLvl4) &&
                ItalianIsoToEmma.TryGetValue(region.IsoLvl4.Trim(), out string? emma))
            {
                return emma;
            }

            return null;
        }

        private static List<string> BuildCandidateAreaTokens(GeoRegionInfo region)
        {
            var list = new List<string>();
            void AddTokens(string? raw)
            {
                if (string.IsNullOrWhiteSpace(raw)) return;
                foreach (string part in raw.Split(new[] { '/', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string norm = NormalizeAreaToken(part);
                    if (norm.Length >= 3 && !list.Contains(norm))
                    {
                        list.Add(norm);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(region.IsoLvl4) &&
                DutchIsoToAliases.TryGetValue(region.IsoLvl4.Trim(), out string[]? nlAliases))
            {
                foreach (string a in nlAliases)
                {
                    AddTokens(a);
                }
            }

            AddTokens(region.State);
            AddTokens(region.Province);
            AddTokens(region.County);
            AddTokens(region.Municipality);
            AddTokens(region.City);
            return list;
        }

        private static bool IsRegionMatch(
            string? expectedEmma,
            List<string> candidateAreas,
            string entryGeocode,
            string areaDesc)
        {
            if (!string.IsNullOrWhiteSpace(expectedEmma) &&
                !string.IsNullOrWhiteSpace(entryGeocode))
            {
                if (string.Equals(expectedEmma, entryGeocode.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // In Italia gli EMMA_ID delle 20 regioni sono univoci e completi
                if (entryGeocode.StartsWith("IT", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(areaDesc) || candidateAreas.Count == 0)
            {
                return false;
            }

            string fullAreaNorm = NormalizeAreaToken(areaDesc);
            string[] areaParts = areaDesc.Split(new[] { '/', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string cand in candidateAreas)
            {
                if (string.Equals(fullAreaNorm, cand, StringComparison.Ordinal))
                {
                    return true;
                }

                foreach (string part in areaParts)
                {
                    string partNorm = NormalizeAreaToken(part);
                    if (partNorm.Length < 3) continue;

                    if (string.Equals(partNorm, cand, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    // Gestisce prefissi tipo "Costa - Minho" o "Comunidad de Madrid" vs "Madrid"
                    if (cand.Length >= 5 && (partNorm.Contains(cand, StringComparison.Ordinal) || cand.Contains(partNorm, StringComparison.Ordinal)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string NormalizeAreaToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string decomposed = input.Trim().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (c is ' ' or '-' or '_')
                {
                    sb.Append(' ');
                }
            }

            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static XElement? SelectCapInfoByLanguage(List<XElement> infos, string targetLang)
        {
            if (infos.Count == 0)
            {
                return null;
            }

            foreach (XElement info in infos)
            {
                string l = GetChildText(info, "language");
                if (!string.IsNullOrWhiteSpace(l) &&
                    l.StartsWith(targetLang, StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            foreach (XElement info in infos)
            {
                string l = GetChildText(info, "language");
                if (!string.IsNullOrWhiteSpace(l) &&
                    l.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                {
                    return info;
                }
            }

            return infos[0];
        }

        private static string ResolveCapAlertTitle(string awarenessType, string eventText, string lang)
        {
            if (!string.IsNullOrWhiteSpace(awarenessType))
            {
                string trimmed = awarenessType.Trim();
                int semiIdx = trimmed.IndexOf(';');
                string codeStr = (semiIdx > 0 ? trimmed.Substring(0, semiIdx) : trimmed).Trim();
                if (int.TryParse(codeStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                {
                    string? key = code switch
                    {
                        1 => "alert_cap_wind",
                        2 => "alert_cap_snow_ice",
                        3 => "alert_cap_storm",
                        4 => "alert_cap_fog",
                        5 => "alert_cap_heat",
                        6 => "alert_cap_cold",
                        7 => "alert_cap_coastal",
                        8 => "alert_cap_fire",
                        9 => "alert_cap_avalanche",
                        10 => "alert_cap_rain",
                        12 or 13 => "alert_cap_flood",
                        _ => null
                    };
                    if (key is not null)
                    {
                        return LocalizationService.T(lang, key);
                    }
                }
            }

            string lower = (eventText ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("idrogeol") || lower.Contains("hydrogeo"))
                return LocalizationService.T(lang, "alert_cap_hydro");
            if (lower.Contains("thunder") || lower.Contains("storm") || lower.Contains("gewitter") || lower.Contains("onweer") || lower.Contains("tormenta") || lower.Contains("temporale") || lower.Contains("trovoada") || lower.Contains("torden"))
                return LocalizationService.T(lang, "alert_cap_storm");
            if (lower.Contains("wind") || lower.Contains("gust") || lower.Contains("sturm") || lower.Contains("viento") || lower.Contains("vento") || lower.Contains("böen") || lower.Contains("gale") || lower.Contains("kuling") || lower.Contains("vind"))
                return LocalizationService.T(lang, "alert_cap_wind");
            if (lower.Contains("frost") || lower.Contains("cold") || lower.Contains("ice") || lower.Contains("glätte") || lower.Contains("gelo") || lower.Contains("ijzel") || lower.Contains("frio"))
                return LocalizationService.T(lang, "alert_cap_cold");
            if (lower.Contains("snow") || lower.Contains("schnee") || lower.Contains("nieve") || lower.Contains("neve") || lower.Contains("sneeuw") || lower.Contains("snø"))
                return LocalizationService.T(lang, "alert_cap_snow_ice");
            if (lower.Contains("flood") || lower.Contains("alluv") || lower.Contains("idraul") || lower.Contains("flom"))
                return LocalizationService.T(lang, "alert_cap_flood");
            if (lower.Contains("rain") || lower.Contains("regen") || lower.Contains("lluvia") || lower.Contains("pioggia") || lower.Contains("chuva") || lower.Contains("precipit") || lower.Contains("nedbør") || lower.Contains("regn"))
                return LocalizationService.T(lang, "alert_cap_rain");
            if (lower.Contains("fog") || lower.Contains("nebel") || lower.Contains("mist") || lower.Contains("niebla") || lower.Contains("nebbia") || lower.Contains("nevoeiro"))
                return LocalizationService.T(lang, "alert_cap_fog");
            if (lower.Contains("heat") || lower.Contains("hitze") || lower.Contains("calor") || lower.Contains("caldo") || lower.Contains("temperature") || lower.Contains("quente"))
                return LocalizationService.T(lang, "alert_cap_heat");
            if (lower.Contains("coast") || lower.Contains("wave") || lower.Contains("mare") || lower.Contains("marítim") || lower.Contains("maritim") || lower.Contains("agitaç") || lower.Contains("agitac"))
                return LocalizationService.T(lang, "alert_cap_coastal");
            if (lower.Contains("avalan"))
                return LocalizationService.T(lang, "alert_cap_avalanche");
            if (lower.Contains("fire"))
                return LocalizationService.T(lang, "alert_cap_fire");

            return LocalizationService.T(lang, "alert_cap_generic");
        }

        private static WeatherAlertLevel MapSeverityAndEvent(string severity, string eventName)
        {
            string sev = (severity ?? string.Empty).Trim().ToLowerInvariant();
            string ev = (eventName ?? string.Empty).Trim().ToLowerInvariant();

            if (sev == "extreme" || ev.Contains("red")) return WeatherAlertLevel.Red;
            if (sev == "severe" || ev.Contains("orange") || ev.Contains("amber")) return WeatherAlertLevel.Orange;
            if (sev is "moderate" or "minor" || ev.Contains("yellow")) return WeatherAlertLevel.Yellow;
            return WeatherAlertLevel.None;
        }

        private static int LevelRank(WeatherAlertLevel level) => level switch
        {
            WeatherAlertLevel.Red => 3,
            WeatherAlertLevel.Orange => 2,
            WeatherAlertLevel.Yellow => 1,
            _ => 0
        };

        private static string GetLevelLabel(WeatherAlertLevel level, string lang) => level switch
        {
            WeatherAlertLevel.Red => LocalizationService.T(lang, "alert_lvl_red"),
            WeatherAlertLevel.Orange => LocalizationService.T(lang, "alert_lvl_orange"),
            WeatherAlertLevel.Yellow => LocalizationService.T(lang, "alert_lvl_yellow"),
            _ => string.Empty
        };

        private static string FormatAlertDetail(DateTimeOffset? onset, DateTimeOffset? expires, DateTime localNow, string lang)
        {
            DateTimeOffset nowRef = ToLocalDateTimeOffset(localNow);
            if (!onset.HasValue || onset.Value <= nowRef)
            {
                if (expires.HasValue)
                {
                    string untilTime = expires.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
                    return LocalizationService.Format(lang, "alert_until", untilTime);
                }
                return LocalizationService.T(lang, "alert_now");
            }

            string fromTime = onset.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            return LocalizationService.Format(lang, "alert_from", fromTime);
        }

        private static string FormatTimeRange(DateTimeOffset? onset, DateTimeOffset? expires)
        {
            if (onset.HasValue && expires.HasValue)
            {
                return $"{onset.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)} – {expires.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)}";
            }
            if (expires.HasValue)
            {
                return expires.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            return string.Empty;
        }

        private static string StripDisclaimer(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            int idx = text.IndexOf("(DISCLAIMER:", StringComparison.OrdinalIgnoreCase);
            string trimmed = idx >= 0 ? text.Substring(0, idx) : text;
            return Regex.Replace(trimmed, @"\s+", " ").Trim();
        }

        private static XDocument LoadXmlSafe(string xml)
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };
            using var sr = new StringReader(xml);
            using var reader = XmlReader.Create(sr, settings);
            return XDocument.Load(reader);
        }

        private static string GetChildText(XElement parent, string localName)
        {
            foreach (XElement el in parent.Elements())
            {
                if (string.Equals(el.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))
                {
                    return el.Value.Trim();
                }
            }
            return string.Empty;
        }

        private static DateTimeOffset? TryParseOffset(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTimeOffset parsed))
            {
                return parsed;
            }

            return null;
        }

        private static DateTimeOffset ToLocalDateTimeOffset(DateTime localTime)
        {
            TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(localTime);
            return new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), offset);
        }

        private static string GetJsonString(JsonElement obj, string propName)
        {
            if (obj.ValueKind == JsonValueKind.Object &&
                obj.TryGetProperty(propName, out JsonElement val) &&
                val.ValueKind == JsonValueKind.String)
            {
                return val.GetString()?.Trim() ?? string.Empty;
            }
            return string.Empty;
        }

        private static string GetFirstNonEmpty(params string[] values)
        {
            foreach (string v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                {
                    return v.Trim();
                }
            }
            return string.Empty;
        }

        internal static GeometryMatchState EvaluateElementGeometry(XElement container, double lat, double lon)
        {
            bool hasValidGeometry = false;

            foreach (XElement el in container.Descendants())
            {
                if (string.Equals(el.Name.LocalName, "polygon", StringComparison.OrdinalIgnoreCase))
                {
                    List<(double Lat, double Lon)> poly = ParseCapPolygon(el.Value);
                    if (poly.Count >= 3)
                    {
                        hasValidGeometry = true;
                        if (IsPointInPolygon(lat, lon, poly))
                        {
                            return GeometryMatchState.Inside;
                        }
                    }
                }
                else if (string.Equals(el.Name.LocalName, "circle", StringComparison.OrdinalIgnoreCase))
                {
                    (double Lat, double Lon, double RadiusKm)? circle = ParseCapCircle(el.Value);
                    if (circle.HasValue)
                    {
                        hasValidGeometry = true;
                        if (IsPointInCircle(lat, lon, circle.Value.Lat, circle.Value.Lon, circle.Value.RadiusKm))
                        {
                            return GeometryMatchState.Inside;
                        }
                    }
                }
            }

            return hasValidGeometry ? GeometryMatchState.Outside : GeometryMatchState.NoGeometry;
        }

        internal static List<(double Lat, double Lon)> ParseCapPolygon(string raw)
        {
            var points = new List<(double Lat, double Lon)>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return points;
            }

            string[] pairs = raw.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs)
            {
                int comma = pair.IndexOf(',');
                if (comma <= 0 || comma >= pair.Length - 1)
                {
                    continue;
                }

                string latPart = pair.Substring(0, comma);
                string lonPart = pair.Substring(comma + 1);
                if (double.TryParse(latPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double latVal) &&
                    double.TryParse(lonPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double lonVal) &&
                    double.IsFinite(latVal) && double.IsFinite(lonVal) &&
                    latVal >= -90.0 && latVal <= 90.0 &&
                    lonVal >= -180.0 && lonVal <= 180.0)
                {
                    points.Add((latVal, lonVal));
                }
            }

            return points.Count >= 3 ? points : new List<(double Lat, double Lon)>();
        }

        internal static bool IsPointInPolygon(double lat, double lon, IReadOnlyList<(double Lat, double Lon)> polygon)
        {
            if (polygon is null || polygon.Count < 3)
            {
                return false;
            }

            const double eps = 1e-9;
            int n = polygon.Count;

            for (int i = 0; i < n; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % n];
                if (IsPointOnSegment(lat, lon, a.Lat, a.Lon, b.Lat, b.Lon, eps))
                {
                    return true;
                }
            }

            bool inside = false;
            int j = n - 1;
            for (int i = 0; i < n; i++)
            {
                double yi = polygon[i].Lat;
                double xi = polygon[i].Lon;
                double yj = polygon[j].Lat;
                double xj = polygon[j].Lon;

                if (((yi > lat) != (yj > lat)) &&
                    (lon < (xj - xi) * (lat - yi) / (yj - yi) + xi))
                {
                    inside = !inside;
                }

                j = i;
            }

            return inside;
        }

        private static bool IsPointOnSegment(double py, double px, double ay, double ax, double by, double bx, double eps)
        {
            double minX = Math.Min(ax, bx) - eps;
            double maxX = Math.Max(ax, bx) + eps;
            double minY = Math.Min(ay, by) - eps;
            double maxY = Math.Max(ay, by) + eps;
            if (px < minX || px > maxX || py < minY || py > maxY)
            {
                return false;
            }

            double dx = bx - ax;
            double dy = by - ay;
            double cross = Math.Abs((px - ax) * dy - (py - ay) * dx);
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < eps)
            {
                return Math.Abs(px - ax) < eps && Math.Abs(py - ay) < eps;
            }

            return (cross / len) <= eps;
        }

        internal static (double Lat, double Lon, double RadiusKm)? ParseCapCircle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            Match m = Regex.Match(
                raw.Trim(),
                @"^([+-]?\d+(?:\.\d+)?)\s*,\s*([+-]?\d+(?:\.\d+)?)\s+([+-]?\d+(?:\.\d+)?)$");
            if (!m.Success)
            {
                return null;
            }

            if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon) &&
                double.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double radiusKm) &&
                double.IsFinite(lat) && double.IsFinite(lon) && double.IsFinite(radiusKm) &&
                lat >= -90.0 && lat <= 90.0 &&
                lon >= -180.0 && lon <= 180.0 &&
                radiusKm >= 0.0)
            {
                return (lat, lon, radiusKm);
            }

            return null;
        }

        internal static double ComputeHaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371.0088;
            const double degToRad = Math.PI / 180.0;
            double dLat = (lat2 - lat1) * degToRad;
            double dLon = (lon2 - lon1) * degToRad;
            double rLat1 = lat1 * degToRad;
            double rLat2 = lat2 * degToRad;

            double sinHalfLat = Math.Sin(dLat * 0.5);
            double sinHalfLon = Math.Sin(dLon * 0.5);
            double a = (sinHalfLat * sinHalfLat) +
                (Math.Cos(rLat1) * Math.Cos(rLat2) * sinHalfLon * sinHalfLon);
            double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            return earthRadiusKm * c;
        }

        internal static bool IsPointInCircle(double lat, double lon, double centerLat, double centerLon, double radiusKm)
        {
            return radiusKm >= 0.0 && ComputeHaversineKm(lat, lon, centerLat, centerLon) <= radiusKm + 1e-6;
        }

        internal static HashSet<string> ExtractReferencedIdentifiers(string rawReferences)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(rawReferences))
            {
                return ids;
            }

            string[] tokens = rawReferences.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                int firstComma = token.IndexOf(',');
                if (firstComma >= 0)
                {
                    int secondComma = token.IndexOf(',', firstComma + 1);
                    string idPart = secondComma > firstComma
                        ? token.Substring(firstComma + 1, secondComma - firstComma - 1).Trim()
                        : token.Substring(firstComma + 1).Trim();
                    if (!string.IsNullOrWhiteSpace(idPart))
                    {
                        ids.Add(idPart);
                    }
                }
                else
                {
                    ids.Add(token.Trim());
                }
            }

            return ids;
        }

        private static int CertaintyRank(string certainty)
        {
            if (string.IsNullOrWhiteSpace(certainty))
            {
                return 1;
            }

            return certainty.Trim().ToLowerInvariant() switch
            {
                "observed" => 3,
                "likely" => 2,
                "possible" => 0,
                "unlikely" => -1,
                _ => 1
            };
        }

        private static string NormalizeCertainty(string certainty)
        {
            if (string.IsNullOrWhiteSpace(certainty))
            {
                return string.Empty;
            }

            return certainty.Trim().ToLowerInvariant() switch
            {
                "observed" => "Observed",
                "likely" => "Likely",
                "possible" => "Possible",
                "unlikely" => "Unlikely",
                _ => string.Empty
            };
        }

        internal enum GeometryMatchState
        {
            NoGeometry,
            Inside,
            Outside
        }

        internal sealed record GeoRegionInfo(
            string CountryCode,
            string IsoLvl4,
            string IsoLvl6,
            string State,
            string County,
            string Province,
            string Municipality,
            string City);

        internal sealed record MeteoAlarmMatch(
            WeatherAlertLevel Level,
            string EventName,
            string AreaDesc,
            string GeocodeId,
            string? CapUrl,
            DateTimeOffset? Onset,
            DateTimeOffset? Expires,
            string Certainty = "",
            string Identifier = "");

        internal sealed record PayloadFetchOutcome(
            string? Body,
            AlertSourceStatus Status,
            DateTimeOffset? LastSuccessfulFetchUtc);

        private sealed record CachedPayload(DateTimeOffset FetchedUtc, string Body);
    }
}
