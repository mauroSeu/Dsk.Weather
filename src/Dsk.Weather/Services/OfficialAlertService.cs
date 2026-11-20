using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    public sealed record OfficialAlertFetchResult(
        bool ProviderAvailable,
        WeatherAlertInfo? Alert);

    public sealed class OfficialAlertService
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private static readonly TimeSpan FeedCacheTtl = TimeSpan.FromMinutes(15);
        private static readonly object CacheLock = new();

        private static readonly Dictionary<string, GeoRegionInfo> GeoCache = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, CachedPayload> PayloadCache = new(StringComparer.Ordinal);

        private static readonly Dictionary<string, string> CountryToMeteoAlarmSlug = new(StringComparer.OrdinalIgnoreCase)
        {
            ["it"] = "italy",
            ["nl"] = "netherlands",
            ["es"] = "spain",
            ["pt"] = "portugal",
            ["fr"] = "france",
            ["at"] = "austria",
            ["ch"] = "switzerland",
            ["be"] = "belgium",
            ["gb"] = "united-kingdom",
            ["ie"] = "ireland",
            ["pl"] = "poland",
            ["gr"] = "greece",
            ["hr"] = "croatia",
            ["si"] = "slovenia",
            ["cz"] = "czechia",
            ["sk"] = "slovakia",
            ["hu"] = "hungary",
            ["ro"] = "romania",
            ["bg"] = "bulgaria",
            ["no"] = "norway",
            ["se"] = "sweden",
            ["dk"] = "denmark",
            ["fi"] = "finland",
            ["lu"] = "luxembourg",
            ["mt"] = "malta",
            ["cy"] = "cyprus",
            ["ee"] = "estonia",
            ["lv"] = "latvia",
            ["lt"] = "lithuania",
            ["rs"] = "serbia",
            ["ba"] = "bosnia-herzegovina",
            ["me"] = "montenegro",
            ["mk"] = "republic-of-north-macedonia",
            ["md"] = "moldova",
            ["is"] = "iceland"
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
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Dsk.Weather/1.4 (https://github.com/mauroSeu/Dsk.Weather)");
            return client;
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
                    return new OfficialAlertFetchResult(false, null);
                }

                string cc = region.CountryCode.ToLowerInvariant();
                DateTime localNow = DateTime.Now;

                if (cc == "de")
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "de:{0:0.##},{1:0.##}", lat, lon);
                    string url = string.Format(
                        CultureInfo.InvariantCulture,
                        "https://api.brightsky.dev/alerts?lat={0:0.####}&lon={1:0.####}",
                        lat,
                        lon);
                    string? json = await GetCachedStringAsync(key, url, ct).ConfigureAwait(false);
                    if (json is null)
                    {
                        return new OfficialAlertFetchResult(false, null);
                    }

                    WeatherAlertInfo? dwdAlert = ParseBrightSkyJson(json, localNow, lang);
                    return new OfficialAlertFetchResult(true, dwdAlert);
                }

                if (cc == "us")
                {
                    string key = string.Format(CultureInfo.InvariantCulture, "us:{0:0.##},{1:0.##}", lat, lon);
                    string url = string.Format(
                        CultureInfo.InvariantCulture,
                        "https://api.weather.gov/alerts/active?point={0:0.####},{1:0.####}",
                        lat,
                        lon);
                    string? json = await GetCachedStringAsync(key, url, ct).ConfigureAwait(false);
                    if (json is null)
                    {
                        return new OfficialAlertFetchResult(false, null);
                    }

                    WeatherAlertInfo? nwsAlert = ParseNwsJson(json, localNow, lang);
                    return new OfficialAlertFetchResult(true, nwsAlert);
                }

                if (CountryToMeteoAlarmSlug.TryGetValue(cc, out string? slug))
                {
                    string feedKey = $"ma:{slug}";
                    string feedUrl = $"https://feeds.meteoalarm.org/feeds/meteoalarm-legacy-atom-{slug}";
                    string? atomXml = await GetCachedStringAsync(feedKey, feedUrl, ct).ConfigureAwait(false);
                    if (atomXml is null)
                    {
                        return new OfficialAlertFetchResult(false, null);
                    }

                    MeteoAlarmMatch? match = ParseMeteoAlarmAtom(atomXml, region, localNow);
                    if (match is null)
                    {
                        return new OfficialAlertFetchResult(true, null);
                    }

                    string? capXml = null;
                    if (!string.IsNullOrWhiteSpace(match.CapUrl))
                    {
                        capXml = await GetCachedStringAsync($"cap:{match.CapUrl}", match.CapUrl, ct).ConfigureAwait(false);
                    }

                    WeatherAlertInfo info = BuildAlertFromCap(capXml, match, localNow, lang);
                    return new OfficialAlertFetchResult(true, info);
                }

                return new OfficialAlertFetchResult(false, null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return new OfficialAlertFetchResult(false, null);
            }
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
                    GetJsonString(addr, "village")));

            lock (CacheLock)
            {
                GeoCache[coordKey] = info;
            }

            return info;
        }

        private static async Task<string?> GetCachedStringAsync(string cacheKey, string url, CancellationToken ct)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (CacheLock)
            {
                if (PayloadCache.TryGetValue(cacheKey, out CachedPayload? entry) &&
                    (now - entry.FetchedUtc) < FeedCacheTtl)
                {
                    return entry.Body;
                }
            }

            using HttpResponseMessage resp = await SharedHttpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            lock (CacheLock)
            {
                PayloadCache[cacheKey] = new CachedPayload(now, body);
            }

            return body;
        }

        internal static MeteoAlarmMatch? ParseMeteoAlarmAtom(string atomXml, GeoRegionInfo region, DateTime localNow)
        {
            if (string.IsNullOrWhiteSpace(atomXml) || region is null)
            {
                return null;
            }

            XDocument doc;
            try
            {
                doc = LoadXmlSafe(atomXml);
            }
            catch
            {
                return null;
            }

            DateTimeOffset nowRef = ToLocalDateTimeOffset(localNow);
            DateTimeOffset maxLookahead = nowRef.AddHours(18);
            string? expectedEmma = ResolveExpectedEmmaId(region);
            List<string> candidateAreas = BuildCandidateAreaTokens(region);

            MeteoAlarmMatch? best = null;
            int bestRank = -1;
            bool bestIsActiveNow = false;
            DateTimeOffset bestOnset = DateTimeOffset.MaxValue;

            foreach (XElement entry in doc.Descendants())
            {
                if (!string.Equals(entry.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string status = GetChildText(entry, "status");
                if (!string.IsNullOrWhiteSpace(status) &&
                    !string.Equals(status, "Actual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string msgType = GetChildText(entry, "message_type");
                if (string.Equals(msgType, "Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTimeOffset? expires = TryParseOffset(GetChildText(entry, "expires"));
                if (expires.HasValue && expires.Value <= nowRef)
                {
                    continue;
                }

                DateTimeOffset? onset = TryParseOffset(GetChildText(entry, "onset"))
                    ?? TryParseOffset(GetChildText(entry, "effective"));
                if (onset.HasValue && onset.Value > maxLookahead)
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

                if (!IsRegionMatch(expectedEmma, candidateAreas, geocode, areaDesc))
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
                bool isActiveNow = !onset.HasValue || onset.Value <= nowRef;
                DateTimeOffset sortOnset = onset ?? nowRef;

                bool isBetter = best is null
                    || rank > bestRank
                    || (rank == bestRank && isActiveNow && !bestIsActiveNow)
                    || (rank == bestRank && isActiveNow == bestIsActiveNow && sortOnset < bestOnset);

                if (isBetter)
                {
                    bestRank = rank;
                    bestIsActiveNow = isActiveNow;
                    bestOnset = sortOnset;
                    best = new MeteoAlarmMatch(level, ev, areaDesc, geocode, capUrl, onset, expires);
                }
            }

            return best;
        }

        internal static WeatherAlertInfo BuildAlertFromCap(
            string? capXml,
            MeteoAlarmMatch match,
            DateTime localNow,
            string lang)
        {
            string normalizedLang = LocalizationService.NormalizeLanguage(lang);
            string senderName = string.Empty;
            string rawDesc = string.Empty;
            string awarenessType = string.Empty;
            string eventLabel = match.EventName;
            string areaDesc = match.AreaDesc;
            DateTimeOffset? onset = match.Onset;
            DateTimeOffset? expires = match.Expires;

            if (!string.IsNullOrWhiteSpace(capXml))
            {
                try
                {
                    XDocument capDoc = LoadXmlSafe(capXml);
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

                        DateTimeOffset? capOnset = TryParseOffset(GetChildText(selectedInfo, "onset"))
                            ?? TryParseOffset(GetChildText(selectedInfo, "effective"));
                        if (capOnset.HasValue)
                        {
                            onset = capOnset;
                        }

                        DateTimeOffset? capExpires = TryParseOffset(GetChildText(selectedInfo, "expires"));
                        if (capExpires.HasValue)
                        {
                            expires = capExpires;
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
                Description: string.IsNullOrWhiteSpace(cleanDesc) ? areaDesc : cleanDesc);
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
            DateTimeOffset nowRef = ToLocalDateTimeOffset(localNow);
            DateTimeOffset maxLookahead = nowRef.AddHours(18);
            bool preferGerman = normalizedLang == "de";

            WeatherAlertLevel bestLevel = WeatherAlertLevel.None;
            int bestRank = -1;
            bool bestActive = false;
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
                if (expires.HasValue && expires.Value <= nowRef)
                {
                    continue;
                }

                DateTimeOffset? onset = TryParseOffset(GetJsonString(item, "onset"))
                    ?? TryParseOffset(GetJsonString(item, "effective"));
                if (onset.HasValue && onset.Value > maxLookahead)
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
                bool activeNow = !onset.HasValue || onset.Value <= nowRef;
                DateTimeOffset sortOnset = onset ?? nowRef;

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
            string evEn = GetJsonString(chosen, "event_en");
            string evDe = GetJsonString(chosen, "event_de");
            string desc = preferGerman
                ? GetFirstNonEmpty(GetJsonString(chosen, "description_de"), GetJsonString(chosen, "description_en"))
                : GetFirstNonEmpty(GetJsonString(chosen, "description_en"), GetJsonString(chosen, "description_de"));

            DateTimeOffset? bOnset = TryParseOffset(GetJsonString(chosen, "onset"))
                ?? TryParseOffset(GetJsonString(chosen, "effective"));
            DateTimeOffset? bExpires = TryParseOffset(GetJsonString(chosen, "expires"));

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
                Description: cleanDesc);
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
            DateTimeOffset nowRef = ToLocalDateTimeOffset(localNow);
            DateTimeOffset maxLookahead = nowRef.AddHours(18);

            WeatherAlertLevel bestLevel = WeatherAlertLevel.None;
            int bestRank = -1;
            bool bestActive = false;
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

                DateTimeOffset? expires = TryParseOffset(GetJsonString(props, "expires"));
                if (expires.HasValue && expires.Value <= nowRef)
                {
                    continue;
                }

                DateTimeOffset? onset = TryParseOffset(GetJsonString(props, "onset"))
                    ?? TryParseOffset(GetJsonString(props, "effective"));
                if (onset.HasValue && onset.Value > maxLookahead)
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
                bool activeNow = !onset.HasValue || onset.Value <= nowRef;
                DateTimeOffset sortOnset = onset ?? nowRef;

                if (bestProps is null
                    || rank > bestRank
                    || (rank == bestRank && activeNow && !bestActive)
                    || (rank == bestRank && activeNow == bestActive && sortOnset < bestOnset))
                {
                    bestLevel = level;
                    bestRank = rank;
                    bestActive = activeNow;
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
                Description: desc);
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
            if (lower.Contains("thunder") || lower.Contains("storm") || lower.Contains("gewitter") || lower.Contains("onweer") || lower.Contains("tormenta") || lower.Contains("temporale") || lower.Contains("trovoada"))
                return LocalizationService.T(lang, "alert_cap_storm");
            if (lower.Contains("wind") || lower.Contains("gust") || lower.Contains("sturm") || lower.Contains("viento") || lower.Contains("vento") || lower.Contains("böen"))
                return LocalizationService.T(lang, "alert_cap_wind");
            if (lower.Contains("frost") || lower.Contains("cold") || lower.Contains("ice") || lower.Contains("glätte") || lower.Contains("gelo") || lower.Contains("ijzel"))
                return LocalizationService.T(lang, "alert_cap_cold");
            if (lower.Contains("snow") || lower.Contains("schnee") || lower.Contains("nieve") || lower.Contains("neve") || lower.Contains("sneeuw"))
                return LocalizationService.T(lang, "alert_cap_snow_ice");
            if (lower.Contains("flood") || lower.Contains("alluv"))
                return LocalizationService.T(lang, "alert_cap_flood");
            if (lower.Contains("rain") || lower.Contains("regen") || lower.Contains("lluvia") || lower.Contains("pioggia") || lower.Contains("chuva") || lower.Contains("precipit"))
                return LocalizationService.T(lang, "alert_cap_rain");
            if (lower.Contains("fog") || lower.Contains("nebel") || lower.Contains("mist") || lower.Contains("niebla") || lower.Contains("nebbia") || lower.Contains("nevoeiro"))
                return LocalizationService.T(lang, "alert_cap_fog");
            if (lower.Contains("heat") || lower.Contains("hitze") || lower.Contains("calor") || lower.Contains("caldo") || lower.Contains("temperature"))
                return LocalizationService.T(lang, "alert_cap_heat");
            if (lower.Contains("coast") || lower.Contains("wave") || lower.Contains("mare"))
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
            DateTimeOffset? Expires);

        private sealed record CachedPayload(DateTimeOffset FetchedUtc, string Body);
    }
}
