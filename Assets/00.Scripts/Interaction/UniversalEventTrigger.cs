using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Scene trigger for dialog, quest and cutscene events: the first branch whose condition matches runs its actions in order.</summary>
public class UniversalEventTrigger : MonoBehaviour, IInteractable
{
    public enum Activation { OnInteract, OnEnter }

    public enum Condition
    {
        Always,                 // always matches
        QuestNotStarted,        // quest is not available, on progress, or completed
        QuestAvailable,         // quest is on the board / offered
        QuestOnProgress,            // quest has been accepted
        QuestObjectiveComplete, // quest is on progress AND the given objective is complete
        QuestCompleted          // quest is done
    }

    public enum ActionType
    {
        PlayDialog,
        PlayCutscene,
        OpenQuestBoard,
        AddAvailableQuest,
        AddOnProgressQuest,
        CompleteObjective,
        ReachLocation
    }

    [Serializable]
    public class EventAction
    {
        public ActionType type;
        public Dialog dialog;
        public PlayableDirector director;
        [Tooltip("Virtual cam active during the cutscene. Leave empty to keep gameplay follow.")]
        public Transform cutsceneVCam;
        [Tooltip("Leave empty to use the scene's QuestBoard, found automatically (including inactive).")]
        public QuestBoard questBoard;
        public QuestData quest;
        public string objectiveId;
        [Tooltip("Matched against the targetId of on-progress quests' ReachLocation objectives.")]
        public string locationId;
    }

    [Serializable]
    public class EventBranch
    {
        public Condition condition;
        [Tooltip("Quest whose state is checked. Ignored for Always.")]
        public QuestData quest;
        [Tooltip("Objective id to check. Only used with QuestObjectiveComplete.")]
        public string objectiveId;
        public EventAction[] actions;
    }

    public Activation activation = Activation.OnInteract;
    [Tooltip("Once a branch has run, the trigger never fires again (until the scene reloads).")]
    public bool triggerOnce;
    [Tooltip("Checked top to bottom — the first branch whose condition matches runs its actions in order.")]
    public EventBranch[] branches;

    bool _fired;
    bool _running;

    void IInteractable.Interact(GameObject interactor)
    {
        if (activation == Activation.OnInteract) Fire();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (activation == Activation.OnEnter && other.CompareTag("Player")) Fire();
    }

    public void Fire()
    {
        if (_running || (triggerOnce && _fired)) return;

        EventBranch branch = SelectBranch();
        if (branch == null) return;

        _fired = true;
        StartCoroutine(Run(branch.actions));
    }

    EventBranch SelectBranch()
    {
        if (branches != null)
            foreach (var branch in branches)
                if (branch != null && Matches(branch))
                    return branch;
        return null;
    }

    static bool Matches(EventBranch branch)
    {
        if (branch.condition == Condition.Always) return true;

        var qm = QuestManager.Instance;
        if (qm == null || branch.quest == null) return false;

        switch (branch.condition)
        {
            case Condition.QuestAvailable: return qm.IsAvailable(branch.quest);
            case Condition.QuestOnProgress:    return qm.IsOnProgress(branch.quest);
            case Condition.QuestObjectiveComplete: return qm.IsObjectiveComplete(branch.quest, branch.objectiveId);
            case Condition.QuestCompleted: return qm.IsCompleted(branch.quest);
            case Condition.QuestNotStarted:
                return !qm.IsAvailable(branch.quest) && !qm.IsOnProgress(branch.quest) && !qm.IsCompleted(branch.quest);
            default: return false;
        }
    }

    IEnumerator Run(EventAction[] actions)
    {
        _running = true;

        if (actions != null)
            foreach (var action in actions)
            {
                if (action == null) continue;
                switch (action.type)
                {
                    case ActionType.PlayDialog:     yield return StartCoroutine(PlayDialog(action.dialog)); break;
                    case ActionType.PlayCutscene:   yield return StartCoroutine(PlayCutscene(action.director, action.cutsceneVCam)); break;
                    case ActionType.OpenQuestBoard: yield return StartCoroutine(OpenQuestBoard(action.questBoard)); break;
                    default:                        ApplyQuestAction(action); break;
                }
            }

        _running = false;
    }

    IEnumerator PlayDialog(Dialog dialog)
    {
        DialogSystem ds = DialogSystem.Instance;
        if (ds == null) yield break;

        if (ds.IsOpen) yield return new WaitUntil(() => !ds.IsOpen);
        ds.Open(dialog);
        yield return new WaitUntil(() => !ds.IsOpen);
    }

    IEnumerator PlayCutscene(PlayableDirector director, Transform vcam)
    {
        PlayerControl player = PlayerControl.Instance;
        if (player != null)
            player.SetCutsceneMode(true);

        if (vcam != null && CameraFollow2D.Instance != null)
            CameraFollow2D.Instance.StartCutscene(vcam);

        if (HUDDisplay.Instance != null)
            HUDDisplay.Instance.HUDONOFF(false);

        if (director != null)
        {
            bool finished = false;
            Action<PlayableDirector> onStopped = _ => finished = true;
            director.stopped += onStopped;
            director.Play();
            yield return new WaitUntil(() => finished);
            director.stopped -= onStopped;
        }

        if (vcam != null && CameraFollow2D.Instance != null)
            CameraFollow2D.Instance.EndCutscene();

        if (HUDDisplay.Instance != null)
            HUDDisplay.Instance.HUDONOFF(true);

        if (ScreenFader.Instance != null) ScreenFader.Instance.ResetAll();

        if (player != null)
            player.SetCutsceneMode(false);
    }

    IEnumerator OpenQuestBoard(QuestBoard board)
    {
        // The board is usually inactive (Show(false) disables its GameObject), so the search must include inactive objects.
        if (board == null)
            board = FindFirstObjectByType<QuestBoard>(FindObjectsInactive.Include);
        if (board == null)
        {
            Debug.LogWarning("[UniversalEventTrigger] No QuestBoard found in the scene.", this);
            yield break;
        }

        board.Show(true);
        if (PlayerControl.Instance != null) PlayerControl.Instance.SetInputEnabled(false);
        yield return new WaitUntil(() => !board.gameObject.activeSelf);
    }

    static void ApplyQuestAction(EventAction action)
    {
        var qm = QuestManager.Instance;
        if (qm == null) return;

        switch (action.type)
        {
            case ActionType.AddAvailableQuest: qm.AddAvailableQuest(action.quest); break;
            case ActionType.AddOnProgressQuest:    qm.AddQuest(action.quest); break;
            case ActionType.CompleteObjective: qm.CompleteObjective(action.quest, action.objectiveId); break;
            case ActionType.ReachLocation:     qm.ReportLocationReached(action.locationId); break;
        }
    }
}
