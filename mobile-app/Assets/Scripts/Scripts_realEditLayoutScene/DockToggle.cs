using UnityEngine;

public class DockToggle : MonoBehaviour
{

    public GameObject outerTopDockPanel;

    private bool isVisible = true;

    public void ToggleDockPanel()
    {
        isVisible = !isVisible;
        outerTopDockPanel.SetActive(isVisible);
    }
}
