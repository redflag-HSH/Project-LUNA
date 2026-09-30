using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom editor window for creating Blue-Archive-style Dialog assets.
/// Open via: Tools > Dialog Editor
/// </summary>
public class DialogEditor : EditorWindow
{
    // ── Data ──────────────────────────────────────────────────────────────────

    string fileName  = "NewDialog";
    string subFolder = "";           // relative to root dialog folder
    int    dialogId  = 0;
    Dialog duplicateIdOwner;         // existing asset already using dialogId (null = free)

    DialogQuestAction questAction = DialogQuestAction.None;
    QuestData         quest;
    string            questObjectiveId;

    class ChoiceData
    {
        public string text       = "...";
        public Dialog nextDialog = null;
    }

    class LineData
    {
        public string     speakerName    = "";
        public Color      namePlateColor = new Color(0.18f, 0.44f, 0.80f, 1f);
        public Sprite     portrait       = null;
        public DialogSide side           = DialogSide.Left;
        public int        portraitSlot   = 0;
        public string     text           = "";
        public bool       choicesFoldout = false;
        public List<ChoiceData> choices  = new();
    }

    List<LineData> lines = new() { new LineData() };
    Vector2 scroll;

    static readonly Color CardColor   = new(0.18f, 0.18f, 0.18f, 0.30f);
    static readonly Color ChoiceColor = new(0.12f, 0.22f, 0.12f, 0.40f);
    static readonly Color FolderColor = new(0.24f, 0.24f, 0.12f, 0.35f);
    static readonly Color MatchColor  = new(0.60f, 0.48f, 0.10f, 0.35f);

    // Strips rich-text / <ruby=…>…</ruby> tags so a searched phrase still matches across markup.
    static readonly Regex MarkupTag = new(@"<[^>]*>");

    const string RootFolder = "Assets/04.Dialogs";

    // ── Browse state ──────────────────────────────────────────────────────────

    enum Tab { Create, Browse }
    Tab activeTab = Tab.Create;

    // folder path → dialogs in that folder
    Dictionary<string, List<Dialog>> folderMap    = new();
    Dictionary<string, bool>         folderExpand = new();
    List<string>                     folderOrder  = new();

    Dialog  selectedDialog;
    Vector2 browseScroll;
    Vector2 previewScroll;
    string  searchText  = "";
    bool    browseDirty = true;

    // Query the tree was last rebuilt with; drawing uses this rather than the live text box so it matches the layout pass.
    string appliedSearch = "";
    bool   IsSearching => !string.IsNullOrWhiteSpace(appliedSearch);

    // Dialogs that matched the search by their text rather than their file name → excerpt of the matching line.
    Dictionary<Dialog, GUIContent> matchHints = new();

    GUIStyle hintStyle;
    GUIStyle placeholderStyle;

    // ── Window ────────────────────────────────────────────────────────────────

    [MenuItem("Tools/Dialog Editor")]
    public static void Open()
    {
        var win = GetWindow<DialogEditor>("Dialog Editor");
        win.minSize = new Vector2(600, 440);
    }

    void OnFocus() => browseDirty = true;

