using System;
using System.Collections.Generic;
using System.Linq;
using TavernQuestFinder.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;

namespace TavernQuestFinder.Services
{
    internal static class IssueGenerationService
    {
        public static IssueGenerationResult TryGenerate(
            Type selectedIssueType,
            IssueDistanceScope distanceScope)
        {
            Campaign? campaign = Campaign.Current;
            IssueManager? issueManager = campaign?.IssueManager;

            if (campaign == null || issueManager == null)
            {
                const string reason =
                    "Campaign or IssueManager is not available.";

                TqfLog.Warn(
                    $"Generation aborted | Type={selectedIssueType.FullName} " +
                    $"| Reason={reason}");

                return IssueGenerationResult.Failed(reason);
            }

            IssueHandlingMode handlingMode =
                IssueCompatibilityService.GetHandlingMode(
                    selectedIssueType);

            Hero? preferredOwner =
                IssueAvailabilityService.GetPreferredOwner(
                    selectedIssueType,
                    distanceScope);

            float? preferredDistance =
                IssueAvailabilityService.GetPreferredDistance(
                    selectedIssueType,
                    distanceScope);

            TqfLog.Info(
                $"Generation begin | Type={selectedIssueType.FullName} " +
                $"| Display=\"{IssueCatalog.GetDisplayName(selectedIssueType)}\" " +
                $"| Mode={handlingMode} " +
                $"| DistanceScope={distanceScope} " +
                $"| NearbyRadius={IssueAvailabilityService.NearbyRadius:F1} " +
                $"| PreferredOwner={preferredOwner?.Name?.ToString() ?? "<none>"} " +
                $"| PreferredDistance={FormatDistance(preferredDistance)}");

            string? lastNativeError = null;
            int checkedCandidates = 0;

            foreach (Hero hero in GetCandidates(
                         handlingMode,
                         preferredOwner,
                         distanceScope))
            {
                if (!IssueCompatibilityService.CanCheckOwner(
                        campaign,
                        hero,
                        handlingMode))
                {
                    continue;
                }

                if (!IssueAvailabilityService.TryGetDistanceFromPlayer(
                        hero,
                        out float candidateDistance))
                {
                    continue;
                }

                checkedCandidates++;

                List<PotentialIssueData> potentialIssues;

                try
                {
                    potentialIssues =
                        issueManager.CheckForIssues(hero);
                }
                catch (Exception exception)
                {
                    lastNativeError =
                        $"CheckForIssues failed for {hero.Name}: " +
                        $"{exception.GetType().Name}: {exception.Message}";

                    TqfLog.Warn(
                        $"Candidate check failed | Owner={hero.Name} " +
                        $"| Distance={candidateDistance:F1} " +
                        $"| Type={selectedIssueType.Name} " +
                        $"| Error={exception.GetType().Name}: {exception.Message}");

                    continue;
                }

                foreach (PotentialIssueData potentialIssue in potentialIssues)
                {
                    if (!potentialIssue.IsValid ||
                        potentialIssue.IssueType != selectedIssueType ||
                        issueManager.HasIssueCoolDown(
                            potentialIssue.IssueType,
                            hero))
                    {
                        continue;
                    }

                    TqfLog.Info(
                        $"Compatible giver found | Owner={hero.Name} " +
                        $"| Settlement={hero.CurrentSettlement?.Name?.ToString() ?? "<none>"} " +
                        $"| Distance={candidateDistance:F1} " +
                        $"| DistanceScope={distanceScope} " +
                        $"| Type={selectedIssueType.Name}");

                    PotentialIssueData selectedPotentialIssue =
                        potentialIssue;

                    IssueBase? createdIssue = null;

                    try
                    {
                        if (!issueManager.CreateNewIssue(
                                in selectedPotentialIssue,
                                hero))
                        {
                            TqfLog.Warn(
                                $"CreateNewIssue returned false " +
                                $"| Owner={hero.Name} " +
                                $"| Distance={candidateDistance:F1} " +
                                $"| Type={selectedIssueType.Name}");

                            continue;
                        }

                        createdIssue = hero.Issue;

                        if (createdIssue == null ||
                            createdIssue.GetType() != selectedIssueType)
                        {
                            lastNativeError =
                                $"IssueManager created an unexpected issue for {hero.Name}.";

                            TqfLog.Warn(lastNativeError);

                            CleanupUnacceptedIssue(
                                issueManager,
                                hero,
                                createdIssue);

                            continue;
                        }

                        TqfLog.Info(
                            $"Issue created | Owner={hero.Name} " +
                            $"| Distance={candidateDistance:F1} " +
                            $"| Issue={createdIssue.GetType().FullName} " +
                            $"| StringId={createdIssue.StringId}");

                        if (handlingMode ==
                            IssueHandlingMode.RequiresNpcConversation)
                        {
                            TqfLog.Info(
                                $"External issue fallback | Owner={hero.Name} " +
                                $"| Distance={candidateDistance:F1} " +
                                $"| Action=LeaveIssueOnNpcForNormalConversation");

                            return IssueGenerationResult.RequiresNpcConversation(
                                hero,
                                createdIssue);
                        }

                        if (!QuestAcceptanceService.CheckPlayerCanTakeIssue(
                                createdIssue,
                                hero,
                                out string? preconditionFailure))
                        {
                            lastNativeError =
                                preconditionFailure ??
                                "The vanilla issue preconditions rejected this quest.";

                            TqfLog.Info(
                                $"Vanilla preconditions rejected " +
                                $"| Owner={hero.Name} " +
                                $"| Distance={candidateDistance:F1} " +
                                $"| Reason={lastNativeError}");

                            CleanupUnacceptedIssue(
                                issueManager,
                                hero,
                                createdIssue);

                            continue;
                        }

                        TqfLog.Info(
                            $"Vanilla preconditions passed | Owner={hero.Name} " +
                            $"| Distance={candidateDistance:F1}");

                        if (!issueManager.StartIssueQuest(hero))
                        {
                            lastNativeError =
                                $"IssueManager.StartIssueQuest returned false for {hero.Name}.";

                            TqfLog.Warn(lastNativeError);
                            continue;
                        }

                        QuestBase? quest = createdIssue.IssueQuest;

                        if (quest == null)
                        {
                            lastNativeError =
                                $"Issue {createdIssue.GetType().Name} did not create an IssueQuest.";

                            TqfLog.Warn(lastNativeError);
                            continue;
                        }

                        TqfLog.Info(
                            $"IssueQuest created " +
                            $"| Quest={quest.GetType().FullName} " +
                            $"| IsOngoing={quest.IsOngoing} " +
                            $"| Registered={QuestAcceptanceService.IsQuestRegistered(quest)} " +
                            $"| JournalEntries={quest.JournalEntries.Count}");

                        QuestAcceptanceService.ExecuteVanillaQuestAcceptance(
                            quest);

                        bool isRegistered =
                            QuestAcceptanceService.IsQuestRegistered(
                                quest);

                        if (!isRegistered)
                        {
                            lastNativeError =
                                $"Quest {quest.GetType().Name} was not registered in QuestManager.";

                            TqfLog.Warn(lastNativeError);
                            continue;
                        }

                        TqfLog.Info(
                            $"Direct acceptance complete | Owner={hero.Name} " +
                            $"| Distance={candidateDistance:F1} " +
                            $"| DistanceScope={distanceScope} " +
                            $"| Quest={quest.GetType().Name} " +
                            $"| Registered={isRegistered} " +
                            $"| IsOngoing={quest.IsOngoing} " +
                            $"| JournalEntries={quest.JournalEntries.Count} " +
                            $"| Tasks={quest.TaskList.Count} " +
                            $"| QuestManagerCount={campaign.QuestManager.Quests.Count}");

                        return IssueGenerationResult.DirectlyAccepted(
                            hero,
                            createdIssue,
                            quest);
                    }
                    catch (Exception exception)
                    {
                        lastNativeError =
                            $"Issue generation failed for {hero.Name}: " +
                            $"{exception.GetType().Name}: {exception.Message}";

                        TqfLog.Error(
                            $"Generation exception | Owner={hero.Name} " +
                            $"| Distance={candidateDistance:F1} " +
                            $"| DistanceScope={distanceScope} " +
                            $"| Type={selectedIssueType.FullName}",
                            exception);

                        if (createdIssue?.IssueQuest != null)
                        {
                            return IssueGenerationResult.Failed(
                                lastNativeError);
                        }

                        CleanupUnacceptedIssue(
                            issueManager,
                            hero,
                            createdIssue);
                    }
                }
            }

            string scopeDescription =
                distanceScope ==
                IssueDistanceScope.NearbyOnly
                    ? $"within {IssueAvailabilityService.NearbyRadius:F0} map units"
                    : $"beyond {IssueAvailabilityService.NearbyRadius:F0} map units";

            string failureReason =
                lastNativeError ??
                $"No existing NPC {scopeDescription} currently satisfies the conditions for this issue type.";

            TqfLog.Info(
                $"Generation failed | Type={selectedIssueType.FullName} " +
                $"| DistanceScope={distanceScope} " +
                $"| CheckedCandidates={checkedCandidates} " +
                $"| Reason={failureReason}");

            return IssueGenerationResult.Failed(
                failureReason);
        }

