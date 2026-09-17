# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

A Unity 2D action-RPG prototype (per `README.md`): Unity **6000.0.62f1**, URP 2D render pipeline, new Input System. It experiments with core RPG systems — quests, dialog, skills, inventory/equipment, Timeline-driven cutscenes — on top of a Souls-like real-time combat/movement base (dodge, guard, bonfire checkpoints, blood-gauge magic, limb-slicing gore).

There is no CLI build/lint/test pipeline in this repo — it's developed entirely from the Unity Editor.

## Working in this repo

- **Open/run**: open the project root in Unity Hub with Editor version **6000.0.62f1**, then Play from the Editor. Scene boot order (`ProjectSettings/EditorBuildSettings.asset`): `MainMenu → Loading → Gameplay → Gameplay2`.
- **No assembly definitions**: all runtime scripts under `Assets/00.Scripts` compile into the single default `Assembly-CSharp` (and its `-Editor` counterpart for `Editor/` subfolders) — there's no asmdef splitting anywhere in `Assets`. Any script edit recompiles the whole project.
- **Tests**: `com.unity.test-framework` is a listed package dependency, but there are currently no test files anywhere under `Assets` — the Test Runner window (`Window > General > Test Runner`) has nothing to run yet. If tests are added later, they run the standard way (Editor Test Runner, or `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -quit` from the CLI).
- **No lint config** (no `.editorconfig`, no analyzer ruleset) — match surrounding code style by hand.
- **After creating new `.cs` files outside the Editor** (e.g. via an external tool), Unity generates their `.meta` files on next Editor focus — commit those alongside the script once they appear; a script without a `.meta` is invisible to Unity.
- Verifying a change generally means: open in the Editor, check the Console for compile errors, and enter Play mode to exercise the actual behavior — there's no automated way to confirm gameplay-affecting changes in this repo.

## Architecture

### Persistent manager bootstrap
Cross-scene singletons (`GameManager`, `SaveManager`, `QuestManager`, `TimeSystem` on `Assets/02.Prefabs/Manager.prefab`; `PlayerControl` and its satellite components on `Assets/02.Prefabs/Player.prefab`) are **not** loaded once from a boot scene — both prefabs are placed redundantly in every gameplay-reachable scene (`MainMenu`, `Gameplay`, `Gameplay2`). Each script does `DontDestroyOnLoad` + a duplicate-guard in `Awake` (`if (Instance != null && Instance != this) { Destroy(gameObject); return; }`), so whichever instance loads first survives and every later duplicate self-destroys. This lets Play mode be entered from any scene during development. When adding a new persistent singleton, follow this exact pattern and place it on the appropriate prefab rather than wiring it into one scene only.

