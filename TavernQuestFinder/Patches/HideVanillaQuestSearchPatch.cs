using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;

namespace TavernQuestFinder.Patches
{
    /// <summary>
    /// 给原版酒馆找任务入口附加动态 MCM 条件，同时保留其余原版对话树。
    /// </summary>
    [HarmonyPatch(typeof(CampaignGameStarter), nameof(CampaignGameStarter.AddPlayerLine))]
    internal static class HideVanillaQuestSearchPatch
    {
        private const string VanillaQuestLineId = "tavernkeeper_talk_to_get_quest";

        private static void Prefix(
            string id,
            ref ConversationSentence.OnConditionDelegate conditionDelegate)
        {
            if (id != VanillaQuestLineId)
                return;

            ConversationSentence.OnConditionDelegate originalCondition = conditionDelegate;
            conditionDelegate = () =>
            {
                TavernQuestFinderSettings settings = TavernQuestFinderSettings.Instance;
                return (settings == null || !settings.HideVanillaQuestSearch) &&
                       (originalCondition == null || originalCondition());
            };
        }
    }
}
