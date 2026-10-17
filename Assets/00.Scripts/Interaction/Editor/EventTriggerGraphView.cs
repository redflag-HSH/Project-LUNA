using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Node graph of UniversalEventTriggers (trigger → branches → actions) and the quests, dialogs and cutscenes they use.
/// Everything is rebuilt from the trigger data, so nodes can't be moved or wired by hand — edits go through node fields and buttons.</summary>
public class EventTriggerGraphView : GraphView
{
    internal static readonly Color FlowColor    = new(0.80f, 0.80f, 0.80f);
    internal static readonly Color PlaysColor   = new(0.40f, 0.65f, 1.00f);
    internal static readonly Color ChecksColor  = new(1.00f, 0.78f, 0.30f);
    internal static readonly Color ChangesColor = new(0.45f, 0.88f, 0.50f);
    internal static readonly Color ChoiceColor  = new(0.80f, 0.55f, 1.00f);

    static readonly Color TriggerHeader  = new(0.12f, 0.34f, 0.38f);
    static readonly Color BranchHeader   = new(0.42f, 0.32f, 0.09f);
    static readonly Color DialogHeader   = new(0.16f, 0.28f, 0.50f);
    static readonly Color CutsceneHeader = new(0.32f, 0.20f, 0.45f);
    static readonly Color QuestHeader    = new(0.16f, 0.38f, 0.20f);
    static readonly Color BoardHeader    = new(0.38f, 0.26f, 0.14f);
    static readonly Color WarningColor   = new(1.00f, 0.70f, 0.25f);
    static readonly Color InfoColor      = new(0.78f, 0.78f, 0.78f);

    const float FlowWidth = 260f, RefWidth = 250f, GapX = 50f, RowGap = 14f, TriggerGap = 48f, RefColumnGap = 120f, LabelWidth = 84f;

    static readonly Regex MarkupTag = new(@"<[^>]*>");

    enum RefInput { PlayedBy, FromChoice, CheckedBy, ChangedBy }

    class Flow
    {
        public GraphNode trigger;
        public readonly List<(GraphNode branch, List<GraphNode> actions)> rows = new();
    }

    readonly EventTriggerGraphWindow _window;
    readonly List<GraphNode> _nodes = new();
    readonly List<Flow> _flows = new();
    readonly Dictionary<Object, RefNode> _refNodes = new();
    readonly List<RefNode>[] _refColumns = { new(), new(), new() };   // cutscenes, dialogs, quests
    readonly List<(Port port, GraphNode from, Object target, RefInput kind)> _pendingRefs = new();
    readonly List<Object> _watched = new();
    readonly Dictionary<GraphNode, float> _placedY = new();
    HashSet<QuestData> _registry;
    List<QuestData> _allQuests;
    float _flowRight;
    bool _laidOut;

    /// <summary>Assets and scene objects shown as nodes; the window rebuilds when any of them changes.</summary>
    public IReadOnlyList<Object> WatchedObjects => _watched;