        private static void CleanupUnacceptedIssue(
            IssueManager issueManager,
            Hero issueOwner,
            IssueBase? issue)
        {
            if (issue != null && issueOwner.Issue == issue)
            {
                TqfLog.Info(
                    $"Cleanup unaccepted issue | Owner={issueOwner.Name} " +
                    $"| Issue={issue.GetType().Name}");

                issueManager.DeactivateIssue(issue);
            }
        }

        private static IEnumerable<Hero> GetCandidates(
            IssueHandlingMode handlingMode,
            Hero? preferredOwner,
            IssueDistanceScope distanceScope)
        {
            IEnumerable<Hero> candidates =
                Hero.AllAliveHeroes;

            if (handlingMode ==
                IssueHandlingMode.DirectVanillaAcceptance)
            {
                candidates = candidates.Where(
                    hero => hero.IsNotable || hero.IsLord);
            }

            candidates = candidates
                .Where(hero =>
                    IssueAvailabilityService.MatchesDistanceScope(
                        hero,
                        distanceScope))
                .OrderBy(GetDistanceForSort)
                .ThenBy(
                    hero => hero.Name.ToString(),
                    StringComparer.OrdinalIgnoreCase);

            if (preferredOwner == null)
            {
                return candidates;
            }

            return new[] { preferredOwner }
                .Concat(
                    candidates.Where(
                        hero => hero != preferredOwner));
        }

