using UnityEngine;

public enum DialogSide { Left, Right }

public enum DialogQuestAction
{
    None,
    AddAvailable,   // quest shows up on the quest board
    AddActive,      // quest is accepted immediately (stage: TalkToClient)
    AdvanceStage    // active quest moves to its next stage; at TalkToPygmalion this completes it
}

[System.Serializable]
public class DialogChoice
{
    [Tooltip("Button label shown to the player.")]
    public string text;

    [Tooltip("Dialog to open when this choice is picked. Leave empty to close the dialog.")]
    public Dialog nextDialog;
}

[System.Serializable]
public class DialogLine
{
    [Tooltip("Character name shown in the name plate.")]
    public string speakerName;

    [Tooltip("Background color of the name plate for this character.")]
    public Color namePlateColor = new Color(0.18f, 0.44f, 0.80f, 1f);

    [Tooltip("Character bust/portrait sprite. Can be different per line for expressions.")]
    public Sprite portrait;

    [Tooltip("Which side of the screen this character stands on.")]
    public DialogSide side = DialogSide.Left;

    [Tooltip("Which portrait slot on that side this line's speaker occupies (0 = first). " +
             "Distinct slots used across the conversation are shown together, evenly spaced.")]
    public int portraitSlot = 0;

    [TextArea(2, 5)]
    public string text;

    [Tooltip("If non-empty, shows a choice prompt after this line instead of auto-advancing.")]
    public DialogChoice[] choices;
}

/// <summary>
/// ScriptableObject holding a full dialog conversation.
/// Each line can have a different speaker, portrait, side, and name plate color.
/// Create via: Assets > Create > Dialog > Dialog
/// </summary>
[CreateAssetMenu(fileName = "NewDialog", menuName = "Dialog/Dialog")]
public class Dialog : ScriptableObject
{
    [Tooltip("Unique id for runtime lookup (e.g. QuestData.dialogID). Must be registered in DialogSystem's registry.")]
    public int dialogId;

    public DialogLine[] lines;

    [Header("On Dialog End")]
    [Tooltip("What to do with the quest below when this dialog closes.")]
    public DialogQuestAction questAction = DialogQuestAction.None;
    [Tooltip("Quest handed to QuestManager when the dialog ends.")]
    public QuestData quest;
}
