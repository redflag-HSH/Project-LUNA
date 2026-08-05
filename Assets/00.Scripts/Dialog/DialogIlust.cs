using UnityEngine;

/// <summary>
/// Controls the left and right character portrait SpriteRenderers.
/// Called by DialogSystem each time a new line is shown.
///
/// Each side supports up to MaxPortraitsPerSide simultaneous characters, addressed by
/// DialogLine.portraitSlot. A slot is revealed the first time its side/slot speaks and
/// stays visible (dimmed) afterward; whichever slots are currently revealed on a side are
/// laid out evenly spaced, centered on that side's original Inspector-placed position.
/// </summary>
[RequireComponent(typeof(Camera))]
public class DialogIlust : MonoBehaviour
{
    const int MaxPortraitsPerSide = 3;

    [SerializeField] Camera cam;

    void Awake() => cam = GetComponent<Camera>();

    [Header("Portraits")]
    [Tooltip("Up to 3 SpriteRenderers for the left side. Slot 0's starting position is the group's anchor.")]
    [SerializeField] SpriteRenderer[] portraitsLeft = new SpriteRenderer[MaxPortraitsPerSide];
    [Tooltip("Up to 3 SpriteRenderers for the right side. Slot 0's starting position is the group's anchor.")]
    [SerializeField] SpriteRenderer[] portraitsRight = new SpriteRenderer[MaxPortraitsPerSide];

    [Tooltip("Horizontal distance between adjacent portraits when more than one is revealed on the same side.")]
    [SerializeField] float portraitSpacing = 2.5f;

    [Tooltip("Alpha of a revealed portrait that is NOT the current speaker.")]
    [Range(0f, 1f)]
    [SerializeField] float dimmedAlpha = 0.45f;

    Vector3 leftAnchor;
    Vector3 rightAnchor;
    readonly bool[] leftRevealed = new bool[MaxPortraitsPerSide];
    readonly bool[] rightRevealed = new bool[MaxPortraitsPerSide];

    void Start()
    {
        leftAnchor = portraitsLeft.Length > 0 && portraitsLeft[0] != null
            ? portraitsLeft[0].transform.localPosition : Vector3.zero;
        rightAnchor = portraitsRight.Length > 0 && portraitsRight[0] != null
            ? portraitsRight[0].transform.localPosition : Vector3.zero;

        ResetRevealed();
        ToggleShow();
        if (DialogSystem.Instance != null)
            DialogSystem.Instance.dialogIlust = this;
    }

    public void Apply(DialogLine line)
    {
        bool leftSpeaking = line.side == DialogSide.Left;
        int slot = Mathf.Clamp(line.portraitSlot, 0, MaxPortraitsPerSide - 1);

        SpriteRenderer[] speakingPortraits = leftSpeaking ? portraitsLeft : portraitsRight;
        bool[] speakingRevealed = leftSpeaking ? leftRevealed : rightRevealed;

        speakingRevealed[slot] = true;
        if (line.portrait != null && slot < speakingPortraits.Length && speakingPortraits[slot] != null)
            speakingPortraits[slot].sprite = line.portrait;

        LayoutSide(portraitsLeft, leftRevealed, leftAnchor);
        LayoutSide(portraitsRight, rightRevealed, rightAnchor);

        // A speaking line with no portrait sprite hides that slot for this beat (e.g. an
        // off-screen/narration line), even if it was revealed earlier in the conversation.
        bool hideSpeakingSlot = line.portrait == null;
        ApplyAlpha(portraitsLeft, leftRevealed, leftSpeaking ? slot : -1, leftSpeaking && hideSpeakingSlot);
        ApplyAlpha(portraitsRight, rightRevealed, !leftSpeaking ? slot : -1, !leftSpeaking && hideSpeakingSlot);
    }

    /// <summary>Evenly spaces every currently-revealed portrait on a side, centered on its anchor.</summary>
    void LayoutSide(SpriteRenderer[] portraits, bool[] revealed, Vector3 anchor)
    {
        int count = 0;
        for (int i = 0; i < portraits.Length; i++)
            if (revealed[i] && portraits[i] != null) count++;

        int rank = 0;
        for (int i = 0; i < portraits.Length; i++)
        {
            if (!revealed[i] || portraits[i] == null) continue;
            Vector3 pos = anchor;
            pos.x += (rank - (count - 1) / 2f) * portraitSpacing;
            portraits[i].transform.localPosition = pos;
            rank++;
        }
    }

    void ApplyAlpha(SpriteRenderer[] portraits, bool[] revealed, int speakingSlot, bool hideSpeakingSlot)
    {
        for (int i = 0; i < portraits.Length; i++)
        {
            if (portraits[i] == null) continue;

            float alpha;
            if (!revealed[i]) alpha = 0f;
            else if (i == speakingSlot) alpha = hideSpeakingSlot ? 0f : 1f;
            else alpha = dimmedAlpha;

            SetAlpha(portraits[i], alpha);
        }
    }

    void ResetRevealed()
    {
        for (int i = 0; i < MaxPortraitsPerSide; i++)
        {
            leftRevealed[i] = false;
            rightRevealed[i] = false;
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

    public void ToggleShow()
    {
        bool isActive = portraitsLeft.Length > 0 && portraitsLeft[0] != null && portraitsLeft[0].gameObject.activeSelf;
        SetActiveAll(portraitsLeft, !isActive);
        SetActiveAll(portraitsRight, !isActive);

        if (!isActive) return;

        ResetRevealed();
        ApplyAlpha(portraitsLeft, leftRevealed, -1, false);
        ApplyAlpha(portraitsRight, rightRevealed, -1, false);
    }

    static void SetActiveAll(SpriteRenderer[] portraits, bool active)
    {
        foreach (var p in portraits)
            if (p != null) p.gameObject.SetActive(active);
    }
}
