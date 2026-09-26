using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Issues;

namespace TavernQuestFinder.Services
{
    internal enum IssueHandlingMode
    {
        DirectVanillaAcceptance,
        RequiresNpcConversation
    }

    internal static class IssueCompatibilityService
    {
        private static readonly HashSet<string> TrustedOfficialAssemblyNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "TaleWorlds.CampaignSystem",
                "SandBox",
                "StoryMode",
                "NavalDLC"
            };

        public static IssueHandlingMode GetHandlingMode(Type issueType)
        {
            return IsTrustedVanillaIssue(issueType)
                ? IssueHandlingMode.DirectVanillaAcceptance
                : IssueHandlingMode.RequiresNpcConversation;
        }

        public static bool IsTrustedVanillaIssue(Type issueType)
        {
            string? assemblyName = issueType.Assembly.GetName().Name;

            return !string.IsNullOrWhiteSpace(assemblyName) &&
                   TrustedOfficialAssemblyNames.Contains(assemblyName);
        }

        public static bool CanCheckOwner(
            Campaign campaign,
            Hero hero,
            IssueHandlingMode handlingMode)
        {
            if (hero == Hero.MainHero ||
                hero.Issue != null ||
                !hero.CanHaveCampaignIssues())
            {
                return false;
            }

            if (handlingMode ==
                IssueHandlingMode.RequiresNpcConversation)
            {
                // Third-party Issue behaviors define their own giver rules
                // through OnCheckForIssue / PotentialIssueData.
                return true;
            }

            if (hero.IsNotable)
            {
                return hero.CurrentSettlement != null;
            }

            if (!hero.IsLord ||
                hero.Clan == null ||
                hero.Clan == Clan.PlayerClan ||
                hero.Age < campaign.Models.AgeModel.HeroComesOfAge)
            {
                return false;
            }

            return hero.IsActive || hero.IsPrisoner;
        }
    }
}
