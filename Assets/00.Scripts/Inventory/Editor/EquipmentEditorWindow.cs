using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Equipment creation and browsing tool.
/// Open via: Tools > Equipment Editor
/// </summary>
public class EquipmentEditorWindow : EditorWindow
{
    // ── Create form state ─────────────────────────────────────────────────────

    string _fileName = "NewEquipmentData";
    string _equipmentId = "";
    string _equipmentName = "";
    string _description = "";
    Sprite _icon;

    // ── Browser state ─────────────────────────────────────────────────────────

    List<EquipmentData> _allEquipment = new();
    EquipmentData _selected = null;
    Vector2 _listScroll;
    Vector2 _formScroll;
    bool _browseMode = true;

    // ── Colors ────────────────────────────────────────────────────────────────

    static readonly Color SelectedColor = new(0.20f, 0.40f, 0.70f, 0.35f);

    // ── Window ────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Equipment Editor")]
    public static void Open()
    {
        var win = GetWindow<EquipmentEditorWindow>("Equipment Editor");
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

        if (GUILayout.Toggle(!_browseMode, "New Equipment", EditorStyles.toolbarButton, GUILayout.Width(96)))
            _browseMode = false;
        if (GUILayout.Toggle(_browseMode, "Browse", EditorStyles.toolbarButton, GUILayout.Width(64)))
            _browseMode = true;

        EditorGUILayout.EndHorizontal();
    }

    // ── Left: asset list ──────────────────────────────────────────────────────

    void DrawListPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(180), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("Equipment", EditorStyles.boldLabel);

        _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

        foreach (var equipment in _allEquipment)
        {
            bool isSelected = equipment == _selected;
            var rect = EditorGUILayout.BeginVertical(GUI.skin.box);

            if (isSelected) EditorGUI.DrawRect(rect, SelectedColor);

            string preview = string.IsNullOrEmpty(equipment.equipmentName) ? "(no name)" : equipment.equipmentName;
            if (preview.Length > 28) preview = preview[..28] + "…";
            EditorGUILayout.LabelField(preview, EditorStyles.miniLabel);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selected = equipment;
                _browseMode = true;
                Repaint();
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(1);
        }

