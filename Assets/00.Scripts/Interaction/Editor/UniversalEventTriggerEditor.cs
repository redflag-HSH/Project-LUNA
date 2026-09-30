using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UniversalEventTrigger))]
public class UniversalEventTriggerEditor : Editor
{
    static readonly Color RangeFill = new Color(0.2f, 0.9f, 0.4f, 0.15f);
    static readonly Color RangeOutline = new Color(0.2f, 0.9f, 0.4f, 0.8f);
    static readonly string[] ActionObjectFields = { "dialog", "director", "cutsceneVCam", "questBoard", "quest" };

    SerializedProperty _activation;
    SerializedProperty _triggerOnce;
    SerializedProperty _branches;

    // Array add/move/delete is deferred until the whole list is drawn, so this GUI event's controls still match its layout pass.
    System.Action _pendingEdit;

    void OnEnable()
    {
        _activation = serializedObject.FindProperty("activation");
        _triggerOnce = serializedObject.FindProperty("triggerOnce");
        _branches = serializedObject.FindProperty("branches");
    }

    public override void OnInspectorGUI()
    {
        var trigger = (UniversalEventTrigger)target;
        serializedObject.Update();

        EditorGUILayout.PropertyField(_activation);
        EditorGUILayout.LabelField(
            _activation.enumValueIndex == (int)UniversalEventTrigger.Activation.OnInteract
                ? "Fires when the player presses Interact in range."
                : "Fires when the player walks into this object's trigger collider.",
            EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.PropertyField(_triggerOnce);
        if (_triggerOnce.boolValue)
            EditorGUILayout.LabelField("Runs once per scene load. It stays armed until one of its branches actually matches.",
                EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space(8);
        DrawBranches();

        _pendingEdit?.Invoke();
        _pendingEdit = null;
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);
        DrawColliderSection(trigger);
        DrawLayerCheck(trigger);
        DrawTestButton(trigger);
    }

    // ── Branches ──────────────────────────────────────────────────────────────

    void DrawBranches()
    {
        EditorGUILayout.LabelField("Branches", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Checked top to bottom — the first branch whose condition matches runs its actions in order, " +
                                   "each finishing before the next starts.", EditorStyles.wordWrappedMiniLabel);

        if (_branches.arraySize == 0)
            EditorGUILayout.HelpBox("No branches — this trigger does nothing.", MessageType.Warning);

        bool belowAlways = false;
        for (int i = 0; i < _branches.arraySize; i++)
        {
            SerializedProperty branch = _branches.GetArrayElementAtIndex(i);
            DrawBranch(branch, i, belowAlways);
            if (branch.FindPropertyRelative("condition").enumValueIndex == (int)UniversalEventTrigger.Condition.Always)
                belowAlways = true;
        }

        if (GUILayout.Button("+ Add Branch", GUILayout.Height(24)))
            _pendingEdit = () => ResetBranch(Append(_branches));
    }

    void DrawBranch(SerializedProperty branch, int index, bool unreachable)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Branch {index + 1}", EditorStyles.boldLabel);
                DrawRowButtons(_branches, index);
            }

            if (unreachable)
                EditorGUILayout.HelpBox("An 'Always' branch above this one always matches first, so this branch never runs.",
                    MessageType.Warning);

            SerializedProperty condition = branch.FindPropertyRelative("condition");
            EditorGUILayout.PropertyField(condition, new GUIContent("If"));