### Scene transitions
`GameManager.LoadScene(...)` is the only entry point that should trigger a scene change (`SceneMover` doors, `SaveManager`'s checkpoint/load flows, main menu all call it — never call `SceneManager.LoadScene` directly). It stores the target scene/spawn position in the static `SceneLoader` bag, loads the dedicated `Loading` scene, and `LoadingScreenUI` (which lives only in that scene) does the actual async load + progress bar + fade before activating the target scene. `GameManager.OnSceneLoaded` (subscribed for the whole session) then applies the spawn override and optional entry-walk-in effect and flips `GameManager.State` back to `Playing`. `SceneMover` (`Assets/00.Scripts/Interaction/SceneMover.cs`) is the door/trigger component — `_interactOrEnter` picks between an interact-button press and automatic on-collision-enter triggering.

### Save system
`SaveManager` (`Assets/00.Scripts/Core/SaveManager.cs`) serializes one `GameSaveData` object to `Documents/gamesave.json` via `JsonUtility`. Every persisted subsystem follows the same **`Record<X>()`/`Apply<X>()` pair** called centrally from `Save()`/`SaveAtBonfire()`/`ApplyToPlayer()` — subsystems never serialize themselves. `JsonUtility` can't serialize dictionaries, so all save DTOs use flat parallel lists instead (e.g. `QuestManager.QuestProgressSave`, `PlayerEquipment.EquippedSaveEntry`). When adding a new persisted system, add a matching `Record`/`Apply` pair here rather than having the subsystem read/write the save file itself.

### ScriptableObject data + registry/id-lookup pattern
Static game data (`QuestData`, `Dialog`, `EquipmentData`, `SlashSkillLevelData`/`MagicSkillLevelData`) is authored as `[CreateAssetMenu]` ScriptableObject assets, each with a manually-assigned string/int id (`questId`, `dialogId`, `equipmentId`). Because save data and cross-references store these ids as plain strings/ints (not direct object references), the owning manager (`QuestManager._registry`, `PlayerEquipment._registry`, `DialogSystem.dialogRegistry`) keeps a **manually maintained array of every asset of that type**, resolved at runtime via a linear `FindById` scan. **A new asset must be added to the relevant registry array in the Inspector or it silently fails to resolve on save/load** — this has been the source of real bugs in this project (a quest working in-session but vanishing after a save/reload because it wasn't registered). `DialogSystem`'s registry auto-refreshes via `OnValidate`; the others do not.

Per-quest/per-equipment sub-data (objectives, plus-levels) is a plain `[Serializable]` C# class embedded as an array field on the owning asset — not its own ScriptableObject — since that data has no independent identity or reuse case (mirrors `SlashSkillLevelData`'s per-level struct-array pattern).

### Player system
`PlayerControl` (`Assets/00.Scripts/Player/PlayerControl.cs`, ~1800 lines) is the monolithic controller: movement/physics, jump (two-tier height via hold-duration, coyote time), dodge, guard, ladder climbing, combos, and all combat execution (quickdraw/rising/smashdown/body-slam/grab-throw) live here, reading stat fields that satellite components push into it. Horizontal velocity is only recomputed from live input while grounded (`IsGrounded()`-gated in `FixedUpdate`); airborne movement is untouched from the moment of takeoff, so jump/fall trajectories carry momentum rather than being live-steered.

Satellite components on the same GameObject, wired via `GetComponent` in `Awake`:
- **`PlayerBasicStatContainer`** — the seven base stats (`Attack/Defend/Health/Stamina/MoveSpeed/AttackSpeed/LuckPoint`), each split into a private base value and a private "plus" accumulator, exposed as `Base + Plus` computed properties. Nothing but `PlayerEquipment.SetEquipmentBonuses(...)` writes the plus accumulators.
- **`PlayerEquipment`** — flat equipped-item list (no slot types/capacity limit); sums every equipped `EquipmentData`'s bonus at its current plus-level and pushes the total into `PlayerBasicStatContainer`.
- **`PlayerSlashSkill`/`PlayerMagicSkill`** — each skill has a `[Min(0)] int xxxLevel` and an `ApplyXxxLevel(level)` method that pulls a row from the matching `*SkillLevelData` asset (array index = level) and copies every field onto its own mirrored fields; `PlayerSlashSkill` then `PushToPlayer()`s those onto `PlayerControl`'s identically-named fields (slash skills execute in `PlayerControl`), while magic skills execute directly inside `PlayerMagicSkill`. Damage scaling (`AttackPoint`/`LuckPoint` crit) is duplicated between `PlayerControl.ScaleOutgoingDamage` and `PlayerMagicSkill.ScaleOutgoingDamage` rather than shared.

### Quest system
`QuestData` holds a `QuestObjective[]` array; each objective has a `QuestObjectiveType` (`Manual`, `KillCount`, `CollectItem`, `ReachLocation`, `WinBattle`), a `targetId` whose meaning depends on the type, `prerequisiteObjectiveIds` for branching order, and `optional`/`completesQuest` flags for alternate-ending branches. `QuestManager` exposes one `Report*` method per objective type (`ReportKill`, `ReportItemQuantity`, `ReportLocationReached`, `ReportBattleResult`) that other systems call — kills/items are wired automatically via static events (`GameManager.OnEnemyKilledWithId`, `Inventory.OnAnyItemAdded`); `ReportBattleResult` exists as a forward-looking hook with no caller yet (no battle/tactics system exists in this project). A quest auto-completes when every non-optional objective is complete, or any single `completesQuest` objective completes.

### Dialog system
`DialogSystem` is a Blue-Archive-style portrait/typewriter dialog runner with click-to-advance, choices, and a Timeline integration (`Dialog/Timeline/DialogTrack.cs` + Signal-driven playback). Each `Dialog` asset can carry one `questAction` (`AddAvailable`/`AddActive`/`CompleteObjective`) applied to `QuestManager` when the dialog closes. `DialogTrigger` (on NPCs) branches which `Dialog` plays based on `DialogCondition` (quest state, including a specific objective's completion) — this is the only branching mechanism for dialog content.

### Enemy AI
Hand-rolled per-actor FSM: `StateMachine`/`BaseState` (`Enter`/`Perform`/`Exit`) plus `IMonsterCore`, a shared interface/contract for stats, attack patterns, and state-factory methods (`CreateAttackState()`, etc.) implemented by each concrete enemy (`Simple/`, `Small/`, `Middle/` subfolders under `Assets/00.Scripts/Enemy`). The limb-slicing/dismemberment mechanic (`PartSliceEnemy`, `EnemySliceable`, `SlicedHalf`) is layered on top and reports kills back through `GameManager.ReportEnemyKill`.

### Editor tooling
Authoring tools for the ScriptableObject data above are custom `EditorWindow`s (not `[CustomEditor]` inspectors) living in an `Editor/` subfolder next to the runtime scripts they edit, registered via `[MenuItem("Tools/...")]` — e.g. `Tools > Quest Editor`, `Tools > Equipment Editor`, `Tools > Slash/Magic Skill Level Editor`. New data-authoring UI should follow this same convention rather than a custom Inspector.

## Notable conventions

- Cross-system communication is either a static C# event on the owning singleton (`GameManager.OnEnemyKilled`, `QuestManager.OnQuestsChanged`, `Inventory.OnAnyItemAdded`) or a direct `Instance.Method()` call — there is no generic event bus.
- UI/dialog/quest strings are frequently authored in Korean directly in code and data assets — keep new player-facing strings consistent with that if extending those systems.
