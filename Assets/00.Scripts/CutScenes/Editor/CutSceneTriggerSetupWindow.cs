// 경로: Assets/Editor/CutSceneTriggerSetupWindow.cs
// Editor 폴더 안에 넣어주세요.

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

public class CutSceneTriggerSetupWindow : EditorWindow
{
    // ── 설정 필드 ──────────────────────────────────────────────
    private UniversalEventTrigger.Activation _mode = UniversalEventTrigger.Activation.OnEnter;
    private bool _playOnce = true;
    private float _radius = 3f;
    private PlayableDirector _director;
    private string _objectName = "CutSceneTrigger";

    // ── 메뉴 등록 ──────────────────────────────────────────────
    [MenuItem("Tools/CutScene Trigger Setup")]
    public static void Open()
    {
        var window = GetWindow<CutSceneTriggerSetupWindow>("CutScene Trigger Setup");
        window.minSize = new Vector2(340, 300);
    }

    // ── GUI ────────────────────────────────────────────────────
    private void OnGUI()
    {
        DrawHeader();
        DrawSettings();
        DrawCreateButton();
    }

    private void DrawHeader()
    {
        GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleCenter
        };

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("🎬 CutScene Trigger 생성", headerStyle);
        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "아래 설정을 입력하고 [씬에 생성] 버튼을 누르면\n" +
            "필요한 컴포넌트가 모두 추가된 오브젝트가 생성됩니다.",
            MessageType.Info);
        EditorGUILayout.Space(10);
    }

    private void DrawSettings()
    {
        EditorGUILayout.LabelField("기본 설정", EditorStyles.boldLabel);

        _objectName = EditorGUILayout.TextField(
            new GUIContent("오브젝트 이름", "씬에 생성될 GameObject 이름"),
            _objectName);

        _mode = (UniversalEventTrigger.Activation)EditorGUILayout.EnumPopup(
            new GUIContent("트리거 모드",
                "On Enter: 범위 진입 즉시 재생\nOn Interact: 범위 안에서 상호작용키 입력 시 재생 (Interact 레이어로 자동 설정)"),
            _mode);

        _playOnce = EditorGUILayout.Toggle(
            new GUIContent("한 번만 재생", "체크 시 컷씬이 한 번 재생되면 다시 트리거되지 않음"),
            _playOnce);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("트리거 범위", EditorStyles.boldLabel);

        _radius = EditorGUILayout.FloatField(
            new GUIContent("반경 (Radius)", "플레이어 감지 범위 (CircleCollider2D 크기)"),
            _radius);

        _radius = Mathf.Max(0.1f, _radius); // 최솟값 보호

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("참조", EditorStyles.boldLabel);

        _director = (PlayableDirector)EditorGUILayout.ObjectField(
            new GUIContent("Playable Director",
                "재생할 Timeline의 PlayableDirector\n(비워두면 나중에 직접 연결 가능)"),
            _director,
            typeof(PlayableDirector),
            true); // 씬 오브젝트 허용
    }

    private void DrawCreateButton()
    {
        EditorGUILayout.Space(16);

        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
        if (GUILayout.Button("✅  씬에 생성", GUILayout.Height(36)))
            CreateTriggerObject();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);

        if (GUILayout.Button("초기화", GUILayout.Height(22)))
            ResetFields();
    }

    // ── 오브젝트 생성 로직 ─────────────────────────────────────
    private void CreateTriggerObject()
    {
        // 1. 이름 유효성 검사
        if (string.IsNullOrWhiteSpace(_objectName))
        {
            EditorUtility.DisplayDialog("오류", "오브젝트 이름을 입력해주세요.", "확인");
            return;
        }

        // 2. 씬뷰 중앙 또는 카메라 앞에 생성
        Vector3 spawnPos = GetSceneViewCenter();

        // 3. GameObject 생성
        GameObject go = new GameObject(_objectName);
        go.transform.position = spawnPos;

        // 4. UniversalEventTrigger 컴포넌트 추가 및 설정 (Always → 컷씬 재생)
        UniversalEventTrigger trigger = go.AddComponent<UniversalEventTrigger>();
        trigger.activation = _mode;
        trigger.triggerOnce = _playOnce;
        trigger.branches = new[]
        {
            new UniversalEventTrigger.EventBranch
            {
                condition = UniversalEventTrigger.Condition.Always,
                actions = new[]
                {
                    new UniversalEventTrigger.EventAction
                    {
                        type = UniversalEventTrigger.ActionType.PlayCutscene,
                        director = _director
                    }
                }
            }
        };

        // Interact 모드는 플레이어의 상호작용 감지가 Interact 레이어만 검사하므로 레이어를 맞춘다
        int interactLayer = LayerMask.NameToLayer("Interact");
        if (_mode == UniversalEventTrigger.Activation.OnInteract && interactLayer >= 0)
            go.layer = interactLayer;

        // 5. CircleCollider2D 추가 (IsTrigger = true)
        CircleCollider2D col = go.AddComponent<CircleCollider2D>();
        col.isTrigger = true;
        col.radius = _radius;

        // 6. Undo 등록 (Ctrl+Z 로 되돌리기 가능)
        Undo.RegisterCreatedObjectUndo(go, "Create CutScene Trigger");

        // 7. 생성된 오브젝트를 Hierarchy에서 선택
        Selection.activeGameObject = go;

        // 8. 씬을 Dirty 처리 (저장 필요 표시)
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[CutSceneTrigger] '{_objectName}' 생성 완료 — 위치: {spawnPos}");
    }

    // ── 씬뷰 중심 좌표 계산 ────────────────────────────────────
    private static Vector3 GetSceneViewCenter()
    {
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView sv = SceneView.lastActiveSceneView;
            // 2D 씬뷰이면 pivot 좌표를 그대로, z는 0
            Vector3 pos = sv.pivot;
            pos.z = 0f;
            return pos;
        }
        return Vector3.zero;
    }

    // ── 필드 초기화 ────────────────────────────────────────────
    private void ResetFields()
    {
        _mode = UniversalEventTrigger.Activation.OnEnter;
        _playOnce = true;
        _radius = 3f;
        _director = null;
        _objectName = "CutSceneTrigger";
    }
}
#endif
