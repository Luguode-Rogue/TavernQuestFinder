using HarmonyLib;
using TavernQuestFinder.CampaignBehaviors;
using TavernQuestFinder.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace TavernQuestFinder
{
    public sealed class SubModule : MBSubModuleBase
    {
        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            TqfLog.Initialize();
            TqfLog.Info("SubModule loaded.");

            new Harmony("tavern.quest.finder").PatchAll();
        }

        protected override void OnGameStart(
            Game game,
            IGameStarter gameStarter)
        {
            base.OnGameStart(game, gameStarter);

            TqfLog.Info(
                $"OnGameStart | GameType={game.GameType?.GetType().FullName ?? "<null>"} " +
                $"| Starter={gameStarter?.GetType().FullName ?? "<null>"}");

            if (game.GameType is Campaign &&
                gameStarter is CampaignGameStarter campaignStarter)
            {
                campaignStarter.AddBehavior(
                    new TavernQuestFinderBehavior());

                TqfLog.Info(
                    "TavernQuestFinderBehavior registered.");
            }
            else
            {
                TqfLog.Info(
                    "Campaign behavior registration skipped: not a campaign game.");
            }
        }
    }
}
