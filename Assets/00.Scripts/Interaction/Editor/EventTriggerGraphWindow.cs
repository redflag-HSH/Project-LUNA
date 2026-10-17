using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>Tools > Event Trigger Graph: every UniversalEventTrigger in the open scenes as an editable node graph.</summary>
public class EventTriggerGraphWindow : EditorWindow
{
    [SerializeField] int _focusId;   // instance id of the only trigger shown; 0 = all
    [SerializeField] bool _followSelection;
    [SerializeField] bool _showReferences = true;

    EventTriggerGraphView _graph;
    ToolbarMenu _showMenu;
    Label _playModeNote;
    Label _emptyNote;
    string _signature;
    bool _refreshQueued;
    bool _forceRebuild;

    [MenuItem("Tools/Event Trigger Graph")]
    static void OpenFromMenu() => Open(null);

    public static void Open(UniversalEventTrigger focus)
    {
        var window = GetWindow<EventTriggerGraphWindow>("Event Trigger Graph");
        window.minSize = new Vector2(640f, 360f);
        if (focus != null) window.ShowOnly(focus.GetInstanceID());
    }

    void OnEnable()
    {
        ObjectChangeEvents.changesPublished += OnObjectChanges;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        EditorApplication.projectChanged += OnProjectChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorSceneManager.sceneOpened += OnSceneOpened;
        EditorSceneManager.sceneClosed += OnSceneClosed;
        Selection.selectionChanged += OnSelectionChanged;
    }

    void OnDisable()
    {
        ObjectChangeEvents.changesPublished -= OnObjectChanges;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        EditorApplication.projectChanged -= OnProjectChanged;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneClosed -= OnSceneClosed;
        Selection.selectionChanged -= OnSelectionChanged;
    }

