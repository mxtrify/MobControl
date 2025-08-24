using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

//there will be a moment where it cannot find EditSpacePanel
// because it is not active
//during the preview, just ignore

public class DraggableIcon : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public static Transform dragLayer;
    public Transform editSpacePanel;

    private Canvas canvas;
    private RectTransform rectTransform;
    private CanvasGroup canvasGroup;
    private Transform originalParent;
    private Vector3 originalPosition;
    private bool isInEditSpace = false;
    private bool isUsed = false;

    private DraggableIcon originalDockIcon;
    private GameObject editSpaceCopy;

    private Image iconImage;
    private Color originalColor;
    private Color usedColor = new Color(0.8f, 0.8f, 0.8f, 1f); //grey

    private GameObject dragPreview;
    private bool isDragging = false;

    void Start()
    {
        canvas = GetComponentInParent<Canvas>();
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        originalParent = transform.parent;
        originalPosition = transform.localPosition;

        iconImage = GetComponent<Image>();
        if (iconImage != null)
        {
            originalColor = new Color(1f, 1f, 0.514f, 1f);
            
        }
        else
        {
            
        }

        FindEditSpacePanel();
        CheckIfInEditSpace();

        
        if (isInEditSpace)
        {
            FindAndLinkToDockIcon();
        }
    }

    private void FindAndLinkToDockIcon()
    {
        if (iconImage?.sprite == null) return;

        string mySpriteName = iconImage.sprite.name;
        

        Transform contentPanel = FindContentPanel();
        if (contentPanel == null)
        {
            
            return;
        }

        

        foreach (Transform child in contentPanel)
        {
            DraggableIcon dockIcon = child.GetComponent<DraggableIcon>();
            if (dockIcon != null && dockIcon.iconImage?.sprite != null)
            {
                string dockSpriteName = dockIcon.iconImage.sprite.name;
                

                if (string.Equals(dockSpriteName, mySpriteName, System.StringComparison.Ordinal))
                {
                    
                    originalDockIcon = dockIcon;
                    dockIcon.editSpaceCopy = this.gameObject;
                    dockIcon.SetUsedState(true);
                    
                    return;
                }
            }
            else if (dockIcon != null)
            {
                
            }
        }

        
    }

    private Transform FindContentPanel()
    {
        GameObject outerDockPanel = GameObject.Find("OuterTopDockPanel");
        if (outerDockPanel != null)
        {
            

            
            Transform topDockPanelScroll = outerDockPanel.transform.Find("TopDockPanelScroll");
            if (topDockPanelScroll != null)
            {
                

                Transform viewport = topDockPanelScroll.Find("viewport");
                if (viewport != null)
                {
                    

                    Transform content = viewport.Find("content");
                    if (content != null)
                    {
                        
                        return content;
                    }
                    else
                    {
                        
                        
                        
                        for (int i = 0; i < viewport.childCount; i++)
                        {
                            Debug.Log($"  - {viewport.GetChild(i).name}");
                        }
                    }
                }
                else
                {
                    
                    
                    
                    for (int i = 0; i < topDockPanelScroll.childCount; i++)
                    {
                        Debug.Log($"  - {topDockPanelScroll.GetChild(i).name}");
                    }
                }
            }
            else
            {
                
                
                
                for (int i = 0; i < outerDockPanel.transform.childCount; i++)
                {
                    Debug.Log($"  - {outerDockPanel.transform.GetChild(i).name}");
                }
            }

            
            
            Transform fallbackContent = FindChildRecursive(outerDockPanel.transform, "content");
            if (fallbackContent != null)
            {
                
                return fallbackContent;
            }
        }
        else
        {
            
        }

        return null;
    }

    
    private Transform FindChildRecursive(Transform parent, string childName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }

            Transform foundInChild = FindChildRecursive(child, childName);
            if (foundInChild != null)
            {
                return foundInChild;
            }
        }
        return null;
    }

    private void FindEditSpacePanel()
    {
        GameObject found = GameObject.Find("EditSpacePanel");
        if (found != null)
        {
            editSpacePanel = found.transform;
        }
        else
        {
            //preview modde no draggable
        }
    }

    private void CheckIfInEditSpace()
    {
        if (editSpacePanel != null)
        {
            isInEditSpace = transform.IsChildOf(editSpacePanel);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        isDragging = true;

        if (isInEditSpace)
        {
            originalParent = transform.parent;
            originalPosition = transform.localPosition;

            if (dragLayer != null)
            {
                transform.SetParent(dragLayer);
            }

            canvasGroup.alpha = 0.8f;
            canvasGroup.blocksRaycasts = false;
        }
        else
        {
            if (dragLayer != null)
            {
                dragPreview = Instantiate(gameObject, dragLayer);

                DraggableIcon previewDragger = dragPreview.GetComponent<DraggableIcon>();
                if (previewDragger != null)
                {
                    previewDragger.enabled = false;
                }

                CanvasGroup previewCanvasGroup = dragPreview.GetComponent<CanvasGroup>();
                if (previewCanvasGroup != null)
                {
                    previewCanvasGroup.alpha = 0.8f;
                    previewCanvasGroup.blocksRaycasts = false;
                }

                RectTransform previewRect = dragPreview.GetComponent<RectTransform>();
                Vector2 localPoint;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    dragLayer.GetComponent<RectTransform>(),
                    eventData.position,
                    canvas.worldCamera,
                    out localPoint);
                previewRect.anchoredPosition = localPoint;
            }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        if (isInEditSpace)
        {
            rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }
        else if (dragPreview != null)
        {
            RectTransform previewRect = dragPreview.GetComponent<RectTransform>();
            previewRect.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        isDragging = false;

        bool droppedOnDock = IsDroppedOnDock(eventData);
        bool droppedOnEditSpace = !droppedOnDock && IsDroppedOnEditSpace(eventData);

        if (isInEditSpace)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;

            if (droppedOnDock)
            {
                RestoreOriginalDockIcon();
                Destroy(gameObject);
            }
            else if (droppedOnEditSpace)
            {
                MoveToEditSpace(eventData.position);
            }
            else
            {
                transform.SetParent(originalParent);
                transform.localPosition = originalPosition;
            }
        }
        else
        {
            if (droppedOnEditSpace)
            {
                CreateCopyInEditSpace(eventData.position);
                SetUsedState(true);
            }

            if (dragPreview != null)
            {
                Destroy(dragPreview);
                dragPreview = null;
            }
        }
    }

    private bool IsDroppedOnDock(PointerEventData eventData)
    {
        Transform outerDockPanel = GameObject.Find("OuterTopDockPanel")?.transform;
        if (outerDockPanel == null) return false;

        if (!outerDockPanel.gameObject.activeInHierarchy) return false;

        RectTransform dockRect = outerDockPanel.GetComponent<RectTransform>();
        if (dockRect == null) return false;

        Vector2 localPoint;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dockRect,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint) &&
            dockRect.rect.Contains(localPoint);
    }

    private bool IsDroppedOnEditSpace(PointerEventData eventData)
    {
        if (editSpacePanel == null) return false;

        RectTransform editSpaceRect = editSpacePanel.GetComponent<RectTransform>();
        Vector2 localPoint;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            editSpaceRect,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint) &&
            editSpaceRect.rect.Contains(localPoint);
    }

    private void CreateCopyInEditSpace(Vector2 screenPosition)
    {
        if (editSpacePanel == null)
        {
            FindEditSpacePanel();
            if (editSpacePanel == null)
            {
                
                return;
            }
        }

        
        string thisSpriteName = iconImage?.sprite?.name;
        if (!string.IsNullOrEmpty(thisSpriteName))
        {
            foreach (Transform child in editSpacePanel)
            {
                DraggableIcon childIcon = child.GetComponent<DraggableIcon>();
                if (childIcon != null)
                {
                    string childSpriteName = childIcon.iconImage?.sprite?.name;
                    if (childSpriteName == thisSpriteName)
                    {
                        
                        return; //skipskip creation if icon already in
                    }
                }
            }
        }

        GameObject copy = Instantiate(this.gameObject, editSpacePanel);
        editSpaceCopy = copy;

        DraggableIcon copyDragger = copy.GetComponent<DraggableIcon>();
        if (copyDragger != null)
        {
            copyDragger.editSpacePanel = editSpacePanel;
            copyDragger.isInEditSpace = true;
            copyDragger.originalDockIcon = this;
            copyDragger.originalColor = this.originalColor;
            copyDragger.isUsed = false; 

            Image copyImage = copy.GetComponent<Image>();
            if (copyImage != null)
            {
                copyImage.color = this.originalColor;
                copyDragger.iconImage = copyImage; 
            }
        }

        PositionInEditSpace(copy, screenPosition);

        
    }

    private void MoveToEditSpace(Vector2 screenPosition)
    {
        if (editSpacePanel == null)
        {
            FindEditSpacePanel();
            if (editSpacePanel == null)
            {
                
                return;
            }
        }

        
        if (originalDockIcon == null)
        {
            FindAndLinkToDockIcon();
        }

        if (originalDockIcon != null)
        {
            originalDockIcon.SetUsedState(true);
            originalDockIcon.editSpaceCopy = this.gameObject;
        }

        transform.SetParent(editSpacePanel);
        isInEditSpace = true;

        PositionInEditSpace(gameObject, screenPosition);
    }

    private void PositionInEditSpace(GameObject target, Vector2 screenPosition)
    {
        RectTransform targetRect = target.GetComponent<RectTransform>();
        RectTransform editSpaceRect = editSpacePanel.GetComponent<RectTransform>();

        targetRect.anchorMin = new Vector2(0.5f, 0.5f);
        targetRect.anchorMax = new Vector2(0.5f, 0.5f);
        targetRect.pivot = new Vector2(0.5f, 0.5f);

        Vector2 localPoint;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            editSpaceRect,
            screenPosition,
            canvas.worldCamera,
            out localPoint);

        targetRect.anchoredPosition = localPoint;

        CanvasGroup targetCanvasGroup = target.GetComponent<CanvasGroup>();
        if (targetCanvasGroup != null)
        {
            targetCanvasGroup.alpha = 1f;
            targetCanvasGroup.blocksRaycasts = true;
        }
    }

    private void SetUsedState(bool used)
    {
        isUsed = used;

        if (iconImage != null)
        {
            Color targetColor = used ? usedColor : originalColor;
            iconImage.color = targetColor;
            
        }
        else
        {
            
        }
    }

    private void RestoreOriginalDockIcon()
    {
        string mySpriteName = iconImage?.sprite?.name;
        

        
        if (originalDockIcon == null)
        {
            
            FindAndLinkToDockIcon();
        }

        if (originalDockIcon == null && !string.IsNullOrEmpty(mySpriteName))
        {
            

            Transform contentPanel = FindContentPanel();
            if (contentPanel != null)
            {
                foreach (Transform child in contentPanel)
                {
                    DraggableIcon dockIcon = child.GetComponent<DraggableIcon>();
                    if (dockIcon != null && dockIcon.iconImage?.sprite != null)
                    {
                        string dockSpriteName = dockIcon.iconImage.sprite.name;
                        

                        if (string.Equals(dockSpriteName, mySpriteName, System.StringComparison.Ordinal))
                        {
                            originalDockIcon = dockIcon;
                            
                            break;
                        }
                    }
                }
            }
        }

        if (originalDockIcon != null)
        {
            originalDockIcon.SetUsedState(false);
            originalDockIcon.editSpaceCopy = null;
            
        }
        else
        {
            

            
            Transform contentPanel = FindContentPanel();
            if (contentPanel != null)
            {
                
                foreach (Transform child in contentPanel)
                {
                    DraggableIcon dockIcon = child.GetComponent<DraggableIcon>();
                    if (dockIcon != null && dockIcon.iconImage?.sprite != null)
                    {
                        Debug.Log($"  - GameObject: '{child.name}', Sprite: '{dockIcon.iconImage.sprite.name}'");
                    }
                }
            }
            else
            {
                
            }
        }
    }

    public bool IsUsed()
    {
        return isUsed;
    }

    public void SetUsed(bool used)
    {
        SetUsedState(used);
    }

    public void SetEditSpacePanel(Transform panel)
    {
        editSpacePanel = panel;
        CheckIfInEditSpace();
    }

    void OnDestroy()
    {
        if (dragPreview != null)
        {
            Destroy(dragPreview);
        }

        
        if (isInEditSpace && originalDockIcon != null)
        {
            originalDockIcon.SetUsedState(false);
            originalDockIcon.editSpaceCopy = null;
            
        }
    }
}