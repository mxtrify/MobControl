using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class TopDockLoader : MonoBehaviour
{
    public GameObject iconButtonPrefab;
    public Transform contentPanel;
    public string resourceFolderName = "_HeathenFlatIconsFree";
    public Transform editSpacePanel;

    private List<GameObject> originalIconOrder = new List<GameObject>();

    void Start()
    {
        StartCoroutine(LoadIconsWithDelay());
    }

    void OnEnable()
    {
        
        if (originalIconOrder.Count > 0)
        {
            StartCoroutine(RefreshGrayOutStatesDelayed());
        }
    }

    private IEnumerator RefreshGrayOutStatesDelayed()
    {
        
        yield return null;
        yield return null;

        ApplyInitialGrayOutState();
    }

    private IEnumerator LoadIconsWithDelay()
    {
        LoadIconsIntoDock();

        
        yield return null;

        
        yield return null;

        
        ApplyInitialGrayOutState();
    }

    void LoadIconsIntoDock()
    {
        Sprite[] icons = Resources.LoadAll<Sprite>(resourceFolderName);
        for (int i = 0; i < icons.Length; i++)
        {
            Sprite icon = icons[i];
            GameObject button = Instantiate(iconButtonPrefab, contentPanel);
            button.GetComponent<Image>().sprite = icon;
            button.name = icon.name;

            originalIconOrder.Add(button);

            if (button.GetComponent<CanvasGroup>() == null)
            {
                button.AddComponent<CanvasGroup>();
            }

            DraggableIcon dragger = button.GetComponent<DraggableIcon>();
            if (dragger == null)
            {
                dragger = button.AddComponent<DraggableIcon>();
            }
            dragger.editSpacePanel = editSpacePanel;

            
            button.transform.SetSiblingIndex(i);
        }
    }

    private void ApplyInitialGrayOutState()
    {
        

        foreach (GameObject dockIcon in originalIconOrder)
        {
            if (dockIcon != null)
            {
                Image buttonImage = dockIcon.GetComponent<Image>();
                if (buttonImage != null && buttonImage.sprite != null)
                {
                    string spriteName = buttonImage.sprite.name;
                    bool shouldBeGrayedOut = IsAlreadyInEditSpacePanel(spriteName);

                    DraggableIcon draggable = dockIcon.GetComponent<DraggableIcon>();
                    if (draggable != null)
                    {
                        
                        draggable.SetUsed(shouldBeGrayedOut);
                        
                    }
                }
            }
        }
    }

    private bool IsAlreadyInEditSpacePanel(string spriteFileName)
    {
        if (editSpacePanel == null)
        {
            
            return false;
        }

        

        
        for (int i = 0; i < editSpacePanel.childCount; i++)
        {
            Transform child = editSpacePanel.GetChild(i);
            Image childImage = child.GetComponent<Image>();
            if (childImage != null && childImage.sprite != null)
            {
                string childSpriteName = childImage.sprite.name;
                

                if (childSpriteName == spriteFileName)
                {
                    
                    return true;
                }
            }
        }

        return false;
    }

    
    private bool alreadyInEditSpacePanel(string spriteFileName)
    {
        return IsAlreadyInEditSpacePanel(spriteFileName);
    }

    public void RestoreOriginalOrder()
    {
        for (int i = 0; i < originalIconOrder.Count; i++)
        {
            if (originalIconOrder[i] != null)
            {
                originalIconOrder[i].transform.SetSiblingIndex(i);
            }
        }
    }

    public int GetOriginalIndex(GameObject icon)
    {
        return originalIconOrder.IndexOf(icon);
    }

    
    public void RefreshGrayOutStates()
    {
        ApplyInitialGrayOutState();
    }
}