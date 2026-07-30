using System.Collections;
using UnityEngine;

public class TimeSystem : MonoBehaviour
{
    public static TimeSystem Instance { get; private set; }

    int _month = 1, _day = 1;
    int _hour = 8, _min = 0;

    public int Month => _month;
    public int Day => _day;
    public int Hour => _hour;
    public int Min => _min;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void StartTimeSet()
    {
        StartCoroutine(timeSetter());
    }
    IEnumerator timeSetter()
    {
        yield return new WaitForSeconds(3f);
        _min += 4;
        if (_min >= 60)
        {
            _min -= 60;
            _hour++;
            if (_hour >= 24)
            {
                _hour -= 24;
                _day++;
                switch (_month)
                {
                    case 1:
                    case 3:
                    case 5:
                    case 7:
                    case 8:
                    case 10:
                    case 12:
                        if (_day > 31)
                        {
                            _day -= 31;
                            _month++;
                        }
                        break;
                    case 4:
                    case 6:
                    case 9:
                    case 11:
                        if (_day > 30)
                        {
                            _day -= 30;
                            _month++;
                        }
                        break;
                    case 2:
                        if (_day > 29)
                        {
                            _day -= 29;
                            _month++;
                        }
                        break;
                }
                if (_month == 13)
                    _month = 1;
            }
        }
    }
    public void LoadFromSave(int month, int day, int hour, int min)
    {
        _month = month;
        _day = day;
        _hour = hour;
        _min = min;
    }
}
