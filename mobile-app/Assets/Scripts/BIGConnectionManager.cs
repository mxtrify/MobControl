using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NativeWebSocket;
using UnityEngine;
using UnityEngine.UI;


public class BIGConnectionManager : MonoBehaviour
{
    
    public static BIGConnectionManager Instance {  get; private set; }

    public WebSocket Socket { get; private set; }
    public bool IsConnected => Socket != null && Socket.State == WebSocketState.Open;

    //connection status indicato
    public Text connectionStatusText;
    public Font defaultFont;

    private string logBuffer = "";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        spawnConnectionText();
    }

    void Start()
    {
        UpdateConnectionText();
    }
   

    public async Task Connectreally(string url)
    {

        

        if (Socket != null && Socket.State == WebSocketState.Open)
        {
            await Socket.Close();
            SafeLog("WebSocket Closed before reconnecting.");
        }

        Socket = new WebSocket(url);

        Socket.OnOpen += () =>
        {
            
            SafeLog("WebSocket Connected.");

            //send identify device name over

            string deviceName = SystemInfo.deviceName;
            string jsonIdentify = $"{{\"type\":\"identify\",\"deviceName\":\"{deviceName}\"}}";
            Send(jsonIdentify);

            Debug.Log("IDENTIFY SENT: " + jsonIdentify);



        };
        Socket.OnError += (err) =>
        {
           
            SafeLog("WebSocket Error: " + err);
        };
        Socket.OnClose += (code) =>
        {
            
            SafeLog("WebSocket Closed. Code: " + code);
        };

        await Socket.Connect();

       
        SafeLog("Connect completed.");
    }

    private void SafeLog(string message)
    {
        try
        {
            
            Debug.Log(message);
            //logBuffer += message + "\n";
            //UpdateConnectionText(logBuffer);
        }
        catch
        {

            //ignoring any logging errors
            //don't even log the error to avoid potential recursion
        }
    }


    public void Send(string message)
    {
        if (IsConnected)
        {
            Socket.SendText(message);
        }
        else
        {
            
            SafeLog("Cannot send message. WebSocket not connected.");
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        Socket?.DispatchMessageQueue();
#endif
        if (connectionStatusText != null)
        {
            string status = IsConnected ? "<color=green>Connected</color>" : "<color=red>NOT CONNECTED</color>";
            connectionStatusText.text = status + "\n" + logBuffer;
        }
    }

    private async void OnApplicationQuit()
    {
        if (Socket != null)
            await Socket.Close();
    }

    public async void Disconnect()
    {
        if (Socket != null)
        {
            if (Socket.State == WebSocketState.Open || Socket.State == WebSocketState.Connecting)
            {
                try
                {
                    await Socket.Close();
                    SafeLog("WebSocket manually disconnected.");
                }
                catch (Exception ex)
                {
                    SafeLog("Error while disconnecting: " + ex.Message);
                }
            }
            else
            {
                SafeLog("Disconnect called, but socket already closed.");
            }

            Socket = null;
        }

        UpdateConnectionText();
    }

    private void UpdateConnectionText()
    {
        if (connectionStatusText != null)
        {
            connectionStatusText.text = (IsConnected ? "<color=green>Connected</color>" : "<color=red>NOT CONNECTED</color>");

        }
    }

    private void spawnConnectionText()
    {
        if (connectionStatusText != null)
        {
            return;
        }

        //create, set up canvas
        GameObject CSC = new GameObject("ConnectionStatusCanvas");
        CSC.transform.SetParent(transform);
        Canvas canvas = CSC.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CSC.AddComponent<CanvasScaler>();
        CSC.AddComponent<GraphicRaycaster>();

        //turn off raycast

        DontDestroyOnLoad(CSC);

        GameObject textObj = new GameObject("ConnectionStatusText");
        textObj.transform.SetParent(CSC.transform);
        connectionStatusText = textObj.AddComponent<Text>();
        connectionStatusText.font = defaultFont;
        connectionStatusText.alignment = TextAnchor.UpperRight;
        connectionStatusText.horizontalOverflow = HorizontalWrapMode.Overflow;
        connectionStatusText.verticalOverflow = VerticalWrapMode.Overflow;
        connectionStatusText.fontSize = 35;
        Outline outline = textObj.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2, -2);

        connectionStatusText.raycastTarget = false;

        //add black outline component

        RectTransform rect = connectionStatusText.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(1, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(1, 1);
        rect.anchoredPosition = new Vector2(-10, -10);
        rect.sizeDelta = new Vector2(400, 300);

    }

}
