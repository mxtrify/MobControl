using System;
using UnityEngine;

public class LayoutMonitor : MonoBehaviour
{

    public static LayoutMonitor Instance;
    public string CurrentChosenLayout = ""; //the json file name

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // keep across scenes
        }
        else
        {
            Destroy(gameObject); // prevent duplicates
        }
    }

    //when want to change the current chosen layout
    public void SetCurrentChosenLayout(string newValue)
    {
        CurrentChosenLayout = newValue;
        Debug.Log("CurrentChosenLayout updated: " + CurrentChosenLayout);
    }

}


//usage in any scene
//
//
//LayoutMonitor.Instance.CurrentChosenLayout
//
//LayoutMonitor.Instance.SetCurrentChosenLayout("...");