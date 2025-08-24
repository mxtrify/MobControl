using System.IO;
using System.IO.Enumeration;
using Unity.VisualScripting;

using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class realEditLayoutMANAGER : MonoBehaviour
{

    //to resize preview space panel
    public RectTransform previewSpacePanel;
    public CanvasScaler canvasScaler;

    //for canvas switching between preview & edit
    public Canvas canvas1;
    public Canvas canvas2;

    //
    public RectTransform editSpacePanel;


    //the layout we're using
    string CurrentLayoutFileUsing;

    //loading
    public GameObject iconPrefab; //for icon 

    string layoutFilePath;


    void Start()
    {
        CurrentLayoutFileUsing = LayoutMonitor.Instance.CurrentChosenLayout;

        //build full path after setting CurrentLayoutFileUsing
        layoutFilePath = Path.Combine(Application.persistentDataPath, "UserLayouts", CurrentLayoutFileUsing);

        ResizePanel(); //need to do it on start if not will block buttons
        LoadIconsToPanel(previewSpacePanel);
        Debug.Log(CurrentLayoutFileUsing);
    }

    void ResizePanel()
    {

        Vector2 refResolution = canvasScaler.referenceResolution;

        float targetWidth = (refResolution.x / 6f) * 5f;
        float targetHeight = (refResolution.y / 6f) * 5f;

        previewSpacePanel.anchorMin = new Vector2(0.5f, 0.5f);
        previewSpacePanel.anchorMax = new Vector2(0.5f, 0.5f);
        previewSpacePanel.pivot = new Vector2(0.5f, 0.5f);

        previewSpacePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
        previewSpacePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);

        previewSpacePanel.anchoredPosition = Vector2.zero;
    }


    public void SwitchCanvas()
    {
        if (canvas1.gameObject.activeSelf)
        {
            canvas1.gameObject.SetActive(false);
            canvas2.gameObject.SetActive(true);
            LoadIconsToPanel(editSpacePanel);
        }
        else
        {
            SaveLayout();
            canvas1.gameObject.SetActive(true);
            canvas2.gameObject.SetActive(false);
            LoadIconsToPanel(previewSpacePanel);

        }
    }

    public void deleteTheeLayout()
    {
        File.Delete(layoutFilePath);
        Debug.Log($"Successful delete, layout file: {layoutFilePath}");

        //after delete, redirect back to layout list scene
        HelperMessageMonitor.Instance?.SetHelpMessage("Layout deleted.\nReturning to layouts listing\n....");

        StartCoroutine(WaitAndChangeScene(2));

    }

    public void selectTheeLayout()
    {
        //not useless anymore
        layoutFilePath = Path.Combine(Application.persistentDataPath, "UserLayouts", CurrentLayoutFileUsing);
        Debug.Log($"Current path selected to load layout from: {layoutFilePath}");
        HelperMessageMonitor.Instance?.SetHelpMessage("Layout selected for use.\nRedirecting to activate controller mode\n....");

        StartCoroutine(WaitAndChangeScene(3));

    }

    private IEnumerator WaitAndChangeScene(int sceneNo)
    {
        yield return new WaitForSeconds(3f);
        MoveToScenes.Instance.MoveToScene(sceneNo);
    }

    public void EnsurePanelFullScreen(RectTransform panel)
    {
        if (panel == null) return;
        panel.anchorMin = Vector2.zero;
        panel.anchorMax = Vector2.one;
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = Vector2.zero;
        panel.sizeDelta = Vector2.zero;
    }

    public void SaveLayout()
    {
        if (editSpacePanel == null)
        {
            
            return;
        }

        
        EnsurePanelFullScreen(editSpacePanel);

        
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(editSpacePanel);

        Vector2 panelSize = editSpacePanel.rect.size;
        if (panelSize.x <= 0 || panelSize.y <= 0)
        {
            
            return;
        }

      
        string json = File.ReadAllText(layoutFilePath);
        ActualLayoutData theLayoutData = JsonUtility.FromJson<ActualLayoutData>(json);

      
        theLayoutData.iconList.Clear();

        
        foreach (Transform child in editSpacePanel)
        {
            
            Image imageComponent = child.GetComponent<Image>();
            if (imageComponent == null || imageComponent.sprite == null)
            {
                
                continue; //skip if no sprite found
            }

           
            string fileName = imageComponent.sprite.name + ".png";

            
            RectTransform iconRect = child.GetComponent<RectTransform>();
            if (iconRect == null)
            {
                
                continue; //skip if no RectTransform
            }

          
            Vector2 anchoredPos = iconRect.anchoredPosition;

            
            Vector2 bottomLeftPos = new Vector2(
                anchoredPos.x + (panelSize.x * 0.5f), 
                anchoredPos.y + (panelSize.y * 0.5f)  
            );

           
            float normalizedX = Mathf.Clamp01(bottomLeftPos.x / panelSize.x);
            float normalizedY = Mathf.Clamp01(bottomLeftPos.y / panelSize.y);

            
            theLayoutData.AddIcon(fileName, normalizedX, normalizedY);

           
        }

        
        theLayoutData.UpdateLastModified();

        
        string outputJson = JsonUtility.ToJson(theLayoutData, true);
        File.WriteAllText(layoutFilePath, outputJson);

        
    }


    public void LoadIconsToPanel(RectTransform targetPanel)
    {
        //clear existing icons to avoid duplicates
        foreach (Transform child in targetPanel)
        {
            Destroy(child.gameObject);
        }

        if (!File.Exists(layoutFilePath))
        {
            
            return; //mei you file zhao bu dao 
        }

        string json = File.ReadAllText(layoutFilePath);
        ActualLayoutData layoutData = JsonUtility.FromJson<ActualLayoutData>(json);

       
        if (targetPanel == previewSpacePanel)
        {
            layoutData.ResizingAllIcons(100, 100);
        }

      
        if (targetPanel == editSpacePanel)
        {
            EnsurePanelFullScreen(targetPanel);
        }

        
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(targetPanel);

        Vector2 panelSize = targetPanel.rect.size;
        

        foreach (var iconData in layoutData.iconList)
        {
            GameObject newIcon = Instantiate(iconPrefab, targetPanel);

            Image img = newIcon.GetComponent<Image>();
            if (img != null)
            {
               
                Sprite sprite = LoadSpriteByName(iconData.iconFilename);
                if (sprite != null)
                {
                    img.sprite = sprite;
                }
                else
                {
                    //cannot load Idk
                }
            }

            RectTransform rt = newIcon.GetComponent<RectTransform>();
            if (rt != null)
            {
                
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

                
                
                float actualX = iconData.posX * panelSize.x;
                float actualY = iconData.posY * panelSize.y; 

                
                Vector2 anchoredPosition = new Vector2(
                    actualX - (panelSize.x * 0.5f), 
                    actualY - (panelSize.y * 0.5f)  
                );

                rt.anchoredPosition = anchoredPosition;
                rt.sizeDelta = new Vector2(iconData.sizeX, iconData.sizeY);

                
            }
        }
    }

    Sprite LoadSpriteByName(string filename)
    {
        string spriteNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
        return Resources.Load<Sprite>("_HeathenFlatIconsFree/" + spriteNameWithoutExtension);
    }
}