    void OnGUI()
    {
        DrawTabs();
        EditorGUILayout.Space(4);

        if (activeTab == Tab.Create)
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawHeader();
            EditorGUILayout.Space(8);
            DrawLines();
            EditorGUILayout.Space(12);
            DrawFooter();
            EditorGUILayout.EndScrollView();
        }
        else
        {
            DrawBrowse();
        }
    }

    void DrawTabs()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Toggle(activeTab == Tab.Create, "Create", EditorStyles.toolbarButton)) activeTab = Tab.Create;
        if (GUILayout.Toggle(activeTab == Tab.Browse, "Browse", EditorStyles.toolbarButton)) activeTab = Tab.Browse;
        EditorGUILayout.EndHorizontal();
    }

    // ── Create ────────────────────────────────────────────────────────────────

    void DrawHeader()
    {
        EditorGUILayout.LabelField("Dialog Asset", EditorStyles.boldLabel);
        fileName = EditorGUILayout.TextField("File Name", fileName);

        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        dialogId = EditorGUILayout.IntField(
            new GUIContent("Dialog ID", "Runtime lookup id, e.g. for DialogSystem.GetDialogById. Must be unique."),
            dialogId);
        if (EditorGUI.EndChangeCheck()) duplicateIdOwner = FindDialogWithId(dialogId, fileName, subFolder);
        if (GUILayout.Button(new GUIContent("Next Free", "Pick the lowest unused id."), GUILayout.Width(72)))
        {
            dialogId = NextFreeDialogId();
            duplicateIdOwner = null;
            GUI.FocusControl(null);
        }
        EditorGUILayout.EndHorizontal();

        if (duplicateIdOwner != null)
            EditorGUILayout.HelpBox($"ID {dialogId} is already used by '{duplicateIdOwner.name}'.", MessageType.Warning);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("On Dialog End", EditorStyles.boldLabel);
        questAction = (DialogQuestAction)EditorGUILayout.EnumPopup(
            new GUIContent("Quest Action", "What to do with the quest below when this dialog closes."),
            questAction);
        if (questAction != DialogQuestAction.None)
        {
            quest = (QuestData)EditorGUILayout.ObjectField("Quest", quest, typeof(QuestData), false);
            if (quest == null)
                EditorGUILayout.HelpBox("Assign a quest, or the action does nothing.", MessageType.Warning);

            if (questAction == DialogQuestAction.CompleteObjective)
            {
                questObjectiveId = EditorGUILayout.TextField("Objective Id", questObjectiveId);
                if (string.IsNullOrEmpty(questObjectiveId))
                    EditorGUILayout.HelpBox("Enter the objective id to complete, or the action does nothing.", MessageType.Warning);
            }
        }

        EditorGUILayout.BeginHorizontal();
        subFolder = EditorGUILayout.TextField(new GUIContent("Subfolder", $"Subfolder inside {RootFolder}/"), subFolder);
        if (GUILayout.Button("…", GUILayout.Width(24)))
        {
            string abs  = Path.GetFullPath(Path.Combine(Application.dataPath, "../", RootFolder));
            string pick = EditorUtility.OpenFolderPanel("Choose Subfolder", abs, "");
            if (!string.IsNullOrEmpty(pick) && pick.StartsWith(abs))
                subFolder = pick.Substring(abs.Length).TrimStart('/', '\\');
        }
        EditorGUILayout.EndHorizontal();

        string savePath = string.IsNullOrWhiteSpace(subFolder)
            ? $"{RootFolder}/{fileName}.asset"
            : $"{RootFolder}/{subFolder}/{fileName}.asset";
        EditorGUILayout.LabelField($"→  {savePath}", EditorStyles.miniLabel);
    }

    void DrawLines()
    {
        EditorGUILayout.LabelField("Lines", EditorStyles.boldLabel);

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            var rect = EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUI.DrawRect(rect, CardColor);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = i > 0;
            if (GUILayout.Button("▲", GUILayout.Width(22)))
            { (lines[i - 1], lines[i]) = (lines[i], lines[i - 1]); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break; }
            GUI.enabled = i < lines.Count - 1;
            if (GUILayout.Button("▼", GUILayout.Width(22)))
            { (lines[i + 1], lines[i]) = (lines[i], lines[i + 1]); EditorGUILayout.EndHorizontal(); EditorGUILayout.EndVertical(); break; }
            GUI.enabled = true;

            EditorGUILayout.LabelField($"Line {i + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            GUI.enabled = lines.Count > 1;
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                lines.RemoveAt(i);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUIUtility.ExitGUI();
                return;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            line.speakerName    = EditorGUILayout.TextField("Speaker", line.speakerName, GUILayout.ExpandWidth(true));
            line.namePlateColor = EditorGUILayout.ColorField(GUIContent.none, line.namePlateColor, GUILayout.Width(48));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            line.portrait = (Sprite)EditorGUILayout.ObjectField("Portrait", line.portrait, typeof(Sprite), false);
            line.side     = (DialogSide)EditorGUILayout.EnumPopup(GUIContent.none, line.side, GUILayout.Width(60));
            EditorGUILayout.LabelField(new GUIContent("Slot", "Which portrait position on that side (0-2). Matches DialogIlust.MaxPortraitsPerSide."), GUILayout.Width(30));
            line.portraitSlot = Mathf.Clamp(EditorGUILayout.IntField(line.portraitSlot, GUILayout.Width(30)), 0, 2);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Text");
            line.text = EditorGUILayout.TextArea(line.text, GUILayout.MinHeight(52));

            EditorGUILayout.Space(4);
            DrawChoices(line);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4);
        }

        if (GUILayout.Button("+ Add Line", GUILayout.Height(26)))
        {
            var prev = lines[lines.Count - 1];
            lines.Add(new LineData { speakerName = prev.speakerName, namePlateColor = prev.namePlateColor, side = prev.side, portraitSlot = prev.portraitSlot });
        }
    }

    void DrawChoices(LineData line)
    {
        line.choicesFoldout = EditorGUILayout.Foldout(
            line.choicesFoldout,
            line.choices.Count > 0 ? $"Choices  ({line.choices.Count})" : "Choices",
            true);

        if (!line.choicesFoldout) return;

        EditorGUI.indentLevel++;

        for (int c = 0; c < line.choices.Count; c++)
        {
            var choice = line.choices[c];

            var rect = EditorGUILayout.BeginVertical(GUI.skin.box);
            EditorGUI.DrawRect(rect, ChoiceColor);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Choice {c + 1}", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", GUILayout.Width(22)))
            {
                line.choices.RemoveAt(c);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                break;
            }
            EditorGUILayout.EndHorizontal();

            choice.text       = EditorGUILayout.TextField("Label", choice.text);
            choice.nextDialog = (Dialog)EditorGUILayout.ObjectField(
                new GUIContent("Next Dialog", "Leave empty to close the conversation."),
                choice.nextDialog, typeof(Dialog), false);

            if (choice.nextDialog == null)
                EditorGUILayout.HelpBox("No Next Dialog → closes the conversation.", MessageType.None);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        EditorGUI.indentLevel--;

        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 15 + 4);
        if (GUILayout.Button("+ Add Choice", GUILayout.Height(22)))
            line.choices.Add(new ChoiceData());
        EditorGUILayout.EndHorizontal();
    }

    void DrawFooter()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear",         GUILayout.Height(28))) ResetForm();
        if (GUILayout.Button("Create Dialog", GUILayout.Height(28))) CreateAsset();
        EditorGUILayout.EndHorizontal();
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    void DrawBrowse()
    {
        // Only on Layout: the results decide how many controls are drawn, so a mid-event rebuild would desync from layout.
        if (browseDirty && Event.current.type == EventType.Layout) RefreshBrowse();

        hintStyle        ??= new GUIStyle(EditorStyles.miniLabel) { padding = { left = 16 }, clipping = TextClipping.Clip };
        placeholderStyle ??= new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleLeft };

        EditorGUILayout.BeginHorizontal();

        // ── Left: folder tree ──
        EditorGUILayout.BeginVertical(GUILayout.Width(210));

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        EditorGUI.BeginChangeCheck();
        searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField);
        if (string.IsNullOrEmpty(searchText))
        {
            Rect field = GUILayoutUtility.GetLastRect();
            field.xMin += EditorStyles.toolbarSearchField.padding.left;
            GUI.Label(field, "Name or dialog text…", placeholderStyle);
        }
        if (EditorGUI.EndChangeCheck()) RequestBrowseRefresh();
        if (GUILayout.Button("↺", EditorStyles.toolbarButton, GUILayout.Width(24))) RequestBrowseRefresh();
        EditorGUILayout.EndHorizontal();

        browseScroll = EditorGUILayout.BeginScrollView(browseScroll);

        foreach (var folder in folderOrder)
        {
            var dialogs = folderMap[folder];
            if (dialogs.Count == 0) continue;

            string label = string.IsNullOrEmpty(folder) ? $"{RootFolder}/" : folder;

            var rect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rect, FolderColor);

            folderExpand[folder] = EditorGUILayout.Foldout(
                folderExpand.GetValueOrDefault(folder, true),
                $"📁 {label}  ({dialogs.Count})", true, EditorStyles.foldoutHeader);

            EditorGUILayout.EndVertical();

            if (!folderExpand.GetValueOrDefault(folder, true)) continue;

            EditorGUI.indentLevel++;
            foreach (var dialog in dialogs)
            {
                bool sel = dialog == selectedDialog;
                var  bg  = GUI.backgroundColor;
                if (sel) GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f, 1f);

                if (GUILayout.Button(dialog.name, sel ? EditorStyles.boldLabel : EditorStyles.label))
                    selectedDialog = dialog;

                GUI.backgroundColor = bg;

                if (matchHints.TryGetValue(dialog, out GUIContent hint) && GUILayout.Button(hint, hintStyle))
                    selectedDialog = dialog;
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        if (folderOrder.Count == 0 && IsSearching)
            EditorGUILayout.HelpBox($"No dialog name or text contains \"{appliedSearch}\".", MessageType.None);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        // ── Divider ──
        GUILayout.Box("", GUILayout.Width(1), GUILayout.ExpandHeight(true));

        // ── Right: preview ──
        EditorGUILayout.BeginVertical();

        if (selectedDialog == null)
        {
            EditorGUILayout.HelpBox("Select a dialog from the tree to preview.", MessageType.None);
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(selectedDialog.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"ID: {selectedDialog.dialogId}", EditorStyles.miniLabel, GUILayout.Width(60));
            if (selectedDialog.questAction != DialogQuestAction.None)
                EditorGUILayout.LabelField(
                    $"⚑ {selectedDialog.questAction}: {(selectedDialog.quest != null ? selectedDialog.quest.questId : "(no quest!)")}" +
                    (selectedDialog.questAction == DialogQuestAction.CompleteObjective ? $" / {selectedDialog.questObjectiveId}" : ""),
                    EditorStyles.miniLabel, GUILayout.Width(220));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select in Project", EditorStyles.miniButton))
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = selectedDialog;
            }
            if (GUILayout.Button("Load into Editor", EditorStyles.miniButton))
            {
                LoadIntoEditor(selectedDialog);
                activeTab = Tab.Create;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (selectedDialog.lines == null || selectedDialog.lines.Length == 0)
            {
                EditorGUILayout.HelpBox("No lines.", MessageType.None);
            }
            else
            {
                previewScroll = EditorGUILayout.BeginScrollView(previewScroll);
                for (int i = 0; i < selectedDialog.lines.Length; i++)
                {
                    var l    = selectedDialog.lines[i];
                    var rect = EditorGUILayout.BeginVertical(GUI.skin.box);
                    EditorGUI.DrawRect(rect, IsSearching && LineMatches(l, appliedSearch) ? MatchColor : CardColor);

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"#{i + 1}", GUILayout.Width(28));

                    if (!string.IsNullOrWhiteSpace(l.speakerName))
                    {
                        var prev = GUI.backgroundColor;
                        GUI.backgroundColor = l.namePlateColor;
                        EditorGUILayout.LabelField(l.speakerName, EditorStyles.boldLabel, GUILayout.Width(110));
                        GUI.backgroundColor = prev;
                    }

                    EditorGUILayout.LabelField($"{l.side} #{l.portraitSlot}", EditorStyles.miniLabel, GUILayout.Width(56));
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField(l.text, EditorStyles.wordWrappedLabel);

                    if (l.choices != null && l.choices.Length > 0)
                    {
                        EditorGUI.indentLevel++;
                        foreach (var c in l.choices)
                            EditorGUILayout.LabelField(
                                $"› {c.text}  →  {(c.nextDialog != null ? c.nextDialog.name : "close")}",
                                EditorStyles.miniLabel);
                        EditorGUI.indentLevel--;
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(2);
                }
                EditorGUILayout.EndScrollView();
            }
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    void RequestBrowseRefresh()
    {
        browseDirty = true;
        Repaint();
    }

    void RefreshBrowse()
    {
        browseDirty = false;
        appliedSearch = searchText;
        folderMap.Clear();
        folderOrder.Clear();
        matchHints.Clear();

        string[] guids = AssetDatabase.FindAssets("t:Dialog");
        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            var    dialog    = AssetDatabase.LoadAssetAtPath<Dialog>(assetPath);
            if (dialog == null) continue;

            if (IsSearching && !ContainsText(dialog.name, appliedSearch))
            {
                GUIContent hint = FindTextMatch(dialog, appliedSearch);
                if (hint == null) continue;
                matchHints[dialog] = hint;
            }

            // folder relative to RootFolder, or full path if outside it
            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string rel = dir.StartsWith(RootFolder)
                ? dir.Substring(RootFolder.Length).TrimStart('/')
                : dir;

            if (!folderMap.ContainsKey(rel))
            {
                folderMap[rel] = new List<Dialog>();
                folderOrder.Add(rel);
                if (!folderExpand.ContainsKey(rel))
                    folderExpand[rel] = true;
            }
            folderMap[rel].Add(dialog);
        }
    }

    static bool ContainsText(string source, string query) =>
        !string.IsNullOrEmpty(source) && source.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;

    static string PlainText(string text) => string.IsNullOrEmpty(text) ? "" : MarkupTag.Replace(text, "");

    /// <summary>True if the line's speaker, text (with or without markup) or a choice label contains the query.</summary>
    static bool LineMatches(DialogLine line, string query)
    {
        if (line == null) return false;
        if (ContainsText(line.speakerName, query) || ContainsText(line.text, query) || ContainsText(PlainText(line.text), query))
            return true;
        if (line.choices != null)
            foreach (var choice in line.choices)
                if (choice != null && ContainsText(choice.text, query)) return true;
        return false;
    }

    /// <summary>Tree hint for the first line matching the query (full line as tooltip), or null if no line matches.</summary>
    static GUIContent FindTextMatch(Dialog dialog, string query)
    {
        if (dialog.lines == null) return null;

        int first = -1, count = 0;
        for (int i = 0; i < dialog.lines.Length; i++)
        {
            if (!LineMatches(dialog.lines[i], query)) continue;
            if (first < 0) first = i;
            count++;
        }
        if (first < 0) return null;

        DialogLine line = dialog.lines[first];
        string speaker = string.IsNullOrWhiteSpace(line.speakerName) ? "" : $"{line.speakerName}: ";
        string more    = count > 1 ? $"  (+{count - 1})" : "";
        return new GUIContent($"#{first + 1} {speaker}{Excerpt(line, query)}{more}", PlainText(line.text));
    }

    /// <summary>A short slice of the line's text around the match, e.g. "…퀘스트 좀 받아가…".</summary>
    static string Excerpt(DialogLine line, string query)
    {
        string plain = PlainText(line.text).Replace('\n', ' ');
        int at = plain.IndexOf(query, System.StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            // Matched on the speaker, a choice label, or text inside a tag.
            if (line.choices != null)
                foreach (var choice in line.choices)
                    if (choice != null && ContainsText(choice.text, query)) return $"› {choice.text}";
            return plain.Length > 30 ? plain.Substring(0, 30) + "…" : plain;
        }

        int start = Mathf.Max(0, at - 10);
        int end   = Mathf.Min(plain.Length, at + query.Length + 20);
        return (start > 0 ? "…" : "") + plain.Substring(start, end - start) + (end < plain.Length ? "…" : "");
    }

    // ── Logic ─────────────────────────────────────────────────────────────────

    /// <summary>Returns the dialog already using <paramref name="id"/>, ignoring the asset this form would save to.</summary>
    static Dialog FindDialogWithId(int id, string fileName, string subFolder)
    {
        string savePath = string.IsNullOrWhiteSpace(subFolder)
            ? $"{RootFolder}/{fileName}.asset"
            : $"{RootFolder}/{subFolder}/{fileName}.asset";

        foreach (var guid in AssetDatabase.FindAssets("t:Dialog"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path == savePath) continue;
            var dialog = AssetDatabase.LoadAssetAtPath<Dialog>(path);
            if (dialog != null && dialog.dialogId == id) return dialog;
        }
        return null;
    }

    static int NextFreeDialogId()
    {
        var used = new HashSet<int>();
        foreach (var guid in AssetDatabase.FindAssets("t:Dialog"))
        {
            var dialog = AssetDatabase.LoadAssetAtPath<Dialog>(AssetDatabase.GUIDToAssetPath(guid));
            if (dialog != null) used.Add(dialog.dialogId);
        }
        int id = 0;
        while (used.Contains(id)) id++;
        return id;
    }

    void LoadIntoEditor(Dialog dialog)
    {
        fileName = dialog.name;
        dialogId = dialog.dialogId;
        duplicateIdOwner = null;
        questAction = dialog.questAction;
        quest       = dialog.quest;
        questObjectiveId = dialog.questObjectiveId;

        // Restore subfolder from asset path
        string assetPath = AssetDatabase.GetAssetPath(dialog);
        string dir       = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        subFolder = dir.StartsWith(RootFolder)
            ? dir.Substring(RootFolder.Length).TrimStart('/')
            : "";

        lines.Clear();
        if (dialog.lines != null)
        {
            foreach (var src in dialog.lines)
            {
                var ld = new LineData
                {
                    speakerName    = src.speakerName,
                    namePlateColor = src.namePlateColor,
                    portrait       = src.portrait,
                    side           = src.side,
                    portraitSlot   = src.portraitSlot,
                    text           = src.text
                };
                if (src.choices != null)
                    foreach (var c in src.choices)
                        ld.choices.Add(new ChoiceData { text = c.text, nextDialog = c.nextDialog });
                lines.Add(ld);
            }
        }
        if (lines.Count == 0) lines.Add(new LineData());
    }

    void CreateAsset()
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            EditorUtility.DisplayDialog("Dialog Editor", "File name cannot be empty.", "OK");
            return;
        }

        string folder = string.IsNullOrWhiteSpace(subFolder)
            ? RootFolder
            : $"{RootFolder}/{subFolder}";

        EnsureFolderExists(folder);

        string path = $"{folder}/{fileName}.asset";

        // Overwrite = update the existing asset in place. Recreating it with
        // CreateAsset destroys the loaded object mid-save (asserts) and breaks
        // references to it (registry entries, nextDialog links).
        Dialog existing = AssetDatabase.LoadAssetAtPath<Dialog>(path);
        if (existing != null)
        {
            bool overwrite = EditorUtility.DisplayDialog(
                "Dialog Editor", $"'{path}' already exists. Overwrite?", "Overwrite", "Cancel");
            if (!overwrite) return;
        }

        duplicateIdOwner = FindDialogWithId(dialogId, fileName, subFolder);
        if (duplicateIdOwner != null)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Dialog Editor",
                $"Dialog ID {dialogId} is already used by '{duplicateIdOwner.name}'.\n" +
                "GetDialogById will only ever find one of them.",
                "Create Anyway", "Cancel");
            if (!proceed) return;
        }

        Dialog asset = existing != null ? existing : CreateInstance<Dialog>();
        asset.dialogId = dialogId;
        asset.questAction = questAction;
        asset.quest = questAction != DialogQuestAction.None ? quest : null;
        asset.questObjectiveId = questAction == DialogQuestAction.CompleteObjective ? questObjectiveId : null;
        asset.lines = new DialogLine[lines.Count];

        for (int i = 0; i < lines.Count; i++)
        {
            var src = lines[i];
            var dlg = new DialogLine
            {
                speakerName    = src.speakerName,
                namePlateColor = src.namePlateColor,
                portrait       = src.portrait,
                side           = src.side,
                portraitSlot   = src.portraitSlot,
                text           = src.text,
                choices        = new DialogChoice[src.choices.Count]
            };
            for (int c = 0; c < src.choices.Count; c++)
                dlg.choices[c] = new DialogChoice { text = src.choices[c].text, nextDialog = src.choices[c].nextDialog };

            asset.lines[i] = dlg;
        }

        if (existing != null)
            EditorUtility.SetDirty(asset);
        else
            AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
        browseDirty = true;

        Debug.Log($"[DialogEditor] {(existing != null ? "Updated" : "Created")} '{path}'");
    }

    void ResetForm()
    {
        fileName  = "NewDialog";
        subFolder = "";
        dialogId  = 0;
        duplicateIdOwner = null;
        questAction = DialogQuestAction.None;
        quest     = null;
        questObjectiveId = null;
        lines     = new List<LineData> { new LineData() };
    }

    static void EnsureFolderExists(string folderPath)
    {
        string[] parts  = folderPath.Split('/');
        string   current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
