using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

public class CutSceneTrigger : MonoBehaviour
{
    public enum TriggerMode { Auto, Interact }

    [Header("Settings")]
    public TriggerMode mode = TriggerMode.Auto;
    [Tooltip("Once played, never plays again.")]
    public bool playOnce = true;

    [Header("References")]
    public PlayableDirector director;

    [Header("Camera")]
    [Tooltip("Virtual cam active during this cutscene. Leave empty to keep gameplay follow.")]
    public Transform cutsceneVCam;

    bool _playerInRange;
    bool _played;
    bool _cutsceneFinished;
    _2DActions _actions;

    void Awake()
    {
        _actions = new _2DActions();
    }

    void OnEnable() => _actions.Player2D.Interact.Enable();
    void OnDisable() => _actions.Player2D.Interact.Disable();

    void Update()
    {
        if (mode != TriggerMode.Interact || !_playerInRange) return;
        if (_actions.Player2D.Interact.WasPressedThisFrame())
            TryPlay();
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
        if (mode == TriggerMode.Auto)
            TryPlay();
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
    }

    void OnDirectorStopped(PlayableDirector d)
    {
        _cutsceneFinished = true;
        director.stopped -= OnDirectorStopped;
    }

    public void TryPlay()
    {
        if (playOnce && _played) return;
        _played = true;
        StartCoroutine(PlayCutscene());
    }

    IEnumerator PlayCutscene()
    {
        PlayerControl player = PlayerControl.Instance;
        if (player != null)
            player.SetCutsceneMode(true);

        if (cutsceneVCam != null && CameraFollow2D.Instance != null)
            CameraFollow2D.Instance.StartCutscene(cutsceneVCam);

        if (HUDDisplay.Instance != null)
            HUDDisplay.Instance.HUDONOFF(false);

        if (director != null)
        {
            _cutsceneFinished = false;
            director.stopped += OnDirectorStopped;
            director.Play();
            yield return new WaitUntil(() => _cutsceneFinished);
        }

        if (cutsceneVCam != null && CameraFollow2D.Instance != null)
            CameraFollow2D.Instance.EndCutscene();

        if (HUDDisplay.Instance != null)
            HUDDisplay.Instance.HUDONOFF(true);

        if (ScreenFader.Instance != null) ScreenFader.Instance.ResetAll();

        if (player != null)
            player.SetCutsceneMode(false);
    }
}
