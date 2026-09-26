# TavernQuestFinder

Bannerlord single-player mod that lets the player ask a tavern keeper for a specific Issue type.

## Menu design

The vanilla conversation list becomes difficult to use at roughly 30 visible choices. TavernQuestFinder therefore limits each task page to **15 Issue entries** and keeps navigation entries separate.

The selection flow is now:

```text
Tavern keeper
  -> task source
       -> Vanilla Issues
            -> page 1 / page 2 / ...
       -> Mod / external Issues
            -> page 1 / page 2 / ...
```

Each page contains at most 15 task entries plus previous/next/back navigation.

## Localized Issue names

Task names are no longer generated from C# class names.

For an `IssueBase` subtype, TavernQuestFinder inspects the type's `Title` getter for the localization-backed `TextObject` template already used by the game, for example:

```csharp
new TextObject("{=VpLzd69e}Escort Merchant Caravan", null)
```

That template is then resolved through `TextObject` using the player's current game language.

Some vanilla titles contain instance-specific placeholders such as `{SETTLEMENT}` or `{TARGET_HERO.NAME}`. There is no live Issue instance while the menu is being built, so unresolved instance placeholders are displayed as an ellipsis while the localized title body is retained.

If a third-party Issue does not expose a constant localization template in its `Title` getter, TavernQuestFinder falls back to a readable class-name-derived label.

## Quest handling

### Verified vanilla Issue

Issue types compiled into the same runtime assembly as `IssueBase` use the direct-accept path:

- vanilla giver eligibility;
- vanilla player preconditions;
- `IssueManager.StartIssueQuest`;
- original quest acceptance consequence;
- active Quest immediately.

### Unknown / third-party Issue

Issue types from other assemblies use the conservative path:

- create the Issue on a compatible existing NPC;
- do not start the Quest automatically;
- show the giver to the player;
- let the third-party mod's own conversation accept and initialize the quest.

Mods that do not use `IssueBase` / `PotentialIssueData` are outside the current discovery mechanism.

## Project layout

- `TavernQuestFinder/SubModule.cs`
- `TavernQuestFinder/CampaignBehaviors/TavernQuestFinderBehavior.cs`
- `TavernQuestFinder/Services/IssueCatalog.cs`
- `TavernQuestFinder/Services/IssueCompatibilityService.cs`
- `TavernQuestFinder/Services/IssueGenerationService.cs`
- `TavernQuestFinder/Services/QuestAcceptanceService.cs`

## Build

Set `BANNERLORD_GAME_DIR` to the Bannerlord installation directory and build using the same workflow as `bannerlord-newmod`.

## Logging

A lightweight runtime log is written to the installed Bannerlord module root:

```text
<Bannerlord>/Modules/TavernQuestFinder/TavernQuestFinder.log
```

The file is overwritten when the submodule is loaded for a new game process, so each launch starts with a clean log.

The logger deliberately does **not** fall back to the current working directory or the source/project directory. If it cannot prove that a path is the installed `Modules/TavernQuestFinder` directory containing `SubModule.xml`, file logging is disabled instead of writing to the wrong location.

Current log coverage includes module startup, campaign behavior registration, Issue discovery/localized-title resolution, menu/page registration, task selection, compatible giver discovery, Issue creation, vanilla precondition checks, direct acceptance consequences, external-issue fallback, cleanup, and failures.

## Availability filtering

The menu is refreshed when the player enters TavernQuestFinder.

The refresh scans existing living heroes once, calls the normal `IssueManager.CheckForIssues(hero)` pipeline, applies cooldown and owner eligibility rules, and records which Issue types currently have at least one legal giver.

Unavailable Issue types are hidden from the task pages. Navigation skips pages that contain no currently available tasks. Selecting a visible task still re-runs the vanilla checks before creation, so a world-state change between menu display and selection fails safely instead of forcing an invalid Issue.

Official game Issues are currently recognized from the known TaleWorlds assemblies `TaleWorlds.CampaignSystem`, `SandBox`, `StoryMode`, and `NavalDLC`. Other assemblies keep the conservative NPC-conversation fallback.

## Direct-accept registration check

Do not use `QuestBase.IsOngoing` to decide whether a freshly generated IssueQuest has actually started. In the current game source, `QuestStates.Ongoing` is enum value zero, so a newly constructed Quest reports `IsOngoing == true` before `StartQuest()` has ever been called.

TavernQuestFinder therefore treats membership in `Campaign.Current.QuestManager.Quests` as the authoritative start check. The original acceptance consequence is executed until it calls `StartQuest()`, which dispatches `OnQuestStarted` and causes `QuestManager.OnQuestStarted` to add the Quest to its active list.


## Generic context for dynamic titles

Vanilla localized Issue titles often contain runtime variables such as `{ISSUE_OWNER.NAME}`, `{TARGET_HERO.NAME}`, `{SETTLEMENT}`, or `{CLAN_NAME}`. The tavern menu is built before an Issue instance exists, so those variables previously disappeared and produced broken labels such as `的起义` or `想抓`.

The title resolver now supplies a generic menu-only context before calling the game's normal `TextObject` localization pipeline. Character variables use `某人`, location variables use `某地`, clan variables use `某家族`, and the lesser-noble name uses `某位贵族`. This affects only the tavern menu label; the actual generated Issue/Quest still uses its real owner, settlement, target and other runtime data.
