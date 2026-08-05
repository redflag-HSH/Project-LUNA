using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class QuestTest : MonoBehaviour, IInteractable
{
    [field: SerializeField] public List<QuestData> Quests { get; private set; }
    int a;
    void Start()
    {
        a = 0;
    }
    public void Interact(GameObject interactor)
    {
        Trigger(a);
    }

    public void Trigger(int input)
    {
        if (a >= Quests.Count)
            return;
        QuestData q = Quests[input];
        QuestManager.Instance.AddAvailableQuest(q);
        Debug.Log(a + "th quest accepted");
        a++;
    }
}