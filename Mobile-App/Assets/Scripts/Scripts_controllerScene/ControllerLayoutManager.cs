using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static ActualLayoutData;

public class ControllerLayoutManager : MonoBehaviour
{

    //retrieve the current in use file name

    //find the json in the directory then load it,

    //

    public Canvas mainCanvas;
    public Font defaultFont;
    public GameObject iconPrefab;

    string currentChosenLayoutForUsage;

    private void Start()
    {
        currentChosenLayoutForUsage = LayoutMonitor.Instance.CurrentChosenLayout;

        Debug.Log("Currently using: " + currentChosenLayoutForUsage);

        if (currentChosenLayoutForUsage == "")
        {
            HelperMessageMonitor.Instance.SetHelpMessage("No layout selected.\nPlease go to \"Layouts\" to select one.");

        }
        else
        {
            //load that layout
            LoadTheChosenController();
            
        }

    }

    void LoadTheChosenController()
    {
        //make the path to access the chosen json
        string chosenPath = Path.Combine(Application.persistentDataPath, "UserLayouts", currentChosenLayoutForUsage);

        if (!File.Exists(chosenPath))
        {
            Debug.LogError($"Layout file not found: {chosenPath}");
            HelperMessageMonitor.Instance.SetHelpMessage("No layout selected.\nPlease go to \"Layouts\" to select one.");
            return;
        }

        string json = File.ReadAllText(chosenPath);
        ActualLayoutData theLayoutData = JsonUtility.FromJson<ActualLayoutData>(json);

        //gett screen/canvas dimensions for positioning
        RectTransform canvasRect = mainCanvas.GetComponent<RectTransform>();
        Vector2 canvasSize = canvasRect.rect.size;

        foreach (var indivIcon in theLayoutData.iconList)
        {
            GameObject icon = Instantiate(iconPrefab, mainCanvas.transform);

            Image img = icon.GetComponent<Image>();
            if (img != null) {

                //load sprite by filename
                Sprite sprite = LoadSpriteByName(indivIcon.iconFilename);
                if (sprite != null)
                {
                    img.sprite = sprite;
                }
                else
                {
                    Debug.LogWarning($"Could not load sprite: {indivIcon.iconFilename}");
                }
            }

            RectTransform rt = icon.GetComponent<RectTransform>();

            if (rt != null)
            {
               
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);

            
                float actualX = indivIcon.posX * canvasSize.x; 
                float actualY = indivIcon.posY * canvasSize.y; 

            
                Vector2 anchoredPosition = new Vector2(
                    actualX - (canvasSize.x * 0.5f), 
                    actualY - (canvasSize.y * 0.5f)  
                );

                rt.anchoredPosition = anchoredPosition;
                rt.sizeDelta = new Vector2(indivIcon.sizeX, indivIcon.sizeY);

                
            }


        }

        Sprite LoadSpriteByName(string filename)
        {
            string spriteNameWithoutExtension = Path.GetFileNameWithoutExtension(filename);
            return Resources.Load<Sprite>("_HeathenFlatIconsFree/" + spriteNameWithoutExtension);
        }


        if (!BIGConnectionManager.Instance.IsConnected)
        {
            HelperMessageMonitor.Instance.SetHelpMessage("No connection.");

        }
        else
        {
            //send a json string containing the title and all the actions within the layout

            List<string> layoutActions = GetActionsForCurrentLayout(theLayoutData);

            string layoutTitle = Path.GetFileNameWithoutExtension(currentChosenLayoutForUsage);
            string actionsList = "[" + string.Join(",", layoutActions.Select(action => $"\"{action}\"")) + "]";

            if (layoutTitle.StartsWith("Layout_"))
            {
                layoutTitle = layoutTitle.Substring("Layout_".Length);
            }

            string jsonToSend = $"{{\"type\":\"layout\",\"title\":\"{layoutTitle}\",\"actions\":{actionsList}}}";



            BIGConnectionManager.Instance.Send(jsonToSend);
            Debug.Log(jsonToSend);
        }

        List<string> GetActionsForCurrentLayout(ActualLayoutData layoutData)
        {
            List<string> actions = new List<string>();
            foreach(var icon in layoutData.iconList)
            {
                string spriteName = Path.GetFileNameWithoutExtension(icon.iconFilename);
                string bridgedAction = BridgerScript.Instance?.GetAction(spriteName);

                if (!string.IsNullOrEmpty(bridgedAction))
                {
                    actions.Add(bridgedAction);
                }
                else
                {
                    Debug.LogWarning($"No action mapping found for sprite: {spriteName}");
                }
            }

            return actions;
        }

    }
}