        private static float GetDistanceForSort(Hero hero)
        {
            return IssueAvailabilityService.TryGetDistanceFromPlayer(
                hero,
                out float distance)
                ? distance
                : float.PositiveInfinity;
        }

        private static string FormatDistance(float? distance)
        {
            return distance.HasValue
                ? distance.Value.ToString("F1")
                : "<none>";
        }
    }

    internal enum IssueGenerationStatus
    {
        Failed,
        DirectlyAccepted,
        RequiresNpcConversation
    }

    internal sealed class IssueGenerationResult
    {
        private IssueGenerationResult(
            IssueGenerationStatus status,
            Hero? issueOwner,
            IssueBase? issue,
            QuestBase? quest,
            string? failureReason)
        {
            Status = status;
            IssueOwner = issueOwner;
            Issue = issue;
            Quest = quest;
            FailureReason = failureReason;
        }

        public IssueGenerationStatus Status { get; }

        public bool Success =>
            Status != IssueGenerationStatus.Failed;

        public Hero? IssueOwner { get; }

        public IssueBase? Issue { get; }

        public QuestBase? Quest { get; }

        public string? FailureReason { get; }

        public static IssueGenerationResult DirectlyAccepted(
            Hero issueOwner,
            IssueBase issue,
            QuestBase quest)
        {
            return new IssueGenerationResult(
                IssueGenerationStatus.DirectlyAccepted,
                issueOwner,
                issue,
                quest,
                null);
        }

        public static IssueGenerationResult RequiresNpcConversation(
            Hero issueOwner,
            IssueBase issue)
        {
            return new IssueGenerationResult(
                IssueGenerationStatus.RequiresNpcConversation,
                issueOwner,
                issue,
                null,
                null);
        }

        public static IssueGenerationResult Failed(string reason)
        {
            return new IssueGenerationResult(
                IssueGenerationStatus.Failed,
                null,
                null,
                null,
                reason);
        }
    }
}
