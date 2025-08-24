using UnityEngine.EventSystems;
using UnityEngine;

public class EditSpaceDropHandler : MonoBehaviour, IDropHandler
{
    public void OnDrop(PointerEventData eventData)
    {
        GameObject draggedObject = eventData.pointerDrag;
        if (draggedObject != null)
        {
            //set dropped object's parent to this EditSpacePanel
            draggedObject.transform.SetParent(this.transform);

            //set pos to where it was dropped
            draggedObject.transform.position = eventData.position;

            //convert screen pos to local pos
            RectTransform rectTransform = draggedObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                Vector2 localPoint;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    this.GetComponent<RectTransform>(),
                    eventData.position,
                    eventData.pressEventCamera,
                    out localPoint);
                rectTransform.localPosition = localPoint;
            }
        }
    }
}