    public EventTriggerGraphView(EventTriggerGraphWindow window)
    {
        _window = window;
        SetupZoom(0.15f, 2f);
        this.AddManipulator(new ContentDragger());
        EnableBackgroundPan();

        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();
    }

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        var node = (evt.target as VisualElement)?.GetFirstOfType<GraphNode>();
        if (node != null) node.menu?.Invoke(evt.menu);
        else evt.menu.AppendAction("Frame All", _ => FrameAll());
    }

    // Left-drag on empty canvas pans (ContentDragger alone needs Alt or the middle button).
    void EnableBackgroundPan()
    {
        bool panning = false;
        Vector2 last = default;
        RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.button != 0 || e.altKey) return;
            if (!(e.target == this || e.target == contentViewContainer || e.target is GridBackground)) return;
            panning = true;
            last = e.position;
            this.CapturePointer(e.pointerId);
            e.StopPropagation();
        });
        RegisterCallback<PointerMoveEvent>(e =>
        {
            if (!panning) return;
            Vector2 delta = (Vector2)e.position - last;
            last = e.position;
            UpdateViewTransform(viewTransform.position + (Vector3)delta, viewTransform.scale);
        });
        RegisterCallback<PointerUpEvent>(e =>
        {
            if (!panning) return;
            panning = false;
            this.ReleasePointer(e.pointerId);
        });
    }

    // ── Build ─────────────────────────────────────────────────────────────────

    public void Build(List<UniversalEventTrigger> triggers, bool showReferences, bool frame)
    {
        foreach (var element in graphElements.ToList()) RemoveElement(element);
        _nodes.Clear();
        _flows.Clear();
        _refNodes.Clear();
        _pendingRefs.Clear();
        _watched.Clear();
        foreach (var column in _refColumns) column.Clear();
        _allQuests = null;
        _registry = LoadRegistry();

        bool multiScene = triggers.Select(t => t.gameObject.scene).Distinct().Count() > 1;
        foreach (var trigger in triggers) BuildFlow(trigger, showReferences, multiScene);

        // Creating a dialog or cutscene node queues its own outgoing references, so this list grows while it's walked.
        for (int i = 0; i < _pendingRefs.Count; i++)
        {
            var (port, from, target, kind) = _pendingRefs[i];
            RefNode node = GetOrCreateRefNode(target);
            Connect(port, node.Input(kind));
            if (!node.sources.Contains(from)) node.sources.Add(from);
        }

        foreach (var node in _nodes)
        {
            node.RefreshExpandedState();
            node.RefreshPorts();
        }
        ScheduleLayout(frame);
    }

    void BuildFlow(UniversalEventTrigger t, bool refs, bool multiScene)
    {
        var flow = new Flow { trigger = TriggerNode(t, multiScene) };
        _flows.Add(flow);

        var branches = t.branches ?? Array.Empty<UniversalEventTrigger.EventBranch>();
        bool belowAlways = false;
        for (int b = 0; b < branches.Length; b++)
        {
            var branch = branches[b];
            GraphNode branchNode = BranchNode(t, b, branch, belowAlways, refs);
            Connect(flow.trigger.flowOut, branchNode.flowIn);

            var actionNodes = new List<GraphNode>();
            var actions = branch.actions ?? Array.Empty<UniversalEventTrigger.EventAction>();
            Port previous = branchNode.flowOut;
            for (int a = 0; a < actions.Length; a++)
            {
                GraphNode actionNode = ActionNode(t, b, a, actions[a], actions.Length, refs);
                Connect(previous, actionNode.flowIn);
                previous = actionNode.flowOut;
                actionNodes.Add(actionNode);
            }
            flow.rows.Add((branchNode, actionNodes));

            if (branch.condition == UniversalEventTrigger.Condition.Always) belowAlways = true;
        }
    }

    GraphNode TriggerNode(UniversalEventTrigger t, bool multiScene)
    {
        GameObject go = t.gameObject;
        var node = Add(new GraphNode(go.name, TriggerHeader, FlowWidth));
        node.flowOut = node.AddPort(Direction.Output, "branches ▸", FlowColor, Port.Capacity.Multi);
        node.AddHeaderButton("Select", "Select this object in the Hierarchy", () => SelectObject(go));

        if (multiScene) node.AddInfo($"Scene: {go.scene.name}");
        if (!go.activeInHierarchy) node.AddInfo("Inactive in the Hierarchy");
        node.AddRow(EnumRow("Activation", t.activation, t, "activation"));
        node.AddRow(ToggleRow("Trigger Once", t.triggerOnce, t, "triggerOnce"));
        foreach (string warning in TriggerWarnings(t)) node.AddWarning(warning);

        int count = t.branches?.Length ?? 0;
        node.AddFooterButton("+ Branch", () => InsertBranch(t, count));
        node.menu = m =>
        {
            m.AppendAction("Add Branch", _ => InsertBranch(t, count));
            m.AppendAction("Select in Hierarchy", _ => SelectObject(go));
        };
        return node;
    }

    GraphNode BranchNode(UniversalEventTrigger t, int b, UniversalEventTrigger.EventBranch branch, bool unreachable, bool refs)
    {
        int count = t.branches.Length;
        int actionCount = branch.actions?.Length ?? 0;
        string path = BranchPath(b);

        var node = Add(new GraphNode($"Branch {b + 1}", BranchHeader, FlowWidth));
        node.flowIn = node.AddPort(Direction.Input, "◂ if", FlowColor, Port.Capacity.Single);
        if (actionCount > 0) node.flowOut = node.AddPort(Direction.Output, "then ▸", FlowColor, Port.Capacity.Single);
        node.AddHeaderButton("▲", "Check this branch earlier", () => MoveBranch(t, b, b - 1), b > 0);
        node.AddHeaderButton("▼", "Check this branch later", () => MoveBranch(t, b, b + 1), b < count - 1);
        node.AddHeaderButton("✕", "Delete this branch and its actions", () => DeleteBranch(t, b));

        if (unreachable)
        {
            node.style.opacity = 0.55f;
            node.AddWarning("An 'Always' branch above always matches first, so this branch never runs.");
        }

        node.AddRow(EnumRow("If", branch.condition, t, path + ".condition"));
        if (branch.condition != UniversalEventTrigger.Condition.Always)
        {
            node.AddRow(ObjectRow("Quest", typeof(QuestData), branch.quest, false, t, path + ".quest"));
            if (branch.condition == UniversalEventTrigger.Condition.QuestObjectiveComplete)
                node.AddRow(ObjectiveRow(branch.quest, branch.objectiveId, t, path + ".objectiveId"));

            if (branch.quest == null)
                node.AddWarning("Assign a quest, or this branch never matches.");
            else if (branch.condition == UniversalEventTrigger.Condition.QuestObjectiveComplete &&
                     ObjectiveProblem(branch.quest, branch.objectiveId) is string problem)
                node.AddWarning(problem + " This branch never matches.");

            if (refs && branch.quest != null)
                QueueRef(node.AddPort(Direction.Output, "checks ▸", ChecksColor, Port.Capacity.Single), node,
                    branch.quest, RefInput.CheckedBy);
        }
        if (actionCount == 0) node.AddInfo("No actions — matches but does nothing.");

        node.AddFooterButton("+ Action", () => InsertAction(t, b, actionCount));
        node.menu = m =>
        {
            m.AppendAction("Add Branch Above", _ => InsertBranch(t, b));
            m.AppendAction("Add Branch Below", _ => InsertBranch(t, b + 1));
            m.AppendAction("Add Action", _ => InsertAction(t, b, actionCount));
            m.AppendSeparator();
            m.AppendAction("Move Up", _ => MoveBranch(t, b, b - 1), Enabled(b > 0));
            m.AppendAction("Move Down", _ => MoveBranch(t, b, b + 1), Enabled(b < count - 1));
            m.AppendSeparator();
            m.AppendAction("Delete Branch", _ => DeleteBranch(t, b));
        };
        return node;
    }

    GraphNode ActionNode(UniversalEventTrigger t, int b, int a, UniversalEventTrigger.EventAction action, int count, bool refs)
    {
        string path = ActionPath(b, a);
        var node = Add(new GraphNode($"{a + 1}. {ObjectNames.NicifyVariableName(action.type.ToString())}",
            ActionHeader(action.type), FlowWidth));
        node.flowIn = node.AddPort(Direction.Input, "◂", FlowColor, Port.Capacity.Single);
        if (a < count - 1) node.flowOut = node.AddPort(Direction.Output, "next ▸", FlowColor, Port.Capacity.Single);
        node.AddHeaderButton("◀", "Run earlier", () => MoveAction(t, b, a, a - 1), a > 0);
        node.AddHeaderButton("▶", "Run later", () => MoveAction(t, b, a, a + 1), a < count - 1);
        node.AddHeaderButton("+", "Insert an action after this one", () => InsertAction(t, b, a + 1));
        node.AddHeaderButton("✕", "Delete this action", () => DeleteAction(t, b, a));

        node.AddRow(EnumRow("Type", action.type, t, path + ".type"));
        switch (action.type)
        {
            case UniversalEventTrigger.ActionType.PlayDialog:
                node.AddRow(ObjectRow("Dialog", typeof(Dialog), action.dialog, false, t, path + ".dialog"));
                if (action.dialog == null) node.AddWarning("Assign a dialog.");
                else if (refs) QueueRef(RefPort(node, "plays ▸", PlaysColor), node, action.dialog, RefInput.PlayedBy);
                break;

            case UniversalEventTrigger.ActionType.PlayCutscene:
                node.AddRow(ObjectRow("Director", typeof(PlayableDirector), action.director, true, t, path + ".director"));
                node.AddRow(ObjectRow("Cutscene Cam", typeof(Transform), action.cutsceneVCam, true, t, path + ".cutsceneVCam"));
                if (action.director == null) node.AddWarning("Assign the PlayableDirector to play.");
                else if (refs) QueueRef(RefPort(node, "plays ▸", PlaysColor), node, action.director, RefInput.PlayedBy);
                break;

            case UniversalEventTrigger.ActionType.OpenQuestBoard:
                node.AddRow(ObjectRow("Board", typeof(QuestBoard), action.questBoard, true, t, path + ".questBoard"));
                if (action.questBoard == null) node.AddInfo("Empty: uses the scene's QuestBoard.");
                break;

            case UniversalEventTrigger.ActionType.AddAvailableQuest:
            case UniversalEventTrigger.ActionType.AddOnProgressQuest:
                node.AddRow(ObjectRow("Quest", typeof(QuestData), action.quest, false, t, path + ".quest"));
                string verb = action.type == UniversalEventTrigger.ActionType.AddAvailableQuest ? "offers ▸" : "starts ▸";
                if (action.quest == null) node.AddWarning("Assign a quest.");
                else if (refs) QueueRef(RefPort(node, verb, ChangesColor), node, action.quest, RefInput.ChangedBy);
                break;

            case UniversalEventTrigger.ActionType.CompleteObjective:
                node.AddRow(ObjectRow("Quest", typeof(QuestData), action.quest, false, t, path + ".quest"));
                node.AddRow(ObjectiveRow(action.quest, action.objectiveId, t, path + ".objectiveId"));
                if (action.quest == null) node.AddWarning("Assign a quest.");
                else
                {
                    if (ObjectiveProblem(action.quest, action.objectiveId) is string problem) node.AddWarning(problem);
                    if (refs) QueueRef(RefPort(node, "completes ▸", ChangesColor), node, action.quest, RefInput.ChangedBy);
                }
                break;

            case UniversalEventTrigger.ActionType.ReachLocation:
                node.AddRow(TextRow("Location Id", action.locationId, t, path + ".locationId"));
                if (string.IsNullOrEmpty(action.locationId))
                {
                    node.AddWarning("Enter the location id that ReachLocation objectives target.");
                    break;
                }
                var reached = QuestsReaching(action.locationId).ToList();
                if (reached.Count == 0) node.AddWarning($"No quest has a ReachLocation objective for '{action.locationId}'.");
                else if (refs)
                {
                    Port port = RefPort(node, "reaches ▸", ChangesColor);
                    foreach (var quest in reached) QueueRef(port, node, quest, RefInput.ChangedBy);
                }
                break;
        }

        node.menu = m =>
        {
            m.AppendAction("Insert Action Before", _ => InsertAction(t, b, a));
            m.AppendAction("Insert Action After", _ => InsertAction(t, b, a + 1));
            m.AppendSeparator();
            m.AppendAction("Run Earlier", _ => MoveAction(t, b, a, a - 1), Enabled(a > 0));
            m.AppendAction("Run Later", _ => MoveAction(t, b, a, a + 1), Enabled(a < count - 1));
            m.AppendSeparator();
            m.AppendAction("Delete Action", _ => DeleteAction(t, b, a));
        };
        return node;
    }

    // ── Reference nodes ───────────────────────────────────────────────────────

    RefNode GetOrCreateRefNode(Object target)
    {
        if (_refNodes.TryGetValue(target, out RefNode node)) return node;

        int column;
        switch (target)
        {
            case PlayableDirector director: node = CutsceneNode(director); column = 0; break;
            case Dialog dialog:             node = DialogNode(dialog);     column = 1; break;
            default:                        node = QuestNode((QuestData)target); column = 2; break;
        }
        _refNodes[target] = node;
        _refColumns[column].Add(node);
        _watched.Add(target);
        return node;
    }

    RefNode QuestNode(QuestData quest)
    {
        var node = Add(new RefNode($"Quest · {(string.IsNullOrWhiteSpace(quest.questName) ? quest.name : quest.questName)}", QuestHeader));
        node.AddHeaderButton("Select", "Select this quest asset", () => SelectObject(quest));
        node.AddInfo($"id: {quest.questId}");
        if (quest.objectives != null)
            foreach (var objective in quest.objectives)
                node.AddInfo($"• {objective.objectiveId}  ({objective.type}" +
                             (objective.optional ? ", optional" : "") + (objective.completesQuest ? ", ends quest" : "") + ")");
        if (_registry != null && !_registry.Contains(quest))
            node.AddWarning("Not in QuestManager's registry, so it won't load back from a save.");
        node.menu = m => m.AppendAction("Select Asset", _ => SelectObject(quest));
        return node;
    }

    RefNode DialogNode(Dialog dialog)
    {
        var node = Add(new RefNode($"Dialog · {dialog.name}", DialogHeader));
        node.AddHeaderButton("Select", "Select this dialog asset", () => SelectObject(dialog));

        int lineCount = dialog.lines?.Length ?? 0;
        node.AddInfo($"id {dialog.dialogId} · {lineCount} line{(lineCount == 1 ? "" : "s")}");
        DialogLine first = dialog.lines?.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.text));
        if (first != null) node.AddInfo(Excerpt(first));

        if (dialog.questAction != DialogQuestAction.None)
        {
            string endAction = "On end: " + ObjectNames.NicifyVariableName(dialog.questAction.ToString());
            if (dialog.questAction == DialogQuestAction.CompleteObjective) endAction += $" '{dialog.questObjectiveId}'";
            node.AddInfo(endAction);
            if (dialog.quest == null) node.AddWarning("The end action has no quest, so it does nothing.");
            else QueueRef(RefPort(node, "on end ▸", ChangesColor), node, dialog.quest, RefInput.ChangedBy);
        }

        var next = (dialog.lines ?? Array.Empty<DialogLine>())
            .Where(l => l.choices != null).SelectMany(l => l.choices)
            .Select(c => c.nextDialog).Where(d => d != null).Distinct().ToList();
        if (next.Count > 0)
        {
            Port port = RefPort(node, "choice ▸", ChoiceColor);
            foreach (var target in next) QueueRef(port, node, target, RefInput.FromChoice);
        }

        node.menu = m => m.AppendAction("Select Asset", _ => SelectObject(dialog));
        return node;
    }

    RefNode CutsceneNode(PlayableDirector director)
    {
        var node = Add(new RefNode($"Cutscene · {director.gameObject.name}", CutsceneHeader));
        node.AddHeaderButton("Select", "Select the director in the Hierarchy", () => SelectObject(director.gameObject));

        if (director.playableAsset == null)
            node.AddWarning("No timeline assigned, so nothing plays.");
        else
            node.AddInfo($"Timeline: {director.playableAsset.name}");

        if (director.playableAsset is TimelineAsset timeline)
        {
            _watched.Add(timeline);
            var dialogs = new List<Dialog>();
            foreach (var track in timeline.GetOutputTracks())
            {
                if (!(track is DialogTrack)) continue;
                foreach (var clip in track.GetClips())
                {
                    if (!(clip.asset is DialogClip dialogClip)) continue;
                    _watched.Add(dialogClip);
                    if (dialogClip.dialog != null && !dialogs.Contains(dialogClip.dialog)) dialogs.Add(dialogClip.dialog);
                }
            }
            if (dialogs.Count > 0)
            {
                node.AddInfo($"{dialogs.Count} dialog clip{(dialogs.Count == 1 ? "" : "s")} on its timeline");
                Port port = RefPort(node, "dialog clips ▸", PlaysColor);
                foreach (var dialog in dialogs) QueueRef(port, node, dialog, RefInput.PlayedBy);
            }
        }

        node.menu = m => m.AppendAction("Select in Hierarchy", _ => SelectObject(director.gameObject));
        return node;
    }

    void QueueRef(Port port, GraphNode from, Object target, RefInput kind) => _pendingRefs.Add((port, from, target, kind));

    static Port RefPort(GraphNode node, string name, Color color) =>
        node.AddPort(Direction.Output, name, color, Port.Capacity.Multi);

    HashSet<QuestData> LoadRegistry()
    {
        QuestManager manager = Object.FindObjectsByType<QuestManager>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(m => !EditorUtility.IsPersistent(m));
        if (manager == null) return null;

        _watched.Add(manager);
        var registry = new HashSet<QuestData>();
        SerializedProperty list = new SerializedObject(manager).FindProperty("_registry");
        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue is QuestData quest) registry.Add(quest);
        return registry;
    }

    IEnumerable<QuestData> QuestsReaching(string locationId)
    {
        _allQuests ??= AssetDatabase.FindAssets("t:QuestData")
            .Select(guid => AssetDatabase.LoadAssetAtPath<QuestData>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(q => q != null).ToList();
        return _allQuests.Where(q => q.objectives != null && q.objectives.Any(o =>
            o.type == QuestObjectiveType.ReachLocation && o.targetId == locationId));
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    // Node heights are only known after UI Toolkit lays them out, so placement waits for a measured frame.
    void ScheduleLayout(bool frame)
    {
        _laidOut = false;
        contentViewContainer.style.opacity = 0f;
        schedule.Execute(() =>
        {
            if (_laidOut || _nodes.Any(n => float.IsNaN(n.layout.height) || n.layout.height <= 0f)) return;
            _laidOut = true;
            ApplyLayout();
            contentViewContainer.style.opacity = 1f;
            if (frame) schedule.Execute(() => FrameAll()).StartingIn(30);
        }).Every(10).Until(() => _laidOut);
    }

    void ApplyLayout()
    {
        _placedY.Clear();
        _flowRight = 0f;
        float branchX = FlowWidth + GapX;
        float actionX = branchX + FlowWidth + GapX;

        float y = 0f;
        foreach (var flow in _flows)
        {
            float top = y, rowY = y;
            foreach (var (branch, actions) in flow.rows)
            {
                float height = Place(branch, branchX, rowY);
                for (int i = 0; i < actions.Count; i++)
                    height = Mathf.Max(height, Place(actions[i], actionX + i * (FlowWidth + GapX), rowY));
                rowY += height + RowGap;
            }
            float triggerBottom = top + Place(flow.trigger, 0f, top);
            y = Mathf.Max(rowY - RowGap, triggerBottom) + TriggerGap;
        }

        float x = _flowRight + RefColumnGap;
        foreach (var column in _refColumns)
        {
            if (column.Count == 0) continue;
            PlaceColumn(column, x);
            x += RefWidth + RefColumnGap;
        }
    }

    // Each reference node goes as close as it can to the highest node pointing at it, so lines stay mostly flat.
    void PlaceColumn(List<RefNode> column, float x)
    {
        var pending = new List<RefNode>(column);
        float cursor = float.MinValue;
        while (pending.Count > 0)
        {
            RefNode next = pending.OrderBy(DesiredY).First();
            float desired = DesiredY(next);
            float top = desired == float.MaxValue ? Mathf.Max(cursor, 0f) : Mathf.Max(desired, cursor);
            cursor = top + Place(next, x, top) + RowGap;
            pending.Remove(next);
        }
    }

    float DesiredY(RefNode node)
    {
        float best = float.MaxValue;
        foreach (var source in node.sources)
            if (_placedY.TryGetValue(source, out float y)) best = Mathf.Min(best, y);
        return best;
    }

    float Place(GraphNode node, float x, float y)
    {
        Rect size = node.layout;
        node.SetPosition(new Rect(x, y, size.width, size.height));
        _placedY[node] = y;
        if (!(node is RefNode)) _flowRight = Mathf.Max(_flowRight, x + size.width);
        return size.height;
    }

    // ── Editing ───────────────────────────────────────────────────────────────

    static string BranchPath(int b) => $"branches.Array.data[{b}]";
    static string ActionPath(int b, int a) => $"{BranchPath(b)}.actions.Array.data[{a}]";

    void Modify(UniversalEventTrigger t, string undoName, Action<SerializedObject> change)
    {
        var so = new SerializedObject(t);
        change(so);
        so.ApplyModifiedProperties();
        Undo.SetCurrentGroupName(undoName);
        _window.QueueRefresh();
    }

    void Write(UniversalEventTrigger t, string path, Action<SerializedProperty> set, string undoName) =>
        Modify(t, undoName, so => set(so.FindProperty(path)));

    void InsertBranch(UniversalEventTrigger t, int index) => Modify(t, "Add Branch", so =>
        UniversalEventTriggerEditor.ResetBranch(InsertElement(so.FindProperty("branches"), index)));

    void MoveBranch(UniversalEventTrigger t, int from, int to) => Modify(t, "Move Branch", so =>
        so.FindProperty("branches").MoveArrayElement(from, to));

    void DeleteBranch(UniversalEventTrigger t, int b) => Modify(t, "Delete Branch", so =>
        so.FindProperty("branches").DeleteArrayElementAtIndex(b));

    void InsertAction(UniversalEventTrigger t, int b, int index) => Modify(t, "Add Action", so =>
        UniversalEventTriggerEditor.ResetAction(InsertElement(so.FindProperty(BranchPath(b) + ".actions"), index)));

    void MoveAction(UniversalEventTrigger t, int b, int from, int to) => Modify(t, "Move Action", so =>
        so.FindProperty(BranchPath(b) + ".actions").MoveArrayElement(from, to));

    void DeleteAction(UniversalEventTrigger t, int b, int a) => Modify(t, "Delete Action", so =>
        so.FindProperty(BranchPath(b) + ".actions").DeleteArrayElementAtIndex(a));

    static SerializedProperty InsertElement(SerializedProperty array, int index)
    {
        if (index >= array.arraySize)
        {
            array.arraySize++;
            index = array.arraySize - 1;
        }
        else array.InsertArrayElementAtIndex(index);
        return array.GetArrayElementAtIndex(index);
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    VisualElement EnumRow(string label, Enum value, UniversalEventTrigger t, string path)
    {
        var field = new EnumField(label, value);
        field.RegisterValueChangedCallback(e => Write(t, path, p => p.intValue = Convert.ToInt32(e.newValue), "Change " + label));
        return Compact(field);
    }

    VisualElement ToggleRow(string label, bool value, UniversalEventTrigger t, string path)
    {
        var field = new Toggle(label) { value = value };
        field.RegisterValueChangedCallback(e => Write(t, path, p => p.boolValue = e.newValue, "Change " + label));
        return Compact(field);
    }

    VisualElement ObjectRow(string label, Type type, Object value, bool sceneObjects, UniversalEventTrigger t, string path)
    {
        var field = new ObjectField(label) { objectType = type, allowSceneObjects = sceneObjects, value = value };
        field.RegisterValueChangedCallback(e => Write(t, path, p => p.objectReferenceValue = e.newValue, "Change " + label));
        return Compact(field);
    }

    // Delayed so typing doesn't rebuild the graph (and drop focus) on every key.
    VisualElement TextRow(string label, string value, UniversalEventTrigger t, string path)
    {
        var field = new TextField(label) { value = value ?? "", isDelayed = true };
        field.RegisterValueChangedCallback(e => Write(t, path, p => p.stringValue = e.newValue, "Change " + label));
        return Compact(field);
    }

    VisualElement ObjectiveRow(QuestData quest, string value, UniversalEventTrigger t, string path)
    {
        var ids = quest != null && quest.objectives != null
            ? quest.objectives.Select(o => o.objectiveId).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList()
            : new List<string>();
        if (ids.Count == 0) return TextRow("Objective", value, t, path);

        // An id the quest doesn't have stays listed, so a typo is shown (and warned about) rather than silently replaced.
        if (!string.IsNullOrEmpty(value) && !ids.Contains(value)) ids.Add(value);
        var field = new DropdownField("Objective", ids, 0);
        field.SetValueWithoutNotify(value ?? "");
        field.RegisterValueChangedCallback(e => Write(t, path, p => p.stringValue = e.newValue, "Change Objective"));
        return Compact(field);
    }

    static VisualElement Compact(VisualElement field)
    {
        var label = field.Q<Label>(className: "unity-base-field__label");
        if (label != null)
        {
            label.style.minWidth = LabelWidth;
            label.style.width = LabelWidth;
        }
        return field;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    T Add<T>(T node) where T : GraphNode
    {
        _nodes.Add(node);
        AddElement(node);
        return node;
    }

    void Connect(Port from, Port to)
    {
        Edge edge = from.ConnectTo(to);
        edge.capabilities = 0;
        IgnorePicking(edge);
        AddElement(edge);
    }

    static void IgnorePicking(VisualElement root) => root.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);

    static IEnumerable<string> TriggerWarnings(UniversalEventTrigger t)
    {
        if (t.branches == null || t.branches.Length == 0) yield return "No branches — this trigger does nothing.";

        var collider = t.GetComponent<Collider2D>();
        if (collider == null) yield return "No Collider2D — the player can't reach it.";
        else if (t.activation == UniversalEventTrigger.Activation.OnEnter && !collider.isTrigger)
            yield return "On Enter needs the collider's Is Trigger enabled.";

        int interactLayer = LayerMask.NameToLayer("Interact");
        if (t.activation == UniversalEventTrigger.Activation.OnInteract && interactLayer >= 0 && t.gameObject.layer != interactLayer)
            yield return "Not on the 'Interact' layer, so Interact won't find it.";
    }

    static string ObjectiveProblem(QuestData quest, string objectiveId)
    {
        if (string.IsNullOrEmpty(objectiveId)) return "Pick an objective.";
        if (quest.objectives == null || quest.objectives.All(o => o.objectiveId != objectiveId))
            return $"'{objectiveId}' isn't an objective of {quest.name}.";
        return null;
    }

    static Color ActionHeader(UniversalEventTrigger.ActionType type) => type switch
    {
        UniversalEventTrigger.ActionType.PlayDialog     => DialogHeader,
        UniversalEventTrigger.ActionType.PlayCutscene   => CutsceneHeader,
        UniversalEventTrigger.ActionType.OpenQuestBoard => BoardHeader,
        _                                               => QuestHeader
    };

    static DropdownMenuAction.Status Enabled(bool enabled) =>
        enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;

    static string Excerpt(DialogLine line)
    {
        string text = MarkupTag.Replace(line.text, "").Replace('\n', ' ').Trim();
        if (text.Length > 48) text = text.Substring(0, 48) + "…";
        return string.IsNullOrWhiteSpace(line.speakerName) ? $"“{text}”" : $"{line.speakerName}: “{text}”";
    }

    static void SelectObject(Object target)
    {
        Selection.activeObject = target;
        EditorGUIUtility.PingObject(target);
    }

    // ── Node types ────────────────────────────────────────────────────────────

    class GraphNode : Node
    {
        public Port flowIn, flowOut;
        public Action<DropdownMenu> menu;

        public GraphNode(string title, Color header, float width)
        {
            this.title = title;
            capabilities = 0;
            style.width = width;
            titleContainer.style.backgroundColor = header;
            titleButtonContainer.Clear();

            extensionContainer.style.backgroundColor = new Color(0.20f, 0.20f, 0.20f, 0.96f);
            extensionContainer.style.paddingLeft = 6;
            extensionContainer.style.paddingRight = 6;
            extensionContainer.style.paddingTop = 4;
            extensionContainer.style.paddingBottom = 6;
        }

        public Port AddPort(Direction direction, string name, Color color, Port.Capacity capacity)
        {
            Port port = Port.Create<Edge>(Orientation.Horizontal, direction, capacity, typeof(bool));
            port.portName = name;
            port.portColor = color;
            IgnorePicking(port);
            (direction == Direction.Input ? inputContainer : outputContainer).Add(port);
            return port;
        }

        public void AddHeaderButton(string text, string tooltip, Action onClick, bool enabled = true)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.style.minWidth = 18;
            button.style.height = 18;
            button.style.marginLeft = 1;
            button.style.marginRight = 1;
            button.style.paddingLeft = 3;
            button.style.paddingRight = 3;
            button.style.fontSize = 10;
            button.style.alignSelf = Align.Center;
            button.SetEnabled(enabled);
            titleButtonContainer.Add(button);
        }

        public void AddRow(VisualElement row) => extensionContainer.Add(row);

        public void AddInfo(string text) => extensionContainer.Add(TextLabel(text, InfoColor));

        public void AddWarning(string text) => extensionContainer.Add(TextLabel("⚠ " + text, WarningColor));

        public void AddFooterButton(string text, Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.style.marginTop = 4;
            extensionContainer.Add(button);
        }

        static Label TextLabel(string text, Color color)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.fontSize = 11;
            label.style.color = color;
            label.style.marginLeft = 3;
            label.style.marginTop = 1;
            return label;
        }

        // The graph view builds every node's context menu, which keeps GraphView's default entries out.
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt) { }
    }

    class RefNode : GraphNode
    {
        public readonly List<GraphNode> sources = new();
        readonly Dictionary<RefInput, Port> _inputs = new();

        public RefNode(string title, Color header) : base(title, header, RefWidth) { }

        public Port Input(RefInput kind)
        {
            if (!_inputs.TryGetValue(kind, out Port port))
            {
                port = kind switch
                {
                    RefInput.PlayedBy   => AddPort(Direction.Input, "◂ played by", PlaysColor, Port.Capacity.Multi),
                    RefInput.FromChoice => AddPort(Direction.Input, "◂ choice from", ChoiceColor, Port.Capacity.Multi),
                    RefInput.CheckedBy  => AddPort(Direction.Input, "◂ checked by", ChecksColor, Port.Capacity.Multi),
                    _                   => AddPort(Direction.Input, "◂ changed by", ChangesColor, Port.Capacity.Multi)
                };
                _inputs[kind] = port;
            }
            return port;
        }
    }
}