            var cond = (UniversalEventTrigger.Condition)condition.enumValueIndex;
            if (cond != UniversalEventTrigger.Condition.Always)
            {
                EditorGUI.indentLevel++;
                RequiredField(branch, "quest", "Assign a quest, or this branch never matches.");
                if (cond == UniversalEventTrigger.Condition.QuestObjectiveComplete)
                    RequiredField(branch, "objectiveId", "Enter the objective id, or this branch never matches.");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Then", EditorStyles.miniBoldLabel);
            DrawActions(branch.FindPropertyRelative("actions"));
        }
    }

    void DrawActions(SerializedProperty actions)
    {
        if (actions.arraySize == 0)
            EditorGUILayout.HelpBox("No actions — this branch matches but does nothing.", MessageType.Info);

        for (int i = 0; i < actions.arraySize; i++)
        {
            SerializedProperty action = actions.GetArrayElementAtIndex(i);
            SerializedProperty type = action.FindPropertyRelative("type");

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(type, new GUIContent($"{i + 1}."));
                DrawRowButtons(actions, i);
            }

            EditorGUI.indentLevel++;
            DrawActionFields(action, (UniversalEventTrigger.ActionType)type.enumValueIndex);
            EditorGUI.indentLevel--;
        }

        if (GUILayout.Button("+ Add Action"))
            _pendingEdit = () => ResetAction(Append(actions));
    }

    static void DrawActionFields(SerializedProperty action, UniversalEventTrigger.ActionType type)
    {
        switch (type)
        {
            case UniversalEventTrigger.ActionType.PlayDialog:
                RequiredField(action, "dialog", "Assign a dialog.");
                break;

            case UniversalEventTrigger.ActionType.PlayCutscene:
                RequiredField(action, "director", "Assign the PlayableDirector to play.");
                EditorGUILayout.PropertyField(action.FindPropertyRelative("cutsceneVCam"), new GUIContent("Cutscene VCam"));
                break;

            case UniversalEventTrigger.ActionType.OpenQuestBoard:
                SerializedProperty board = action.FindPropertyRelative("questBoard");
                EditorGUILayout.PropertyField(board);
                if (board.objectReferenceValue == null)
                    EditorGUILayout.LabelField("Empty: uses the scene's QuestBoard.", EditorStyles.miniLabel);
                break;

            case UniversalEventTrigger.ActionType.AddAvailableQuest:
            case UniversalEventTrigger.ActionType.AddActiveQuest:
                RequiredField(action, "quest", "Assign a quest.");
                break;

            case UniversalEventTrigger.ActionType.CompleteObjective:
                RequiredField(action, "quest", "Assign a quest.");
                RequiredField(action, "objectiveId", "Enter the objective id to complete.");
                break;

            case UniversalEventTrigger.ActionType.ReachLocation:
                RequiredField(action, "locationId", "Enter the location id that ReachLocation objectives target.");
                break;
        }
    }

    static void RequiredField(SerializedProperty parent, string name, string missingMessage)
    {
        SerializedProperty prop = parent.FindPropertyRelative(name);
        EditorGUILayout.PropertyField(prop);

        bool missing = prop.propertyType == SerializedPropertyType.String
            ? string.IsNullOrEmpty(prop.stringValue)
            : prop.objectReferenceValue == null;
        if (missing)
            EditorGUILayout.HelpBox(missingMessage, MessageType.Warning);
    }

    void DrawRowButtons(SerializedProperty array, int index)
    {
        using (new EditorGUI.DisabledScope(index == 0))
            if (GUILayout.Button("▲", GUILayout.Width(22)))
                _pendingEdit = () => array.MoveArrayElement(index, index - 1);

        using (new EditorGUI.DisabledScope(index == array.arraySize - 1))
            if (GUILayout.Button("▼", GUILayout.Width(22)))
                _pendingEdit = () => array.MoveArrayElement(index, index + 1);

        if (GUILayout.Button("✕", GUILayout.Width(24)))
            _pendingEdit = () => array.DeleteArrayElementAtIndex(index);
    }

    // Growing a serialized array copies the last element, so new rows are reset to blank.
    static SerializedProperty Append(SerializedProperty array)
    {
        array.arraySize++;
        return array.GetArrayElementAtIndex(array.arraySize - 1);
    }

    static void ResetBranch(SerializedProperty branch)
    {
        branch.FindPropertyRelative("condition").enumValueIndex = (int)UniversalEventTrigger.Condition.Always;
        branch.FindPropertyRelative("quest").objectReferenceValue = null;
        branch.FindPropertyRelative("objectiveId").stringValue = "";
        branch.FindPropertyRelative("actions").arraySize = 0;
    }

    static void ResetAction(SerializedProperty action)
    {
        action.FindPropertyRelative("type").enumValueIndex = (int)UniversalEventTrigger.ActionType.PlayDialog;
        foreach (string field in ActionObjectFields)
            action.FindPropertyRelative(field).objectReferenceValue = null;
        action.FindPropertyRelative("objectiveId").stringValue = "";
        action.FindPropertyRelative("locationId").stringValue = "";
    }

    // ── Collider / layer / test ───────────────────────────────────────────────

    static void DrawColliderSection(UniversalEventTrigger trigger)
    {
        Collider2D col = trigger.GetComponent<Collider2D>();
        if (col == null)
        {
            EditorGUILayout.HelpBox("No Collider2D — the player can't reach this trigger.", MessageType.Warning);
            if (GUILayout.Button("Add CircleCollider2D"))
                Undo.AddComponent<CircleCollider2D>(trigger.gameObject).isTrigger = true;
            return;
        }

        if (trigger.activation == UniversalEventTrigger.Activation.OnEnter && !col.isTrigger)
        {
            EditorGUILayout.HelpBox("On Enter needs the collider's Is Trigger enabled.", MessageType.Warning);
            if (GUILayout.Button("Enable Is Trigger"))
            {
                Undo.RecordObject(col, "Enable Is Trigger");
                col.isTrigger = true;
            }
        }

        if (col is CircleCollider2D circle)
        {
            EditorGUI.BeginChangeCheck();
            float radius = EditorGUILayout.FloatField("Trigger Radius", circle.radius);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(circle, "Change Trigger Radius");
                circle.radius = Mathf.Max(0.1f, radius);
            }
        }
    }

    static void DrawLayerCheck(UniversalEventTrigger trigger)
    {
        if (trigger.activation != UniversalEventTrigger.Activation.OnInteract) return;

        int interactLayer = LayerMask.NameToLayer("Interact");
        if (interactLayer < 0 || trigger.gameObject.layer == interactLayer) return;

        EditorGUILayout.HelpBox("On Interact only works on the 'Interact' layer — the player's interact check ignores other layers.",
            MessageType.Warning);
        if (GUILayout.Button("Move to 'Interact' Layer"))
        {
            Undo.RecordObject(trigger.gameObject, "Move to Interact Layer");
            trigger.gameObject.layer = interactLayer;
        }
    }

    static void DrawTestButton(UniversalEventTrigger trigger)
    {
        EditorGUILayout.Space(4);
        using (new EditorGUI.DisabledScope(!Application.isPlaying))
            if (GUILayout.Button("▶  Fire() Test", GUILayout.Height(26)))
                trigger.Fire();

        if (!Application.isPlaying)
            EditorGUILayout.LabelField("The test button works in Play mode only.", EditorStyles.miniLabel);
    }

    // ── Scene view ────────────────────────────────────────────────────────────

    void OnSceneGUI()
    {
        var trigger = (UniversalEventTrigger)target;
        CircleCollider2D circle = trigger.GetComponent<CircleCollider2D>();
        if (circle == null) return;

        float scale = MaxScale(trigger.transform);
        EditorGUI.BeginChangeCheck();
        float radius = Handles.RadiusHandle(Quaternion.identity, trigger.transform.position, circle.radius * scale);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(circle, "Drag Trigger Radius");
            circle.radius = Mathf.Max(0.1f, radius / scale);
        }
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected | GizmoType.Active)]
    static void DrawRangeGizmo(UniversalEventTrigger trigger, GizmoType gizmoType)
    {
        CircleCollider2D circle = trigger.GetComponent<CircleCollider2D>();
        if (circle == null) return;

        Vector3 center = trigger.transform.position;
        float radius = circle.radius * MaxScale(trigger.transform);
        bool selected = (gizmoType & GizmoType.Selected) != 0;

        Handles.color = selected ? RangeFill : new Color(RangeFill.r, RangeFill.g, RangeFill.b, 0.06f);
        Handles.DrawSolidDisc(center, Vector3.forward, radius);

        Color outline = selected ? RangeOutline : new Color(RangeOutline.r, RangeOutline.g, RangeOutline.b, 0.4f);
        Handles.color = outline;
        Handles.DrawWireDisc(center, Vector3.forward, radius);

        var labelStyle = new GUIStyle { normal = { textColor = outline }, fontStyle = FontStyle.Bold, fontSize = 11 };
        Handles.Label(center + Vector3.right * radius + Vector3.up * 0.15f, $"r = {circle.radius:F2}", labelStyle);
    }

    static float MaxScale(Transform t) => Mathf.Max(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y));
}