    void CreateGUI()
    {
        var toolbar = new Toolbar();

        _showMenu = new ToolbarMenu();
        toolbar.Add(_showMenu);

        var follow = new ToolbarToggle { text = "Follow Selection", tooltip = "Show only the trigger selected in the Hierarchy." };
        follow.SetValueWithoutNotify(_followSelection);
        follow.RegisterValueChangedCallback(e =>
        {
            _followSelection = e.newValue;
            OnSelectionChanged();
        });
        toolbar.Add(follow);

        var references = new ToolbarToggle { text = "References", tooltip = "Show the quests, dialogs and cutscenes the triggers use." };
        references.SetValueWithoutNotify(_showReferences);
        references.RegisterValueChangedCallback(e =>
        {
            _showReferences = e.newValue;
            Rebuild(frame: true);
        });
        toolbar.Add(references);

        toolbar.Add(new ToolbarButton(() => _graph.FrameAll()) { text = "Frame All" });
        toolbar.Add(new ToolbarSpacer { flex = true });

        _playModeNote = new Label("Play mode: edits here are lost when you stop.");
        _playModeNote.style.color = new Color(1f, 0.70f, 0.25f);
        _playModeNote.style.unityTextAlign = TextAnchor.MiddleLeft;
        _playModeNote.style.marginRight = 12;
        toolbar.Add(_playModeNote);

        var legend = new Label(Legend());
        legend.style.unityTextAlign = TextAnchor.MiddleLeft;
        legend.style.marginRight = 6;
        toolbar.Add(legend);

        rootVisualElement.Add(toolbar);

        _graph = new EventTriggerGraphView(this);
        _graph.style.flexGrow = 1;
        rootVisualElement.Add(_graph);

        _emptyNote = new Label("No UniversalEventTrigger in the open scene(s).") { pickingMode = PickingMode.Ignore };
        _emptyNote.style.position = Position.Absolute;
        _emptyNote.style.left = 0;
        _emptyNote.style.right = 0;
        _emptyNote.style.top = 60;
        _emptyNote.style.unityTextAlign = TextAnchor.MiddleCenter;
        _emptyNote.style.color = new Color(0.6f, 0.6f, 0.6f);
        rootVisualElement.Add(_emptyNote);

        Rebuild(frame: true);
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    void OnObjectChanges(ref ObjectChangeEventStream stream) => QueueRefresh();
    void OnUndoRedo() => QueueRefresh();
    void OnHierarchyChanged() => QueueRefresh();
    void OnProjectChanged() => QueueRefresh(force: true);
    void OnPlayModeChanged(PlayModeStateChange _) => QueueRefresh(force: true);
    void OnSceneOpened(Scene scene, OpenSceneMode mode) => QueueRefresh(force: true);
    void OnSceneClosed(Scene scene) => QueueRefresh(force: true);

    /// <summary>Rebuilds once, on the next editor tick, if anything the graph shows has changed.</summary>
    internal void QueueRefresh(bool force = false)
    {
        _forceRebuild |= force;
        if (_refreshQueued) return;
        _refreshQueued = true;
        EditorApplication.delayCall += () =>
        {
            _refreshQueued = false;
            if (this == null || _graph == null) return;

            bool forced = _forceRebuild;
            _forceRebuild = false;
            if (forced || ComputeSignature(FindTriggers()) != _signature) Rebuild(frame: false);
        };
    }

    void Rebuild(bool frame)
    {
        if (_graph == null) return;

        List<UniversalEventTrigger> all = FindTriggers();
        if (_focusId != 0 && all.All(t => t.GetInstanceID() != _focusId)) _focusId = 0;
        List<UniversalEventTrigger> shown = _focusId == 0 ? all : all.Where(t => t.GetInstanceID() == _focusId).ToList();

        RebuildShowMenu(all);
        _graph.Build(shown, _showReferences, frame);
        _signature = ComputeSignature(all);

        _emptyNote.style.display = all.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        _playModeNote.style.display = EditorApplication.isPlayingOrWillChangePlaymode ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Any edit to a trigger, its GameObject/collider, or a node's asset changes this, which is what triggers a rebuild.
    string ComputeSignature(List<UniversalEventTrigger> triggers)
    {
        var sb = new StringBuilder();
        foreach (var t in triggers)
        {
            GameObject go = t.gameObject;
            var collider = t.GetComponent<Collider2D>();
            sb.Append(t.GetInstanceID()).Append('|').Append(go.name).Append('|').Append(go.activeInHierarchy)
              .Append('|').Append(go.layer).Append('|').Append(collider != null).Append(collider != null && collider.isTrigger)
              .Append('|').Append(EditorJsonUtility.ToJson(t)).Append('\n');
        }
        foreach (Object watched in _graph.WatchedObjects)
            sb.Append(watched != null ? EditorJsonUtility.ToJson(watched) : "(destroyed)").Append('\n');
        return sb.ToString();
    }

    // ── Filter ────────────────────────────────────────────────────────────────

    void ShowOnly(int triggerId)
    {
        _focusId = triggerId;
        Rebuild(frame: true);
    }

    void OnSelectionChanged()
    {
        if (!_followSelection || Selection.activeGameObject == null) return;
        var trigger = Selection.activeGameObject.GetComponent<UniversalEventTrigger>();
        if (trigger != null && trigger.GetInstanceID() != _focusId) ShowOnly(trigger.GetInstanceID());
    }

    void RebuildShowMenu(List<UniversalEventTrigger> all)
    {
        DropdownMenu menu = _showMenu.menu;
        menu.MenuItems().Clear();
        menu.AppendAction("All Triggers", _ => ShowOnly(0), _ => Checked(_focusId == 0));
        if (all.Count > 0) menu.AppendSeparator();

        bool multiScene = all.Select(t => t.gameObject.scene).Distinct().Count() > 1;
        var used = new HashSet<string>();
        foreach (var trigger in all)
        {
            int id = trigger.GetInstanceID();
            string name = multiScene ? $"{trigger.gameObject.scene.name}/{trigger.gameObject.name}" : trigger.gameObject.name;
            string label = name;
            for (int n = 2; !used.Add(label); n++) label = $"{name} ({n})";
            menu.AppendAction(label, _ => ShowOnly(id), _ => Checked(_focusId == id));
        }

        var focused = all.FirstOrDefault(t => t.GetInstanceID() == _focusId);
        _showMenu.text = focused != null ? $"Show: {focused.gameObject.name}" : "Show: All Triggers";
    }

    static DropdownMenuAction.Status Checked(bool isChecked) =>
        isChecked ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal;

    static List<UniversalEventTrigger> FindTriggers() =>
        Object.FindObjectsByType<UniversalEventTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => !EditorUtility.IsPersistent(t) && t.gameObject.scene.IsValid())
            .OrderBy(t => t.gameObject.scene.name, StringComparer.Ordinal)
            .ThenBy(t => HierarchyKey(t.transform), StringComparer.Ordinal)
            .ToList();

    static string HierarchyKey(Transform t)
    {
        var indices = new List<string>();
        for (; t != null; t = t.parent) indices.Add(t.GetSiblingIndex().ToString("D5"));
        indices.Reverse();
        return string.Join("/", indices);
    }

    static string Legend()
    {
        static string Swatch(Color color, string label) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>━━</color> {label}";
        return string.Join("    ",
            Swatch(EventTriggerGraphView.FlowColor, "runs"),
            Swatch(EventTriggerGraphView.PlaysColor, "plays"),
            Swatch(EventTriggerGraphView.ChecksColor, "checks quest"),
            Swatch(EventTriggerGraphView.ChangesColor, "changes quest"),
            Swatch(EventTriggerGraphView.ChoiceColor, "choice"));
    }
}
