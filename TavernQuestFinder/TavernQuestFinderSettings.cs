using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace TavernQuestFinder
{
    public sealed class TavernQuestFinderSettings : AttributeGlobalSettings<TavernQuestFinderSettings>
    {
        public override string Id => "TavernQuestFinder";
        public override string DisplayName => "酒馆任务查找";
        public override string FolderName => "TavernQuestFinder";
        public override string FormatType => "xml";

        [SettingPropertyBool(
            "隐藏原版酒馆找任务选项",
            RequireRestart = false,
            HintText = "隐藏原版的‘是否知道谁有任务’选项；本 Mod 的特定任务查找选项仍会保留。",
            Order = 0)]
        public bool HideVanillaQuestSearch { get; set; } = true;
    }
}
