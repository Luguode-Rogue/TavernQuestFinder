using System;
using System.Collections.Generic;
using TavernQuestFinder.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace TavernQuestFinder.Services
{
    internal static class IssueAvailabilityService
    {
        // Campaign-map distance used to define the player's local area.
        // Current settlement givers are naturally distance 0.
        public const float NearbyRadius = 50f;

        private static readonly object Sync = new object();

        private static Dictionary<Type, IssueAvailabilityInfo> _availability =
            new Dictionary<Type, IssueAvailabilityInfo>();

        public static void Refresh()
        {
            Campaign? campaign = Campaign.Current;
            IssueManager? issueManager = campaign?.IssueManager;
            MobileParty? mainParty = MobileParty.MainParty;

            if (campaign == null ||
                issueManager == null ||
                mainParty == null)
            {
                lock (Sync)
                {
                    _availability =
                        new Dictionary<Type, IssueAvailabilityInfo>();
                }

                TqfLog.Warn(
                    "Availability refresh skipped: " +
                    "Campaign/IssueManager/MainParty unavailable.");
                return;
            }

            CampaignVec2 playerPosition = mainParty.Position;
            Settlement? playerSettlement =
                Settlement.CurrentSettlement ??
                mainParty.CurrentSettlement;

            TqfLog.Info(
                $"Player location captured " +
                $"| Settlement={playerSettlement?.Name?.ToString() ?? "<world map>"} " +
                $"| Position=({playerPosition.X:F2},{playerPosition.Y:F2}) " +
                $"| NearbyRadius={NearbyRadius:F1}");

            var available =
                new Dictionary<Type, IssueAvailabilityInfo>();

            int checkedHeroes = 0;
            int validPotentialIssues = 0;
            int checkErrors = 0;
            int loggedErrors = 0;
            int unknownPositionHeroes = 0;

            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == Hero.MainHero ||
                    hero.Issue != null ||
                    !hero.CanHaveCampaignIssues())
                {
                    continue;
                }

                checkedHeroes++;

                List<PotentialIssueData> potentialIssues;

                try
                {
                    potentialIssues =
                        issueManager.CheckForIssues(hero);
                }
                catch (Exception exception)
                {
                    checkErrors++;

                    if (loggedErrors < 5)
                    {
                        loggedErrors++;
                        TqfLog.Warn(
                            $"Availability CheckForIssues failed " +
                            $"| Hero={hero.Name} " +
                            $"| Error={exception.GetType().Name}: {exception.Message}");
                    }

                    continue;
                }

                if (!TryGetDistance(
                        playerPosition,
                        hero,
                        out float distance))
                {
                    unknownPositionHeroes++;
                    continue;
                }

                foreach (PotentialIssueData potentialIssue in potentialIssues)
                {
                    if (!potentialIssue.IsValid)
                    {
                        continue;
                    }

                    IssueHandlingMode mode =
                        IssueCompatibilityService.GetHandlingMode(
                            potentialIssue.IssueType);

                    if (!IssueCompatibilityService.CanCheckOwner(
                            campaign,
                            hero,
                            mode))
                    {
                        continue;
                    }

                    if (issueManager.HasIssueCoolDown(
                            potentialIssue.IssueType,
                            hero))
                    {
                        continue;
                    }

                    validPotentialIssues++;

                    if (!available.TryGetValue(
                            potentialIssue.IssueType,
                            out IssueAvailabilityInfo? info))
                    {
                        info = new IssueAvailabilityInfo();
                        available.Add(
                            potentialIssue.IssueType,
                            info);
                    }

                    info.Consider(hero, distance);
                }
            }

            lock (Sync)
            {
                _availability = available;
            }

            int nearbyTypes = 0;
            int remoteOnlyTypes = 0;

            foreach (KeyValuePair<Type, IssueAvailabilityInfo> pair
                     in available)
            {
                IssueAvailabilityInfo info = pair.Value;

                if (info.HasNearby)
                {
                    nearbyTypes++;
                }
                else if (info.HasRemote)
                {
                    remoteOnlyTypes++;
                }

                TqfLog.Info(
                    $"Available issue by distance " +
                    $"| Type={pair.Key.FullName} " +
                    $"| Display=\"{IssueCatalog.GetDisplayName(pair.Key)}\" " +
                    $"| NearbyOwner={info.NearbyOwner?.Name?.ToString() ?? "<none>"} " +
                    $"| NearbyDistance={FormatDistance(info.NearbyDistance)} " +
                    $"| RemoteOwner={info.RemoteOwner?.Name?.ToString() ?? "<none>"} " +
                    $"| RemoteDistance={FormatDistance(info.RemoteDistance)}");
            }

            TqfLog.Info(
                $"Availability refreshed " +
                $"| CheckedHeroes={checkedHeroes} " +
                $"| ValidPotentialIssues={validPotentialIssues} " +
                $"| AvailableTypes={available.Count} " +
                $"| NearbyTypes={nearbyTypes} " +
                $"| RemoteOnlyTypes={remoteOnlyTypes} " +
                $"| UnknownPositionHeroes={unknownPositionHeroes} " +
                $"| CheckErrors={checkErrors}");
        }

        public static bool IsAvailable(Type issueType)
        {
            lock (Sync)
            {
                return _availability.TryGetValue(
                           issueType,
                           out IssueAvailabilityInfo? info) &&
                       (info.HasNearby || info.HasRemote);
            }
        }

        public static bool IsNearbyAvailable(Type issueType)
        {
            lock (Sync)
            {
                return _availability.TryGetValue(
                           issueType,
                           out IssueAvailabilityInfo? info) &&
                       info.HasNearby;
            }
        }

        public static bool IsRemoteOnlyAvailable(Type issueType)
        {
            lock (Sync)
            {
                return _availability.TryGetValue(
                           issueType,
                           out IssueAvailabilityInfo? info) &&
                       !info.HasNearby &&
                       info.HasRemote;
            }
        }

        public static bool HasAnyAvailable(
            IEnumerable<Type> issueTypes)
        {
            lock (Sync)
            {
                foreach (Type issueType in issueTypes)
                {
                    if (_availability.TryGetValue(
                            issueType,
                            out IssueAvailabilityInfo? info) &&
                        (info.HasNearby || info.HasRemote))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static Hero? GetPreferredOwner(
            Type issueType,
            IssueDistanceScope distanceScope)
        {
            lock (Sync)
            {
                if (!_availability.TryGetValue(
                        issueType,
                        out IssueAvailabilityInfo? info))
                {
                    return null;
                }

                return distanceScope ==
                       IssueDistanceScope.NearbyOnly
                    ? info.NearbyOwner
                    : info.RemoteOwner;
            }
        }

        public static float? GetPreferredDistance(
            Type issueType,
            IssueDistanceScope distanceScope)
        {
            lock (Sync)
            {
                if (!_availability.TryGetValue(
                        issueType,
                        out IssueAvailabilityInfo? info))
                {
                    return null;
                }

                float distance =
                    distanceScope ==
                    IssueDistanceScope.NearbyOnly
                        ? info.NearbyDistance
                        : info.RemoteDistance;

                return float.IsPositiveInfinity(distance)
                    ? (float?)null
                    : distance;
            }
        }

        public static bool TryGetDistanceFromPlayer(
            Hero hero,
            out float distance)
        {
            MobileParty? mainParty = MobileParty.MainParty;

            if (mainParty == null)
            {
                distance = float.PositiveInfinity;
                return false;
            }

            return TryGetDistance(
                mainParty.Position,
                hero,
                out distance);
        }

        public static bool MatchesDistanceScope(
            Hero hero,
            IssueDistanceScope distanceScope)
        {
            if (!TryGetDistanceFromPlayer(
                    hero,
                    out float distance))
            {
                return false;
            }

            return distanceScope ==
                   IssueDistanceScope.NearbyOnly
                ? distance <= NearbyRadius
                : distance > NearbyRadius;
        }

        private static bool TryGetDistance(
            CampaignVec2 playerPosition,
            Hero hero,
            out float distance)
        {
            CampaignVec2 heroPosition =
                hero.GetCampaignPosition();

            if (heroPosition == CampaignVec2.Invalid)
            {
                distance = float.PositiveInfinity;
                return false;
            }

            distance =
                playerPosition.Distance(heroPosition);

            return !float.IsNaN(distance) &&
                   !float.IsInfinity(distance);
        }

        private static string FormatDistance(float distance)
        {
            return float.IsPositiveInfinity(distance)
                ? "<none>"
                : distance.ToString("F1");
        }

        private sealed class IssueAvailabilityInfo
        {
            public Hero? NearbyOwner { get; private set; }

            public float NearbyDistance { get; private set; } =
                float.PositiveInfinity;

            public Hero? RemoteOwner { get; private set; }

            public float RemoteDistance { get; private set; } =
                float.PositiveInfinity;

            public bool HasNearby => NearbyOwner != null;

            public bool HasRemote => RemoteOwner != null;

            public void Consider(
                Hero hero,
                float distance)
            {
                if (distance <= NearbyRadius)
                {
                    if (distance < NearbyDistance)
                    {
                        NearbyOwner = hero;
                        NearbyDistance = distance;
                    }

                    return;
                }

                if (distance < RemoteDistance)
                {
                    RemoteOwner = hero;
                    RemoteDistance = distance;
                }
            }
        }
    }

    internal enum IssueDistanceScope
    {
        NearbyOnly,
        RemoteOnly
    }
}
