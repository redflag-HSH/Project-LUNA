using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

public class QuestBoard : MonoBehaviour
{
    [SerializeField] GameObject pinPrefab;
    void Start()
    {
        Show(false);
    }
    public void Show(bool onoff)
    {
        gameObject.SetActive(onoff);
        if (onoff)
            SetUpQuestPins();
        else
            PlayerControl.Instance.SetInputEnabled(true);
    }
    void SetUpQuestPins()
    {
        // Show(true) can run repeatedly (interact key stays live while the
        // board is open) — rebuild pins from scratch each time.
        foreach (QuestPin old in GetComponentsInChildren<QuestPin>(true))
            Destroy(old.gameObject);

        Rect boardRect = GetComponent<RectTransform>().rect;

        foreach (QuestData q in QuestManager.Instance.AvailableQuests)
        {
            GameObject ig = Instantiate(pinPrefab, transform);
            QuestPin pin = ig.GetComponent<QuestPin>();
            // pinX/pinY are 0-10 sliders; 5 = board center, 0/10 = the board's edges.
            // Scaling by the board's current rect keeps pins in place across canvas sizes.
            float x = (q.pinX / 10f - 0.5f) * boardRect.width;
            float y = (q.pinY / 10f - 0.5f) * boardRect.height;
            ig.GetComponent<RectTransform>().localPosition = new Vector2(x, y);
            pin.quest = q;
            pin.dialog = DialogSystem.Instance != null ? DialogSystem.Instance.GetDialogById(q.dialogID) : null;
        }
    }
}
