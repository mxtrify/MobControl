using UnityEngine;
using UnityEngine.SceneManagement;


//HOME 0
//settingsScene 1
//editLayoutScene 2
//controllerScene 3
//realEditLayoutScene 4
//qrScannerScene 5

//ManualInputScene 6


public class MoveToScenes : MonoBehaviour
{
    public static MoveToScenes Instance;

    private void Awake()
    {
        
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); //singleton

        
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
       
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Debug.Log($"Scene loaded: {scene.name}");

       
        StartCoroutine(ReconnectButtonsAfterFrame());

       
    }

    private System.Collections.IEnumerator ReconnectButtonsAfterFrame()
    {
        
        yield return null;

        string currentSceneName = SceneManager.GetActiveScene().name;

        switch (currentSceneName)
        {
            case "HOME":
                ConnectHomeButtons();
                break;
            case "controllerScene":
                ConnectControllerButtons(); 
                break;
            case "editLayoutScene":
                ConnectEditLayoutButtons();
                break;
            case "realEditLayoutScene":
                ConnectRealEditLayoutButtons();
                break;

            case "settingsScene":
                ConnectSettingsButton();
                break;

            case "qrScannerScene":
                ConnectQrScannerButton();
                break;
            case "ManualInputScene":
                ConnectManualInputButton();
                break;
        }
    }

    private void ConnectHomeButtons()
    {
       
        var settingsButton = GameObject.Find("settingsButton")?.GetComponent<UnityEngine.UI.Button>();
        var controllerButton = GameObject.Find("controllerButton")?.GetComponent<UnityEngine.UI.Button>();
        var editLayoutButton = GameObject.Find("editLayoutButton")?.GetComponent<UnityEngine.UI.Button>();

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveAllListeners();
            settingsButton.onClick.AddListener(() => MoveToScene(1));
        }

        

        if (controllerButton != null)
        {
            controllerButton.onClick.RemoveAllListeners();
            controllerButton.onClick.AddListener(() => MoveToScene(3)); 
        }

        if (editLayoutButton != null)
        {
            editLayoutButton.onClick.RemoveAllListeners();
            editLayoutButton.onClick.AddListener(() => MoveToScene(2));
        }

        Debug.Log("HOME buttons reconnected");
    }

    private void ConnectSettingsButton()
    {
        var backHomeButton_S = GameObject.Find("backHomeButton_S")?.GetComponent<UnityEngine.UI.Button>();
        var go2QRScannerButton = GameObject.Find("go2QRScannerButton")?.GetComponent<UnityEngine.UI.Button>();
        var go2ManualInputConnectButton = GameObject.Find("go2ManualInputConnectButton")?.GetComponent<UnityEngine.UI.Button>();

        if (backHomeButton_S != null)
        {
            Debug.Log("backHomeButton_2 pressed.");
            backHomeButton_S.onClick.RemoveAllListeners();
            backHomeButton_S.onClick.AddListener(() => MoveToScene(0));
        }

        if (go2QRScannerButton != null)
        {
            Debug.Log("go2QRScannerButton pressed.");
            go2QRScannerButton.onClick.RemoveAllListeners();
            go2QRScannerButton.onClick.AddListener(() => MoveToScene(5));
        }

        if (go2ManualInputConnectButton != null)
        {
            Debug.Log("go2ManualInputConnectButton pressed.");
            go2ManualInputConnectButton.onClick.RemoveAllListeners();
            go2ManualInputConnectButton.onClick.AddListener(() => MoveToScene(6));
        }
    }

    private void ConnectQrScannerButton()
    {
        var back2settingsButton = GameObject.Find("back2settingsButton")?.GetComponent<UnityEngine.UI.Button>();

        if (back2settingsButton != null)
        {
            Debug.Log("back2settingsButton pressed.");
            back2settingsButton.onClick.RemoveAllListeners();
            back2settingsButton.onClick.AddListener(() => MoveToScene(1));
        }
    }

    private void ConnectManualInputButton()
    {
        var backSettingsButton = GameObject.Find("backSettingsButton")?.GetComponent<UnityEngine.UI.Button>();
        if (backSettingsButton != null)
        {
            Debug.Log("backSettingsButton pressed.");
            backSettingsButton.onClick.RemoveAllListeners();
            backSettingsButton.onClick.AddListener(() => MoveToScene(1));
        }
    }

    private void ConnectControllerButtons()
    {
        var backhomeButton = GameObject.Find("backhomeButton")?.GetComponent<UnityEngine.UI.Button>();

        if (backhomeButton != null)
        {
            Debug.Log("backhomeButton pressed.");
            backhomeButton.onClick.RemoveAllListeners();
            backhomeButton.onClick.AddListener(() => MoveToScene(0));
        }
    }

    private void ConnectEditLayoutButtons()
    {
        
        var back2HomeButton = GameObject.Find("back2HomeButton")?.GetComponent<UnityEngine.UI.Button>();
        var createNewLayoutButton = GameObject.Find("createNewLayoutButton")?.GetComponent<UnityEngine.UI.Button>();

        if (back2HomeButton != null)
        {
            Debug.Log("back2HomeButton pressed.");
            back2HomeButton.onClick.RemoveAllListeners();
            back2HomeButton.onClick.AddListener(() => MoveToScene(0));
        }

        if (createNewLayoutButton != null)
        {
            createNewLayoutButton.onClick.RemoveAllListeners();

            createNewLayoutButton.onClick.AddListener(() => MoveToScene(4));
        }

        Debug.Log("EditLayout buttons reconnected");
    }

    private void ConnectRealEditLayoutButtons()
    {
        var back2listButton = GameObject.Find("back2listButton")?.GetComponent<UnityEngine.UI.Button>();
        
        //var deleteButton = GameObject.Find("deleteButton")?.GetComponent<UnityEngine.UI.Button>();
        
        if (back2listButton != null)
        {
            Debug.Log("back2listButton pressed.");
            back2listButton.onClick.RemoveAllListeners();
            back2listButton.onClick.AddListener(() => MoveToScene(2));
        }

        //if (deleteButton != null)
        //{
        //    Debug.Log("deleteButton pressed.");
        //    deleteButton.onClick.RemoveAllListeners();
        //    deleteButton.onClick.AddListener(() => MoveToScene(2));
        //}


        Debug.Log("real EditLayout buttons reconnected");
    }

    public void MoveToScene(int sceneIndex)
    {
        SceneManager.LoadScene(sceneIndex);
    }




    
    public void MovingToLayoutEditor(int sceneIndex)
    {
        
        SceneManager.LoadScene("realEditLayoutScene");
    }
}