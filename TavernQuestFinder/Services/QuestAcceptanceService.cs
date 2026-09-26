using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using TavernQuestFinder.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.Localization;

namespace TavernQuestFinder.Services
{
    internal static class QuestAcceptanceService
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        private static readonly MethodInfo? CheckPreconditionsMethod =
            typeof(IssueBase).GetMethod(
                "CheckPreconditions",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        private static readonly FieldInfo? OfferDialogFlowField =
            typeof(QuestBase).GetField(
                "OfferDialogFlow",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        private static readonly FieldInfo? DialogFlowLinesField =
            typeof(DialogFlow).GetField(
                "Lines",
                BindingFlags.Instance |
                BindingFlags.NonPublic);

        public static bool CheckPlayerCanTakeIssue(
            IssueBase issue,
            Hero issueOwner,
            out string? failureReason)
        {
            failureReason = null;

            if (CheckPreconditionsMethod == null)
            {
                failureReason =
                    "IssueBase.CheckPreconditions could not be found in this game version.";

                TqfLog.Warn(failureReason);
                return false;
            }

            object?[] arguments =
            {
                issueOwner,
                null
            };

            try
            {
                object? result =
                    CheckPreconditionsMethod.Invoke(
                        issue,
                        arguments);

                if (result is bool canTake && canTake)
                {
                    TqfLog.Info(
                        $"CheckPreconditions PASS " +
                        $"| Issue={issue.GetType().Name} " +
                        $"| Owner={issueOwner.Name}");

                    return true;
                }

                failureReason =
                    (arguments[1] as TextObject)?.ToString() ??
                    "The vanilla issue preconditions rejected this quest.";

                TqfLog.Info(
                    $"CheckPreconditions FAIL " +
                    $"| Issue={issue.GetType().Name} " +
                    $"| Owner={issueOwner.Name} " +
                    $"| Reason={failureReason}");

                return false;
            }
            catch (TargetInvocationException exception)
            {
                Exception inner =
                    exception.InnerException ?? exception;

                failureReason =
                    $"Vanilla issue precondition check failed: " +
                    $"{inner.GetType().Name}: {inner.Message}";

                TqfLog.Error(
                    "CheckPreconditions invocation failed.",
                    inner);

                return false;
            }
            catch (Exception exception)
            {
                failureReason =
                    $"Vanilla issue precondition check failed: " +
                    $"{exception.GetType().Name}: {exception.Message}";

                TqfLog.Error(
                    "CheckPreconditions failed.",
                    exception);

                return false;
            }
        }

        public static bool IsQuestRegistered(
            QuestBase quest)
        {
            QuestManager? questManager =
                Campaign.Current?.QuestManager;

            if (questManager == null)
            {
                return false;
            }

            return questManager.Quests.Any(
                candidate =>
                    ReferenceEquals(candidate, quest));
        }

        public static void ExecuteVanillaQuestAcceptance(
            QuestBase quest)
        {
            if (IsQuestRegistered(quest))
            {
                TqfLog.Info(
                    $"Quest already registered in QuestManager; " +
                    $"acceptance consequence skipped " +
                    $"| Quest={quest.GetType().FullName}");

                return;
            }

            if (OfferDialogFlowField == null ||
                DialogFlowLinesField == null)
            {
                throw new InvalidOperationException(
                    "Unable to inspect QuestBase.OfferDialogFlow in this game version.");
            }

            object? offerDialogFlow =
                OfferDialogFlowField.GetValue(quest);

            if (offerDialogFlow == null)
            {
                throw new InvalidOperationException(
                    $"Quest {quest.GetType().FullName} has no OfferDialogFlow.");
            }

            if (DialogFlowLinesField.GetValue(offerDialogFlow)
                is not IEnumerable lines)
            {
                throw new InvalidOperationException(
                    $"Quest {quest.GetType().FullName} has an unreadable OfferDialogFlow.");
            }

            int executedConsequences = 0;

            TqfLog.Info(
                $"Executing vanilla acceptance flow " +
                $"| Quest={quest.GetType().FullName} " +
                $"| IsOngoingBefore={quest.IsOngoing} " +
                $"| RegisteredBefore={IsQuestRegistered(quest)} " +
                $"| JournalEntriesBefore={quest.JournalEntries.Count}");

            foreach (object? line in lines)
            {
                if (line == null)
                {
                    continue;
                }

                FieldInfo? consequenceField =
                    line.GetType().GetField(
                        "ConsequenceDelegate",
                        InstanceFlags);

                if (consequenceField?.GetValue(line)
                    is not Delegate consequence)
                {
                    continue;
                }

                TqfLog.Info(
                    $"Acceptance consequence " +
                    $"| Method={consequence.Method.DeclaringType?.FullName}.{consequence.Method.Name}");

                try
                {
                    consequence.DynamicInvoke();
                    executedConsequences++;
                }
                catch (TargetInvocationException exception)
                {
                    Exception inner =
                        exception.InnerException ?? exception;

                    TqfLog.Error(
                        "Acceptance consequence threw an exception.",
                        inner);

                    throw inner;
                }

                bool registered =
                    IsQuestRegistered(quest);

                TqfLog.Info(
                    $"Acceptance consequence completed " +
                    $"| Registered={registered} " +
                    $"| IsOngoing={quest.IsOngoing} " +
                    $"| JournalEntries={quest.JournalEntries.Count} " +
                    $"| Tasks={quest.TaskList.Count}");

                // In vanilla, the consequence that calls StartQuest() also
                // performs the task-specific setup in the same delegate.
                // Once that delegate returns and the Quest is present in
                // QuestManager, do not execute unrelated later branch
                // consequences from the same DialogFlow.
                if (registered)
                {
                    break;
                }
            }

            bool isRegistered =
                IsQuestRegistered(quest);

            TqfLog.Info(
                $"Acceptance flow finished " +
                $"| Quest={quest.GetType().Name} " +
                $"| Consequences={executedConsequences} " +
                $"| Registered={isRegistered} " +
                $"| IsOngoing={quest.IsOngoing} " +
                $"| JournalEntries={quest.JournalEntries.Count} " +
                $"| Tasks={quest.TaskList.Count}");

            if (!isRegistered)
            {
                throw new InvalidOperationException(
                    executedConsequences == 0
                        ? $"Quest {quest.GetType().FullName} has no acceptance consequence in its OfferDialogFlow."
                        : $"Quest {quest.GetType().FullName} acceptance consequences did not register the quest in QuestManager.");
            }
        }
    }
}
