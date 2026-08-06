using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    // ──────────────────────────────────────────────────────────────
    //  Singleton
    // ──────────────────────────────────────────────────────────────
    public static GameManager Instance { get; private set; }

    // ──────────────────────────────────────────────────────────────
    //  Game State
    // ──────────────────────────────────────────────────────────────
    public enum GameState { Boot, MainMenu, Playing, Paused, GameOver }

    public GameState State { get; private set; } = GameState.Boot;

    // ──────────────────────────────────────────────────────────────
    //  Static Events  (cross-system, no Inspector wiring needed)
    // ──────────────────────────────────────────────────────────────
    public static event Action<GameState> OnStateChanged;
    public static event Action OnPlayerDied;
    public static event Action OnGameOver;
    public static event Action OnGamePaused;
    public static event Action OnGameResumed;
    public static event Action OnEnemyKilled;
    public static event Action<string> OnEnemyKilledWithId;

    // ──────────────────────────────────────────────────────────────
    //  Inspector
    // ──────────────────────────────────────────────────────────────
    [Header("Scene Names")]
    [Tooltip("Exact build-settings name of the main menu scene")]
    [SerializeField] private string _mainMenuScene = "MainMenu";
    [Tooltip("Exact build-settings name of the first gameplay scene")]
    [SerializeField] private string _gameplayScene = "Gameplay";
    [Tooltip("Leave empty to skip the loading screen and load directly")]
    [SerializeField] private string _loadingScene = "Loading";

    [Header("Game Over")]
    [Tooltip("Seconds between player death and the Game Over screen firing")]
    [SerializeField] private float _gameOverDelay = 3f;

    [Header("Inspector Events")]
    public UnityEvent onGameOver;
    public UnityEvent onGamePaused;
    public UnityEvent onGameResumed;

    //──────────────────────────────────────────────────────────────
    //  Session Stats  (read by UI, end-screen, etc.)
    // ──────────────────────────────────────────────────────────────
    public int EnemiesKilled { get; private set; }
    public int SliceKills { get; private set; }
    public int DamageTaken { get; private set; }
    public int BloodCollected { get; private set; }
    public float PlayTime { get; private set; }

    // ──────────────────────────────────────────────────────────────
    //  Private
    // ──────────────────────────────────────────────────────────────
    private bool _gameOverTriggered;
    private Coroutine _hitStopCoroutine;

    // ──────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        SetState(GameState.Playing);
    }

    private void Update()
    {
        if (State != GameState.Playing) return;

        PlayTime += Time.deltaTime;
        CheckPlayerDeath();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ──────────────────────────────────────────────────────────────
    //  State Machine
    // ──────────────────────────────────────────────────────────────
    private void SetState(GameState next)
    {
        if (State == next) return;
        State = next;
        OnStateChanged?.Invoke(next);
    }

    // ──────────────────────────────────────────────────────────────
    //  Game Flow — public API
    // ──────────────────────────────────────────────────────────────

    public void StartNewGame()
    {
        ResetSession();
        LoadScene(_gameplayScene);
    }

    public void PauseGame()
    {
        if (State != GameState.Playing) return;
        SetState(GameState.Paused);
        Time.timeScale = 0f;
        OnGamePaused?.Invoke();
        onGamePaused?.Invoke();
    }

    public void ResumeGame()
    {
        if (State != GameState.Paused) return;
        SetState(GameState.Playing);
        Time.timeScale = 1f;
        OnGameResumed?.Invoke();
        onGameResumed?.Invoke();
    }

    public void TogglePause()
    {
        if (State == GameState.Playing) PauseGame();
        else if (State == GameState.Paused) ResumeGame();
    }

    public void RetryGame()
    {
        Time.timeScale = 1f;
        ResetSession();
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        ResetSession();
        LoadScene(_mainMenuScene);
    }

    // ──────────────────────────────────────────────────────────────
    //  Scene Loading
    // ──────────────────────────────────────────────────────────────
    private string _pendingTargetScene;

    public void LoadScene(string sceneName)
    {
        SetState(GameState.Boot);
        StartCoroutine(LoadSceneRoutine(sceneName, Vector2.zero));
        TimeSystem.Instance.stopTimeSet();
    }

    public void LoadScene(string sceneName, Vector2 spawnPosition)
    {
        SetState(GameState.Boot);
        StartCoroutine(LoadSceneRoutine(sceneName, spawnPosition));
        TimeSystem.Instance.stopTimeSet();
    }

    private IEnumerator LoadSceneRoutine(string sceneName, Vector2? spawn)
    {
        _gameOverTriggered = false;

        if (!string.IsNullOrEmpty(_loadingScene))
        {
            _pendingTargetScene = sceneName;
            SceneLoader.Set(sceneName, spawn);
            AsyncOperation op = SceneManager.LoadSceneAsync(_loadingScene);
            while (!op.isDone) yield return null;
        }
        else
        {
            // No loading scene — direct load
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
            while (!op.isDone) yield return null;

            ApplySpawnOverride(spawn);
            SetState(GameState.Playing);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Called after LoadingScreenUI activates the real target scene
        if (scene.name == _loadingScene || scene.name == _mainMenuScene) return;
        if (_pendingTargetScene != null && scene.name == _pendingTargetScene)
        {
            _pendingTargetScene = null;
            ApplySpawnOverride(SceneLoader.SpawnOverride);
            SceneLoader.Clear();
            SetState(GameState.Playing);
            TimeSystem.Instance.StartTimeSet();
        }
    }

    private void ApplySpawnOverride(Vector2? spawn)
    {
        if (spawn.HasValue && PlayerControl.Instance != null)
            PlayerControl.Instance.Teleport(spawn.Value);
    }

    // ──────────────────────────────────────────────────────────────
    //  Game Over
    // ──────────────────────────────────────────────────────────────
    private void CheckPlayerDeath()
    {
        if (_gameOverTriggered) return;
        if (PlayerControl.Instance != null && PlayerControl.Instance.IsDead)
            StartCoroutine(GameOverRoutine());
    }

    private IEnumerator GameOverRoutine()
    {
        _gameOverTriggered = true;
        OnPlayerDied?.Invoke();

        yield return new WaitForSeconds(_gameOverDelay);

        SetState(GameState.GameOver);
        OnGameOver?.Invoke();
        onGameOver?.Invoke();

        _gameOverTriggered = false;
    }

    // ──────────────────────────────────────────────────────────────
    //  Stats Reporting  (called by enemies, player, blood system)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Call from MeleeMonster / PartSliceEnemy on death.
    /// Pass isSlice=true when the kill was delivered via EnemySliceable.
    /// Pass monsterId to drive KillCount quest objectives for that monster.
    /// </summary>
    public void ReportEnemyKill(string monsterId = null, bool isSlice = false)
    {
        EnemiesKilled++;
        if (isSlice) SliceKills++;
        OnEnemyKilled?.Invoke();
        if (!string.IsNullOrEmpty(monsterId))
            OnEnemyKilledWithId?.Invoke(monsterId);
    }

    /// <summary>Call from PlayerControl.TakeDamage to track cumulative damage.</summary>
    public void ReportDamageTaken(int amount)
    {
        DamageTaken += Mathf.Max(0, amount);
    }

    /// <summary>Call from BloodSphere when it adds blood to the player's gauge.</summary>
    public void ReportBloodCollected(int amount)
    {
        BloodCollected += Mathf.Max(0, amount);
    }

    private void ResetSession()
    {
        EnemiesKilled = 0;
        SliceKills = 0;
        DamageTaken = 0;
        BloodCollected = 0;
        PlayTime = 0f;
        _gameOverTriggered = false;
    }

    // ──────────────────────────────────────────────────────────────
    //  HitStop
    // ──────────────────────────────────────────────────────────────
    public void DoHitStop(float duration, float timeScale = 0f)
    {
        if (_hitStopCoroutine != null) StopCoroutine(_hitStopCoroutine);
        _hitStopCoroutine = StartCoroutine(HitStopCoroutine(duration, timeScale));
    }

    private IEnumerator HitStopCoroutine(float duration, float timeScale)
    {
        Time.timeScale = timeScale;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = 1f;
        _hitStopCoroutine = null;
    }

    // ──────────────────────────────────────────────────────────────
    //  Editor Debug
    // ──────────────────────────────────────────────────────────────
#if UNITY_EDITOR
    [ContextMenu("Debug / Force Game Over")]
    private void Debug_ForceGameOver() => StartCoroutine(GameOverRoutine());

    [ContextMenu("Debug / Print Session Stats")]
    private void Debug_PrintStats() =>
            Debug.Log($"[GameManager] Kills={EnemiesKilled} | SliceKills={SliceKills} | DamageTaken={DamageTaken} | BloodCollected={BloodCollected} | PlayTime={PlayTime:F1}s");
#endif
}
