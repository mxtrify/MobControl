using UnityEngine;
using UnityEngine.UI;

public class HelperMessageMonitor : MonoBehaviour
{
    public static HelperMessageMonitor Instance;
    public Font defaultFont;

    private Canvas helperCanvas;
    private GameObject currentMessageObj;

    private Color orange = new Color(1f, 0.6f, 0f) ;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            
            CreateHelperCanvas();
        }
        else
        {
            Destroy(gameObject); 
        }
    }

    private void CreateHelperCanvas()
    {
        
        GameObject canvasGO = new GameObject("HelperCanvas");
        canvasGO.transform.SetParent(transform, false);

        helperCanvas = canvasGO.AddComponent<Canvas>();
        helperCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        helperCanvas.sortingOrder = 9999; // ensure it's on top
        helperCanvas.pixelPerfect = true;

       
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

      
        GraphicRaycaster raycaster = canvasGO.AddComponent<GraphicRaycaster>();
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
    }



    public void SetHelpMessage(string messageValue)
    {
        ClearMessage(); // remove old one if exists

        currentMessageObj = new GameObject("HelperMessage");
        currentMessageObj.transform.SetParent(helperCanvas.transform, false);

        Text text = currentMessageObj.AddComponent<Text>();
        text.text = messageValue;
        text.alignment = TextAnchor.MiddleCenter;
        text.fontSize = 55;
        text.color = orange;
        text.font = defaultFont;
        text.raycastTarget = false;
        Outline outline = currentMessageObj.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2, -2);

        RectTransform rectTransform = text.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(800, 300);
        rectTransform.anchoredPosition = Vector2.zero; // centre
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    }

    public void ClearMessage()
    {
        if (currentMessageObj != null)
        {
            Destroy(currentMessageObj);
            currentMessageObj = null;
        }
    }
}