        if (_allEquipment.Count == 0)
            EditorGUILayout.HelpBox("No equipment assets found.", MessageType.None);

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
            EditorGUILayout.HelpBox("Select an equipment on the left.", MessageType.None);
            return;
        }

        EditorGUILayout.LabelField($"ID: {_selected.equipmentId}", EditorStyles.miniLabel);
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(_selected.equipmentName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField(_selected.description, EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space(10);
        DrawPlusLevels();
        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Select in Project", GUILayout.Height(26)))
        {
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = _selected;
        }

        if (GUILayout.Button("Delete", GUILayout.Height(26)))
        {
            if (EditorUtility.DisplayDialog("Equipment Editor", $"Delete '{_selected.name}'?", "Delete", "Cancel"))
            {
                string path = AssetDatabase.GetAssetPath(_selected);
                AssetDatabase.DeleteAsset(path);
                _selected = null;
                RefreshList();
            }
        }

        EditorGUILayout.EndHorizontal();
    }

    // Plus levels are index = enhancement level (+0, +1, +2, ...), so rows can only be
    // appended or trimmed from the end — removing a middle row would silently renumber
    // every level after it.
    void DrawPlusLevels()
    {
        EditorGUILayout.LabelField("Plus Levels", EditorStyles.boldLabel);

        var levels = new List<EquipmentPlusLevel>(_selected.plusLevels ?? System.Array.Empty<EquipmentPlusLevel>());
        bool changed = false;

        for (int i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
            EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUILayout.LabelField($"+{i}", EditorStyles.boldLabel);

            int newAttack = EditorGUILayout.IntField("Attack Point", level.attackPoint);
            if (newAttack != level.attackPoint) { level.attackPoint = newAttack; changed = true; }

            int newDefend = EditorGUILayout.IntField("Defend Point", level.defendPoint);
            if (newDefend != level.defendPoint) { level.defendPoint = newDefend; changed = true; }

            int newHealth = EditorGUILayout.IntField("Health Point", level.healthPoint);
            if (newHealth != level.healthPoint) { level.healthPoint = newHealth; changed = true; }

            int newStamina = EditorGUILayout.IntField("Stamina Point", level.staminaPoint);
            if (newStamina != level.staminaPoint) { level.staminaPoint = newStamina; changed = true; }

            int newMoveSpeed = EditorGUILayout.IntField("Move Speed Point", level.moveSpeedPoint);
            if (newMoveSpeed != level.moveSpeedPoint) { level.moveSpeedPoint = newMoveSpeed; changed = true; }

            int newAttackSpeed = EditorGUILayout.IntField("Attack Speed Point", level.attackSpeedPoint);
            if (newAttackSpeed != level.attackSpeedPoint) { level.attackSpeedPoint = newAttackSpeed; changed = true; }

            int newLuck = EditorGUILayout.IntField("Luck Point", level.luckPoint);
            if (newLuck != level.luckPoint) { level.luckPoint = newLuck; changed = true; }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ Add Level"))
        {
            levels.Add(new EquipmentPlusLevel());
            changed = true;
        }

        using (new EditorGUI.DisabledScope(levels.Count <= 1))
        {
            if (GUILayout.Button($"Remove +{levels.Count - 1}"))
            {
                levels.RemoveAt(levels.Count - 1);
                changed = true;
            }
        }

        EditorGUILayout.EndHorizontal();

        if (changed)
        {
            _selected.plusLevels = levels.ToArray();
            EditorUtility.SetDirty(_selected);
            AssetDatabase.SaveAssetIfDirty(_selected);
        }
    }

    void DrawCreateForm()
    {
        EditorGUILayout.LabelField("New Equipment", EditorStyles.boldLabel);
        _fileName = EditorGUILayout.TextField("File Name", _fileName);
        EditorGUILayout.Space(4);
        _equipmentId = EditorGUILayout.TextField("Equipment ID", _equipmentId);
        _equipmentName = EditorGUILayout.TextField("Equipment Name", _equipmentName);
        EditorGUILayout.LabelField("Description");
        _description = EditorGUILayout.TextArea(_description, GUILayout.MinHeight(60));
        EditorGUILayout.Space(4);
        _icon = (Sprite)EditorGUILayout.ObjectField("Icon", _icon, typeof(Sprite), false);

        EditorGUILayout.Space(12);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Clear", GUILayout.Height(28)))
            ResetForm();

        if (GUILayout.Button("Create Equipment Asset", GUILayout.Height(28)))
            CreateAsset();

        EditorGUILayout.EndHorizontal();
    }

    // ── Logic ─────────────────────────────────────────────────────────────────

    void RefreshList()
    {
        _allEquipment.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:EquipmentData"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<EquipmentData>(path);
            if (asset != null) _allEquipment.Add(asset);
        }
        _allEquipment.Sort((a, b) => string.Compare(a.equipmentId, b.equipmentId, System.StringComparison.Ordinal));
        Repaint();
    }

    void CreateAsset()
    {
        if (string.IsNullOrWhiteSpace(_fileName))
        {
            EditorUtility.DisplayDialog("Equipment Editor", "File name cannot be empty.", "OK");
            return;
        }

        string folder = "Assets/10.Equipment";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "10.Equipment");

        string path = $"{folder}/{_fileName}.asset";

        // Overwrite = update the existing asset in place. Recreating it with
        // CreateAsset destroys the loaded object mid-save (asserts) and breaks
        // references to it (PlayerEquipment registry).
        EquipmentData existing = AssetDatabase.LoadAssetAtPath<EquipmentData>(path);
        if (existing != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Equipment Editor", $"'{path}' already exists. Overwrite?", "Overwrite", "Cancel");
            if (!overwrite) return;
        }

        EquipmentData asset = existing != null ? existing : CreateInstance<EquipmentData>();
        asset.equipmentId = _equipmentId;
        asset.equipmentName = _equipmentName;
        asset.description = _description;
        asset.icon = _icon;

        if (existing != null)
            EditorUtility.SetDirty(asset);
        else
            AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
        Debug.Log($"[EquipmentEditor] {(existing != null ? "Updated" : "Created")} '{path}'");

        RefreshList();
        _selected = asset;
        _browseMode = true;
    }

    void ResetForm()
    {
        _fileName = "NewEquipmentData";
        _equipmentId = "";
        _equipmentName = "";
        _description = "";
        _icon = null;
    }
}
