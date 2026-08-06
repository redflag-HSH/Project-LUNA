using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Quest creation and browsing tool.
/// Open via: Tools > Quest Editor
/// </summary>
public class QuestEditorWindow : EditorWindow
{
    // ── Create form state ─────────────────────────────────────────────────────

    string _fileName = "NewQuest";
    string _questId = "";
    string _questName = "";
    string _info = "";
    float _pinX = 5f;
    float _pinY = 5f;
    bool _repeatable = false;

    // ── Browser state ─────────────────────────────────────────────────────────

    List<QuestData> _allQuests = new();
    QuestData _selected = null;
    Vector2 _listScroll;
    Vector2 _formScroll;
    bool _browseMode = true;

    // ── Colors ────────────────────────────────────────────────────────────────

    static readonly Color SelectedColor = new(0.20f, 0.40f, 0.70f, 0.35f);

    // ── Window ────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Quest Editor")]
    public static void Open()
    {
        var win = GetWindow<QuestEditorWindow>("Quest Editor");
        win.minSize = new Vector2(540, 280);
    }

    void OnEnable() => RefreshList();

    void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(2);

        EditorGUILayout.BeginHorizontal();
        DrawListPanel();
        DrawSeparator();
        DrawRightPanel();
        EditorGUILayout.EndHorizontal();
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64)))
            RefreshList();

        GUILayout.FlexibleSpace();

        if (GUILayout.Toggle(!_browseMode, "New Quest", EditorStyles.toolbarButton, GUILayout.Width(80)))
            _browseMode = false;
        if (GUILayout.Toggle(_browseMode, "Browse", EditorStyles.toolbarButton, GUILayout.Width(64)))
            _browseMode = true;

        EditorGUILayout.EndHorizontal();
    }

    // ── Left: asset list ──────────────────────────────────────────────────────

    void DrawListPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(180), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("Quests", EditorStyles.boldLabel);

        _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

        foreach (var quest in _allQuests)
        {
            bool isSelected = quest == _selected;
            var rect = EditorGUILayout.BeginVertical(GUI.skin.box);

            if (isSelected) EditorGUI.DrawRect(rect, SelectedColor);

            string preview = string.IsNullOrEmpty(quest.questName) ? "(no name)" : quest.questName;
            if (preview.Length > 28) preview = preview[..28] + "…";
            EditorGUILayout.LabelField(preview, EditorStyles.miniLabel);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selected = quest;
                _browseMode = true;
                Repaint();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(1);
        }

        if (_allQuests.Count == 0)
            EditorGUILayout.HelpBox("No quest assets found.", MessageType.None);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    void DrawSeparator()
    {
        var rect = EditorGUILayout.GetControlRect(false, GUILayout.Width(1), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.3f));
    }

    // ── Right: info or create form ────────────────────────────────────────────

    void DrawRightPanel()
    {
        _formScroll = EditorGUILayout.BeginScrollView(_formScroll, GUILayout.ExpandWidth(true));

        if (_browseMode) DrawInfo();
        else DrawCreateForm();

        EditorGUILayout.EndScrollView();
    }

    void DrawInfo()
    {
        if (_selected == null)
        {
            EditorGUILayout.HelpBox("Select a quest on the left.", MessageType.None);
            return;
        }

        EditorGUILayout.LabelField($"ID: {_selected.questId}", EditorStyles.miniLabel);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(_selected.questName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(_selected.info, EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField($"Pin Position: ({_selected.pinX:0.##}, {_selected.pinY:0.##})", EditorStyles.miniLabel);
        EditorGUILayout.LabelField(_selected.repeatable ? "Repeatable: yes" : "Repeatable: no (one-time)", EditorStyles.miniLabel);

        EditorGUILayout.Space(10);
        DrawObjectives();
        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Select in Project", GUILayout.Height(26)))
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = _selected;
        }

        if (GUILayout.Button("Delete", GUILayout.Height(26)))
        {
            if (EditorUtility.DisplayDialog("Quest Editor", $"Delete '{_selected.name}'?", "Delete", "Cancel"))
            {
                string path = AssetDatabase.GetAssetPath(_selected);
                AssetDatabase.DeleteAsset(path);
                _selected = null;
                RefreshList();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawObjectives()
    {
        EditorGUILayout.LabelField("Objectives", EditorStyles.boldLabel);

        var objectives = new List<QuestObjective>(_selected.objectives ?? System.Array.Empty<QuestObjective>());
        int removeIndex = -1;
        bool changed = false;

        for (int i = 0; i < objectives.Count; i++)
        {
            var obj = objectives[i];
            EditorGUILayout.BeginVertical(GUI.skin.box);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"#{i}", GUILayout.Width(20));
            string newId = EditorGUILayout.TextField(obj.objectiveId);
            if (newId != obj.objectiveId) { obj.objectiveId = newId; changed = true; }
            if (GUILayout.Button("×", GUILayout.Width(22))) removeIndex = i;
            EditorGUILayout.EndHorizontal();

            string newDesc = EditorGUILayout.TextField("Description", obj.description);
            if (newDesc != obj.description) { obj.description = newDesc; changed = true; }

            var newType = (QuestObjectiveType)EditorGUILayout.EnumPopup("Type", obj.type);
            if (newType != obj.type) { obj.type = newType; changed = true; }

            if (obj.type != QuestObjectiveType.Manual)
            {
                string targetLabel = obj.type switch
                {
                    QuestObjectiveType.KillCount => "Monster Id",
                    QuestObjectiveType.CollectItem => "Item Code / Name",
                    QuestObjectiveType.ReachLocation => "Location Id",
                    QuestObjectiveType.WinBattle => "Battle Id",
                    _ => "Target Id",
                };
                string newTarget = EditorGUILayout.TextField(targetLabel, obj.targetId);
                if (newTarget != obj.targetId) { obj.targetId = newTarget; changed = true; }
            }

            if (obj.type == QuestObjectiveType.KillCount || obj.type == QuestObjectiveType.CollectItem)
            {
                int newCount = EditorGUILayout.IntField("Required Count", obj.requiredCount);
                newCount = Mathf.Max(1, newCount);
                if (newCount != obj.requiredCount) { obj.requiredCount = newCount; changed = true; }
            }

            string prereqJoined = obj.prerequisiteObjectiveIds != null ? string.Join(", ", obj.prerequisiteObjectiveIds) : "";
            string newPrereq = EditorGUILayout.TextField(
                new GUIContent("Prerequisites", "Comma-separated objective ids that must complete before this one can progress."),
                prereqJoined);
            if (newPrereq != prereqJoined)
            {
                obj.prerequisiteObjectiveIds = string.IsNullOrWhiteSpace(newPrereq)
                    ? System.Array.Empty<string>()
                    : System.Array.ConvertAll(newPrereq.Split(','), s => s.Trim());
                changed = true;
            }

            bool newOptional = EditorGUILayout.Toggle(
                new GUIContent("Optional", "If on, this objective doesn't block quest completion."), obj.optional);
            if (newOptional != obj.optional) { obj.optional = newOptional; changed = true; }

            bool newCompletesQuest = EditorGUILayout.Toggle(
                new GUIContent("Completes Quest", "If on, finishing this objective alone completes the whole quest (branch / alternate ending)."),
                obj.completesQuest);
            if (newCompletesQuest != obj.completesQuest) { obj.completesQuest = newCompletesQuest; changed = true; }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        if (objectives.Count == 0)
            EditorGUILayout.HelpBox("No objectives yet.", MessageType.None);

        if (removeIndex >= 0)
        {
            objectives.RemoveAt(removeIndex);
            changed = true;
        }

        if (GUILayout.Button("+ Add Objective"))
        {
            objectives.Add(new QuestObjective { objectiveId = $"objective_{objectives.Count}", requiredCount = 1 });
            changed = true;
        }

        if (changed)
        {
            _selected.objectives = objectives.ToArray();
            EditorUtility.SetDirty(_selected);
            AssetDatabase.SaveAssetIfDirty(_selected);
        }
    }

    void DrawCreateForm()
    {
        EditorGUILayout.LabelField("New Quest", EditorStyles.boldLabel);
        _fileName = EditorGUILayout.TextField("File Name", _fileName);
        EditorGUILayout.Space(4);
        _questId = EditorGUILayout.TextField("Quest ID", _questId);
        _questName = EditorGUILayout.TextField("Quest Name", _questName);
        EditorGUILayout.LabelField("Quest Info");
        _info = EditorGUILayout.TextArea(_info, GUILayout.MinHeight(60));
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Pin Position (0-10, 5 = centered)");
        _pinX = EditorGUILayout.Slider("Pin X", _pinX, 0f, 10f);
        _pinY = EditorGUILayout.Slider("Pin Y", _pinY, 0f, 10f);
        _repeatable = EditorGUILayout.Toggle(
            new GUIContent("Repeatable", "If on, the quest can be acquired again after completion. Off = once only."),
            _repeatable);

        EditorGUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Clear", GUILayout.Height(28)))
            ResetForm();

        if (GUILayout.Button("Create Quest Asset", GUILayout.Height(28)))
            CreateAsset();

        EditorGUILayout.EndHorizontal();
    }

    // ── Logic ─────────────────────────────────────────────────────────────────

    void RefreshList()
    {
        _allQuests.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:QuestData"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<QuestData>(path);
            if (asset != null) _allQuests.Add(asset);
        }
        _allQuests.Sort((a, b) => string.Compare(a.questId, b.questId, System.StringComparison.Ordinal));
        Repaint();
    }

    void CreateAsset()
    {
        if (string.IsNullOrWhiteSpace(_fileName))
        {
            EditorUtility.DisplayDialog("Quest Editor", "File name cannot be empty.", "OK");
            return;
        }

        string folder = "Assets/05.Quests";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "05.Quests");

        string path = $"{folder}/{_fileName}.asset";

        // Overwrite = update the existing asset in place. Recreating it with
        // CreateAsset destroys the loaded object mid-save (asserts) and breaks
        // references to it (QuestManager registry, dialog quest actions).
        QuestData existing = AssetDatabase.LoadAssetAtPath<QuestData>(path);
        if (existing != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Quest Editor", $"'{path}' already exists. Overwrite?", "Overwrite", "Cancel");
            if (!overwrite) return;
        }

        QuestData asset = existing != null ? existing : CreateInstance<QuestData>();
        asset.questId = _questId;
        asset.questName = _questName;
        asset.info = _info;
        asset.pinX = _pinX;
        asset.pinY = _pinY;
        asset.repeatable = _repeatable;

        if (existing != null)
            EditorUtility.SetDirty(asset);
        else
            AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
        Debug.Log($"[QuestEditor] {(existing != null ? "Updated" : "Created")} '{path}'");

        RefreshList();
        _selected = asset;
        _browseMode = true;
    }

    void ResetForm()
    {
        _fileName = "NewQuest";
        _questId = "";
        _questName = "";
        _info = "";
        _pinX = 5f;
        _pinY = 5f;
        _repeatable = false;
    }
}
