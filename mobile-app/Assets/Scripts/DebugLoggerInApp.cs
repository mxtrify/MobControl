using TMPro;
using UnityEngine;

public class DebugLoggerInApp : MonoBehaviour
{
    public static DebugLoggerInApp Instance;
    public TextMeshProUGUI logText;
    private static string fullLog = "";


    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        fullLog = logString + "\n" + fullLog;

        string[] lines = fullLog.Split('\n');
        if (lines.Length > 20)
        {
            fullLog = string.Join("\n", lines, 0, 20);

        }
        logText.text = fullLog;
    }
}
