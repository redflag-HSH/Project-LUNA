using UnityEngine;

/// <summary>
/// Controls the left and right character portraits. Called by DialogSystem each time a
/// new line is shown.
///
/// Each side supports up to MaxPortraitsPerSide simultaneous characters, addressed by
/// DialogLine.portraitSlot. A slot's SpriteRenderer is instantiated from portraitPrefab
/// the first time its side/slot speaks, and destroyed when the conversation closes.
/// Whichever slots are currently revealed on a side are laid out evenly spaced, centered
/// on that side's anchor position.
/// </summary>
[RequireComponent(typeof(Camera))]
public class DialogIlust : MonoBehaviour
{
    const int MaxPortraitsPerSide = 3;

    [SerializeField] Camera cam;

    void Awake() => cam = GetComponent<Camera>();

    [Header("Portraits")]
    [Tooltip("Prefab with a SpriteRenderer on its root. One instance is spawned per revealed slot.")]
    [SerializeField] GameObject portraitPrefab;

    [Header("Anchor Layout")]
    [Tooltip("Horizontal anchor position as a fraction of the camera's half-width from center (0 = center, 1 = edge of frame).")]
    [Range(0f, 1f)]
    [SerializeField] float horizontalAnchorRatio = 0.6f;

    [Tooltip("Vertical anchor position as a fraction of the camera's half-height from center (-1 = bottom, 1 = top).")]
    [Range(-1f, 1f)]
    [SerializeField] float verticalAnchorRatio = 0f;

    [Tooltip("Local Z offset for spawned portraits (render depth relative to the camera).")]
    [SerializeField] float anchorDepth = 1.1f;

    [Tooltip("Horizontal distance between adjacent portraits when more than one is revealed on the same side.")]
    [SerializeField] float portraitSpacing = 2.5f;

    [Tooltip("Alpha of a revealed portrait that is NOT the current speaker.")]
    [Range(0f, 1f)]
    [SerializeField] float dimmedAlpha = 0.45f;

    // null = slot not yet revealed this conversation.
    readonly SpriteRenderer[] leftPortraits = new SpriteRenderer[MaxPortraitsPerSide];
    readonly SpriteRenderer[] rightPortraits = new SpriteRenderer[MaxPortraitsPerSide];

    bool isShowing;

    void Start()
    {
        if (DialogSystem.Instance != null)
            DialogSystem.Instance.dialogIlust = this;
    }

    public void Apply(DialogLine line)
    {
        bool leftSpeaking = line.side == DialogSide.Left;
        int slot = Mathf.Clamp(line.portraitSlot, 0, MaxPortraitsPerSide - 1);
        SpriteRenderer[] speakingPortraits = leftSpeaking ? leftPortraits : rightPortraits;

        if (line.portrait != null)
        {
            if (speakingPortraits[slot] == null)
                speakingPortraits[slot] = SpawnPortrait();
            if (speakingPortraits[slot] != null)
                speakingPortraits[slot].sprite = line.portrait;
        }

        LayoutSide(leftPortraits, ComputeAnchor(-1));
        LayoutSide(rightPortraits, ComputeAnchor(1));

        // A speaking line with no portrait sprite hides that slot for this beat (e.g. an
        // off-screen/narration line), even if it was revealed earlier in the conversation.
        bool hideSpeakingSlot = line.portrait == null;
        ApplyAlpha(leftPortraits, leftSpeaking ? slot : -1, leftSpeaking && hideSpeakingSlot);
        ApplyAlpha(rightPortraits, !leftSpeaking ? slot : -1, !leftSpeaking && hideSpeakingSlot);
    }

    SpriteRenderer SpawnPortrait()
    {
        if (portraitPrefab == null) return null;
        var go = Instantiate(portraitPrefab, transform);
        if (go.TryGetComponent<SpriteRenderer>(out var sr)) return sr;

        Debug.LogError("[DialogIlust] portraitPrefab has no SpriteRenderer on its root.", go);
        Destroy(go);
        return null;
    }

    /// <summary>Derives a side's anchor from the camera's current orthographic size/aspect,
    /// so layout stays correctly framed regardless of camera size or aspect ratio.</summary>
    /// <param name="sideSign">-1 for the left side, +1 for the right side.</param>
    Vector3 ComputeAnchor(int sideSign)
    {
        float halfHeight = cam != null ? cam.orthographicSize : 5f;
        float halfWidth = cam != null ? halfHeight * cam.aspect : halfHeight;
        return new Vector3(sideSign * halfWidth * horizontalAnchorRatio, halfHeight * verticalAnchorRatio, anchorDepth);
    }

    /// <summary>Evenly spaces every currently-revealed portrait on a side, centered on its anchor.</summary>
    void LayoutSide(SpriteRenderer[] portraits, Vector3 anchor)
    {
        int count = 0;
        for (int i = 0; i < portraits.Length; i++)
            if (portraits[i] != null) count++;

        int rank = 0;
        for (int i = 0; i < portraits.Length; i++)
        {
            if (portraits[i] == null) continue;
            Vector3 pos = anchor;
            pos.x += (rank - (count - 1) / 2f) * portraitSpacing;
            portraits[i].transform.localPosition = pos;
            rank++;
        }
    }

    void ApplyAlpha(SpriteRenderer[] portraits, int speakingSlot, bool hideSpeakingSlot)
    {
        for (int i = 0; i < portraits.Length; i++)
        {
            if (portraits[i] == null) continue;
            float alpha = i == speakingSlot ? (hideSpeakingSlot ? 0f : 1f) : dimmedAlpha;
            SetAlpha(portraits[i], alpha);
        }
    }

    void OnDrawGizmos()
    {
        if (cam == null) return;

        float height = cam.orthographicSize * 2f;
        float width = height * cam.aspect;

        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawWireCube(transform.position, new Vector3(width, height, 0f));
    }

    static void SetAlpha(SpriteRenderer sr, float alpha)
    {
        Color c = sr.color;
        c.a = alpha;
        sr.color = c;
    }

    /// <summary>Toggles between conversation-open and conversation-closed. On close, every
    /// spawned portrait is destroyed so the next conversation starts fresh.</summary>
    public void ToggleShow()
    {
        isShowing = !isShowing;
        if (isShowing) return;

        ClearSide(leftPortraits);
        ClearSide(rightPortraits);
    }

    void ClearSide(SpriteRenderer[] portraits)
    {
        for (int i = 0; i < portraits.Length; i++)
        {
            if (portraits[i] != null) Destroy(portraits[i].gameObject);
            portraits[i] = null;
        }
    }
}
