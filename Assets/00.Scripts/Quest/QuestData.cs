using UnityEngine;

[CreateAssetMenu(fileName = "NewQuest", menuName = "Quest/Quest")]
public class QuestData : ScriptableObject
{
    public string questId;
    public string questName;
    [TextArea(2, 5)] public string info;

    [Space(10)]
    [Tooltip("Objectives for this quest. A quest completes when all non-optional objectives are " +
             "complete, or when any single objective with completesQuest=true is completed.")]
    public QuestObjective[] objectives;

    [Space(10)]
    [Tooltip("Pin position on the board, 0-10 per axis. 5 is centered; 0 is the left/bottom edge, 10 is the right/top edge.")]
    [Range(0f, 10f)] public float pinX = 5f;
    [Range(0f, 10f)] public float pinY = 5f;
    [Tooltip("If true, this quest can be acquired again after being completed. Default (false) = acquirable once.")]
    public bool repeatable;
}
