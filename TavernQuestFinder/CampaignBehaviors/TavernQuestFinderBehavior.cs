using System;
using System.Collections.Generic;
using Helpers;
using TavernQuestFinder.Logging;
using TavernQuestFinder.Services;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;

namespace TavernQuestFinder.CampaignBehaviors
{
    internal sealed class TavernQuestFinderBehavior : CampaignBehaviorBase
    {
        private const int IssueEntriesPerPage = 15;

        private IReadOnlyList<Type> _allIssueTypes = Array.Empty<Type>();
        private IReadOnlyList<Type> _vanillaIssueTypes = Array.Empty<Type>();
        private IReadOnlyList<Type> _externalIssueTypes = Array.Empty<Type>();
        private IssueGenerationResult? _lastGenerationResult;
        private Type? _pendingRemoteIssueType;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(
                this,
                OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        private void OnSessionLaunched(
            CampaignGameStarter campaignGameStarter)
        {
            TqfLog.Info("Campaign session launched; building Issue catalog.");

            _allIssueTypes = IssueCatalog.GetAllIssueTypes();

            _vanillaIssueTypes =
                IssueCatalog.GetVanillaIssueTypes(_allIssueTypes);

            _externalIssueTypes =
                IssueCatalog.GetExternalIssueTypes(_allIssueTypes);

            TqfLog.Info(
                $"Issue groups ready | All={_allIssueTypes.Count} " +
                $"| Vanilla={_vanillaIssueTypes.Count} " +
                $"| External={_externalIssueTypes.Count} " +
                $"| EntriesPerPage={IssueEntriesPerPage} " +
                $"| NearbyRadius={IssueAvailabilityService.NearbyRadius:F1}");

            AddDialogs(campaignGameStarter);

            TqfLog.Info("Tavern dialog tree registered.");
        }

        private void AddDialogs(
            CampaignGameStarter campaignGameStarter)
        {
            campaignGameStarter.AddPlayerLine(
                "tqf_tavernkeeper_find_specific_issue",
                "tavernkeeper_talk",
                "tqf_issue_intro",
                "{=!}我想接一种特定的活。你这里能直接帮我安排吗？",
                null,
                RefreshAvailability,
                110,
                null,
                null);

            campaignGameStarter.AddDialogLine(
                "tqf_tavernkeeper_no_available_issue",
                "tqf_issue_intro",
                "tavernkeeper_pretalk",
                "{=!}我刚问了一圈，附近和远处现在都没有符合条件、能安排给你的差事。",
                HasNoAvailableIssues,
                null,
                110,
                null);

            campaignGameStarter.AddDialogLine(
                "tqf_tavernkeeper_issue_intro",
                "tqf_issue_intro",
                "tqf_issue_group_select",
                "{=!}可以。我会先替你找附近的差事；只有附近没有时，才会问你要不要接远处的。",
                HasAnyAvailableIssues,
                null,
                100,
                null);

            AddGroupSelectionDialogs(
                campaignGameStarter,
                "vanilla",
                "{=!}原版任务",
                _vanillaIssueTypes);

            AddGroupSelectionDialogs(
                campaignGameStarter,
                "external",
                "{=!}Mod / 扩展任务",
                _externalIssueTypes);

            AddPagedIssueDialogs(
                campaignGameStarter,
                "vanilla",
                _vanillaIssueTypes);

            AddPagedIssueDialogs(
                campaignGameStarter,
                "external",
                _externalIssueTypes);

            campaignGameStarter.AddPlayerLine(
                "tqf_group_cancel",
                "tqf_issue_group_select",
                "tavernkeeper_pretalk",
                "{=!}算了，我暂时不接。",
                null,
                null,
                0,
                null,
                null);

            AddRemoteConfirmationDialogs(
                campaignGameStarter);

            AddResultDialogs(campaignGameStarter);
        }

        private void AddGroupSelectionDialogs(
            CampaignGameStarter campaignGameStarter,
            string groupId,
            string displayText,
            IReadOnlyList<Type> issueTypes)
        {
            int pageCount = GetPageCount(issueTypes);

            for (int targetPage = 0;
                 targetPage < pageCount;
                 targetPage++)
            {
                int capturedPage = targetPage;

                campaignGameStarter.AddPlayerLine(
                    $"tqf_group_{groupId}_{capturedPage:D2}",
                    "tqf_issue_group_select",
                    GetPageStateToken(
                        groupId,
                        capturedPage),
                    displayText,
                    () => IsFirstAvailablePage(
                        issueTypes,
                        capturedPage),
                    null,
                    100,
                    null,
                    null);
            }
        }

        private void AddPagedIssueDialogs(
            CampaignGameStarter campaignGameStarter,
            string groupId,
            IReadOnlyList<Type> issueTypes)
        {
            if (issueTypes.Count == 0)
            {
                TqfLog.Info(
                    $"Menu group skipped | Group={groupId} | Count=0");
                return;
            }

            int pageCount = GetPageCount(issueTypes);

            TqfLog.Info(
                $"Menu group registered | Group={groupId} " +
                $"| Issues={issueTypes.Count} | Pages={pageCount}");

            for (int pageIndex = 0;
                 pageIndex < pageCount;
                 pageIndex++)
            {
                int capturedPage = pageIndex;

                string pageStateToken =
                    GetPageStateToken(groupId, capturedPage);

                string pageOptionsToken =
                    GetPageOptionsToken(groupId, capturedPage);

                string pagePrompt =
                    groupId == "vanilla"
                        ? $"{{=!}}这些是当前能找到的原版差事。第 {capturedPage + 1}/{pageCount} 页。"
                        : $"{{=!}}这些是当前能找到委托人的扩展差事。第 {capturedPage + 1}/{pageCount} 页。";

                campaignGameStarter.AddDialogLine(
                    $"tqf_{groupId}_page_intro_{capturedPage:D2}",
                    pageStateToken,
                    pageOptionsToken,
                    pagePrompt,
                    null,
                    null,
                    100,
                    null);

                int firstIndex =
                    capturedPage * IssueEntriesPerPage;

                int lastExclusive =
                    Math.Min(
                        firstIndex + IssueEntriesPerPage,
                        issueTypes.Count);

                TqfLog.Info(
                    $"Menu page registered | Group={groupId} " +
                    $"| Page={capturedPage + 1}/{pageCount} " +
                    $"| StaticEntries={lastExclusive - firstIndex}");

                for (int index = firstIndex;
                     index < lastExclusive;
                     index++)
                {
                    Type issueType = issueTypes[index];
                    string displayName =
                        IssueCatalog.GetDisplayName(issueType);

                    campaignGameStarter.AddPlayerLine(
                        $"tqf_{groupId}_issue_near_{index:D3}",
                        pageOptionsToken,
                        "tqf_issue_result",
                        "{=!}" + displayName,
                        () => IssueAvailabilityService.IsNearbyAvailable(
                            issueType),
                        () => SelectNearbyIssue(issueType),
                        100,
                        null,
                        null);

                    campaignGameStarter.AddPlayerLine(
                        $"tqf_{groupId}_issue_remote_{index:D3}",
                        pageOptionsToken,
                        "tqf_remote_issue_confirm",
                        "{=!}" + displayName + "（远距离）",
                        () => IssueAvailabilityService.IsRemoteOnlyAvailable(
                            issueType),
                        () => PrepareRemoteIssue(issueType),
                        100,
                        null,
                        null);
                }

                for (int targetPage = 0;
                     targetPage < pageCount;
                     targetPage++)
                {
                    if (targetPage == capturedPage)
                    {
                        continue;
                    }

                    int capturedTargetPage = targetPage;

                    if (capturedTargetPage < capturedPage)
                    {
                        campaignGameStarter.AddPlayerLine(
                            $"tqf_{groupId}_prev_{capturedPage:D2}_{capturedTargetPage:D2}",
                            pageOptionsToken,
                            GetPageStateToken(
                                groupId,
                                capturedTargetPage),
                            $"{{=!}}上一页 ({capturedTargetPage + 1}/{pageCount})",
                            () => IsNearestAvailablePage(
                                issueTypes,
                                capturedPage,
                                capturedTargetPage,
                                false),
                            null,
                            20,
                            null,
                            null);
                    }
                    else
                    {
                        campaignGameStarter.AddPlayerLine(
                            $"tqf_{groupId}_next_{capturedPage:D2}_{capturedTargetPage:D2}",
                            pageOptionsToken,
                            GetPageStateToken(
                                groupId,
                                capturedTargetPage),
                            $"{{=!}}下一页 ({capturedTargetPage + 1}/{pageCount})",
                            () => IsNearestAvailablePage(
                                issueTypes,
                                capturedPage,
                                capturedTargetPage,
                                true),
                            null,
                            20,
                            null,
                            null);
                    }
                }

                campaignGameStarter.AddPlayerLine(
                    $"tqf_{groupId}_back_{capturedPage:D2}",
                    pageOptionsToken,
                    "tqf_issue_intro",
                    "{=!}返回任务分类",
                    null,
                    RefreshAvailability,
                    10,
                    null,
                    null);
            }
        }

        private void AddRemoteConfirmationDialogs(
            CampaignGameStarter campaignGameStarter)
        {
            campaignGameStarter.AddDialogLine(
                "tqf_remote_issue_confirm_dialog",
                "tqf_remote_issue_confirm",
                "tqf_remote_issue_confirm_options",
                "{=!}附近没有这类差事。不过更远的地方有一份：委托人是 {TQF_REMOTE_GIVER.LINK}，目前在 {TQF_REMOTE_LOCATION} 一带，距离大约 {TQF_REMOTE_DISTANCE}。你还要接吗？",
                HasPendingRemoteIssue,
                null,
                100,
                null);

            campaignGameStarter.AddPlayerLine(
                "tqf_remote_issue_accept",
                "tqf_remote_issue_confirm_options",
                "tqf_issue_result",
                "{=!}接，我愿意接远处的差事。",
                HasPendingRemoteIssue,
                AcceptPendingRemoteIssue,
                100,
                null,
                null);

            campaignGameStarter.AddPlayerLine(
                "tqf_remote_issue_decline",
                "tqf_remote_issue_confirm_options",
                "tqf_issue_intro",
                "{=!}不了，还是看看别的。",
                HasPendingRemoteIssue,
                DeclinePendingRemoteIssue,
                90,
                null,
                null);
        }

        private void AddResultDialogs(
            CampaignGameStarter campaignGameStarter)
        {
            campaignGameStarter.AddDialogLine(
                "tqf_issue_result_direct",
                "tqf_issue_result",
                "tavernkeeper_pretalk",
                "{=!}安排好了。这件差事已经算你接下了。委托人是 {TQF_ISSUE_GIVER.LINK}；之后如果任务需要交付或当面说明，再去找{?TQF_ISSUE_GIVER.GENDER}她{?}他{\\?}。",
                IsLastGenerationDirectlyAccepted,
                null,
                100,
                null);

            campaignGameStarter.AddDialogLine(
                "tqf_issue_result_npc_required",
                "tqf_issue_result",
                "tavernkeeper_pretalk",
                "{=!}这种差事的具体规矩我不清楚，不过 {TQF_ISSUE_GIVER.LINK} 正在找人。你得亲自去找{?TQF_ISSUE_GIVER.GENDER}她{?}他{\\?}谈谈。",
                IsLastGenerationNpcConversationRequired,
                null,
                100,
                null);

            campaignGameStarter.AddDialogLine(
                "tqf_issue_result_failure",
                "tqf_issue_result",
                "tavernkeeper_pretalk",
                "{=!}刚才的情况有变化，这件差事现在没法安排。",
                IsLastGenerationFailed,
                ShowFailureReason,
                100,
                null);
        }

        private void RefreshAvailability()
        {
            _pendingRemoteIssueType = null;

            TqfLog.Info(
                "Refreshing available Issues before showing TavernQuestFinder menu.");

            IssueAvailabilityService.Refresh();

            TqfLog.Info(
                $"Availability menu state " +
                $"| VanillaAvailable={CountAvailable(_vanillaIssueTypes)} " +
                $"| ExternalAvailable={CountAvailable(_externalIssueTypes)} " +
                $"| NearbyRadius={IssueAvailabilityService.NearbyRadius:F1}");
        }

        private void SelectNearbyIssue(Type issueType)
        {
            TqfLog.Info(
                $"Menu route | Type={issueType.FullName} " +
                $"| Route=NearbyDirectAttempt");

            GenerateSelectedIssue(
                issueType,
                IssueDistanceScope.NearbyOnly);
        }

        private void PrepareRemoteIssue(Type issueType)
        {
            _pendingRemoteIssueType = issueType;

            Hero? remoteOwner =
                IssueAvailabilityService.GetPreferredOwner(
                    issueType,
                    IssueDistanceScope.RemoteOnly);

            float? distance =
                IssueAvailabilityService.GetPreferredDistance(
                    issueType,
                    IssueDistanceScope.RemoteOnly);

            if (remoteOwner != null)
            {
                StringHelpers.SetCharacterProperties(
                    "TQF_REMOTE_GIVER",
                    remoteOwner.CharacterObject,
                    null,
                    false);
            }

            string location =
                remoteOwner?.CurrentSettlement?.Name?.ToString() ??
                "远方";

            MBTextManager.SetTextVariable(
                "TQF_REMOTE_LOCATION",
                location,
                false);

            MBTextManager.SetTextVariable(
                "TQF_REMOTE_DISTANCE",
                distance.HasValue
                    ? $"{distance.Value:F1} 个地图单位"
                    : "未知",
                false);

            TqfLog.Info(
                $"Remote confirmation offered " +
                $"| Type={issueType.FullName} " +
                $"| Display=\"{IssueCatalog.GetDisplayName(issueType)}\" " +
                $"| Owner={remoteOwner?.Name?.ToString() ?? "<none>"} " +
                $"| Settlement={remoteOwner?.CurrentSettlement?.Name?.ToString() ?? "<none>"} " +
                $"| Distance={(distance.HasValue ? distance.Value.ToString("F1") : "<unknown>")} " +
                $"| NearbyRadius={IssueAvailabilityService.NearbyRadius:F1}");
        }

        private void AcceptPendingRemoteIssue()
        {
            Type? issueType =
                _pendingRemoteIssueType;

            _pendingRemoteIssueType = null;

            if (issueType == null)
            {
                _lastGenerationResult =
                    IssueGenerationResult.Failed(
                        "Remote issue confirmation state was lost.");

                TqfLog.Warn(
                    "Remote confirmation accepted but no pending Issue type existed.");
                return;
            }

            TqfLog.Info(
                $"Remote confirmation accepted " +
                $"| Type={issueType.FullName}");

            GenerateSelectedIssue(
                issueType,
                IssueDistanceScope.RemoteOnly);
        }

        private void DeclinePendingRemoteIssue()
        {
            Type? issueType =
                _pendingRemoteIssueType;

            TqfLog.Info(
                $"Remote confirmation declined " +
                $"| Type={issueType?.FullName ?? "<none>"}");

            _pendingRemoteIssueType = null;
            RefreshAvailability();
        }

        private void GenerateSelectedIssue(
            Type issueType,
            IssueDistanceScope distanceScope)
        {
            string displayName =
                IssueCatalog.GetDisplayName(issueType);

            TqfLog.Info(
                $"Menu selection | Display=\"{displayName}\" " +
                $"| Type={issueType.FullName} " +
                $"| DistanceScope={distanceScope}");

            _lastGenerationResult =
                IssueGenerationService.TryGenerate(
                    issueType,
                    distanceScope);

            TqfLog.Info(
                $"Menu selection result | Display=\"{displayName}\" " +
                $"| DistanceScope={distanceScope} " +
                $"| Status={_lastGenerationResult.Status} " +
                $"| Owner={_lastGenerationResult.IssueOwner?.Name?.ToString() ?? "<none>"} " +
                $"| Failure={_lastGenerationResult.FailureReason ?? "<none>"}");

            if (_lastGenerationResult.Success &&
                _lastGenerationResult.IssueOwner != null)
            {
                StringHelpers.SetCharacterProperties(
                    "TQF_ISSUE_GIVER",
                    _lastGenerationResult.IssueOwner.CharacterObject,
                    null,
                    false);

                MBTextManager.SetTextVariable(
                    "TQF_ISSUE_TYPE",
                    displayName,
                    false);
            }
        }

        private bool HasPendingRemoteIssue()
        {
            return _pendingRemoteIssueType != null;
        }

        private bool HasAnyAvailableIssues()
        {
            return IssueAvailabilityService.HasAnyAvailable(
                _allIssueTypes);
        }

        private bool HasNoAvailableIssues()
        {
            return !HasAnyAvailableIssues();
        }

        private bool IsLastGenerationDirectlyAccepted()
        {
            return _lastGenerationResult?.Status ==
                   IssueGenerationStatus.DirectlyAccepted;
        }

        private bool IsLastGenerationNpcConversationRequired()
        {
            return _lastGenerationResult?.Status ==
                   IssueGenerationStatus.RequiresNpcConversation;
        }

        private bool IsLastGenerationFailed()
        {
            return _lastGenerationResult?.Status ==
                   IssueGenerationStatus.Failed;
        }

        private void ShowFailureReason()
        {
            string reason =
                _lastGenerationResult?.FailureReason ??
                "Unknown issue generation failure.";

            TqfLog.Warn(
                $"Showing failure to player | Reason={reason}");

            InformationManager.DisplayMessage(
                new InformationMessage(
                    "[TavernQuestFinder] " + reason));
        }

        private static int GetPageCount(
            IReadOnlyList<Type> issueTypes)
        {
            return issueTypes.Count == 0
                ? 0
                : (issueTypes.Count + IssueEntriesPerPage - 1) /
                  IssueEntriesPerPage;
        }

        private static bool IsFirstAvailablePage(
            IReadOnlyList<Type> issueTypes,
            int pageIndex)
        {
            if (!HasAvailableOnPage(
                    issueTypes,
                    pageIndex))
            {
                return false;
            }

            for (int page = 0; page < pageIndex; page++)
            {
                if (HasAvailableOnPage(issueTypes, page))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNearestAvailablePage(
            IReadOnlyList<Type> issueTypes,
            int currentPage,
            int targetPage,
            bool forward)
        {
            if (!HasAvailableOnPage(
                    issueTypes,
                    targetPage))
            {
                return false;
            }

            if (forward)
            {
                if (targetPage <= currentPage)
                {
                    return false;
                }

                for (int page = currentPage + 1;
                     page < targetPage;
                     page++)
                {
                    if (HasAvailableOnPage(issueTypes, page))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (targetPage >= currentPage)
            {
                return false;
            }

            for (int page = currentPage - 1;
                 page > targetPage;
                 page--)
            {
                if (HasAvailableOnPage(issueTypes, page))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasAvailableOnPage(
            IReadOnlyList<Type> issueTypes,
            int pageIndex)
        {
            int firstIndex =
                pageIndex * IssueEntriesPerPage;

            if (firstIndex < 0 ||
                firstIndex >= issueTypes.Count)
            {
                return false;
            }

            int lastExclusive =
                Math.Min(
                    firstIndex + IssueEntriesPerPage,
                    issueTypes.Count);

            for (int index = firstIndex;
                 index < lastExclusive;
                 index++)
            {
                if (IssueAvailabilityService.IsAvailable(
                        issueTypes[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountAvailable(
            IReadOnlyList<Type> issueTypes)
        {
            int count = 0;

            foreach (Type issueType in issueTypes)
            {
                if (IssueAvailabilityService.IsAvailable(
                        issueType))
                {
                    count++;
                }
            }

            return count;
        }

        private static string GetPageStateToken(
            string groupId,
            int pageIndex)
        {
            return $"tqf_{groupId}_page_state_{pageIndex:D2}";
        }

        private static string GetPageOptionsToken(
            string groupId,
            int pageIndex)
        {
            return $"tqf_{groupId}_page_options_{pageIndex:D2}";
        }
    }
}
