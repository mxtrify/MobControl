using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System;
//using Newtonsoft.Json;
using UnityEngine.SceneManagement;
//using UnityEditor.Rendering;

public class layoutsMANAGER : MonoBehaviour
{

    //go into a directory
    //see all the files, 
    //select those that starts with "Layout_" and ends with ".json"
    //add file name to list
    //then for each file name in the list, create a clickable button for it
    //the button will have the createdDate as text display
    //if list is empty
    //display a text "No layouts found."

    //when user clicks on a button of the list, 
    //currentchosenlayout will be change to that layout file name
    //there is also a create new layout button
    //currentchosenlayout will be set to that new layout name

    public Transform layoutButtonsContainer;
    public Button layoutButtonPrefab;

    string folderPath;

    private void Start()
    {
        folderPath = Path.Combine(Application.persistentDataPath, "UserLayouts");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            Debug.Log("Created UserLayouts directory at " + folderPath);
        }


        if (!PlayerPrefs.HasKey("DefaultLayoutsCopied"))
        {
            CopyDefaultLayoutsIfNeed();
            PlayerPrefs.SetInt("DefaultLayoutsCopied", 1);
            PlayerPrefs.Save();
        }

        LoadLayoutButtons();
    }

    void LoadLayoutButtons()
    {

        string[] files = Directory.GetFiles(folderPath, "Layout_*.json");

        if (files.Length == 0)
        {
            //create text object to tell no layouts found
            HelperMessageMonitor.Instance.SetHelpMessage("No layouts found.");
            return;
        }

        foreach(string fileThing in files)
        {
            
                string json = File.ReadAllText(fileThing);

                //ActualLayoutData theLayoutData = JsonConvert.DeserializeObject<ActualLayoutData>(json);
                ActualLayoutData theLayoutData = JsonUtility.FromJson<ActualLayoutData>(json);

                //create a button
                Button newButton = Instantiate(layoutButtonPrefab, layoutButtonsContainer);
                
                newButton.GetComponentInChildren<Text>().text = theLayoutData.createdDate;

                if (theLayoutData.createdDate == "0000-00-00 00:00:01")
                {
                    newButton.GetComponentInChildren<Text>().text = "Default Layout 1";
                }
                else if (theLayoutData.createdDate == "0000-00-00 00:00:02"){
                    newButton.GetComponentInChildren<Text>().text = "Default Layout 2";
                }
                else
                {
                    newButton.GetComponentInChildren<Text>().text = theLayoutData.createdDate;
                }




            //button functionality
            string fileName = Path.GetFileName(fileThing);
                newButton.onClick.AddListener(() => {
                    LayoutMonitor.Instance.SetCurrentChosenLayout(fileName);
                    //also move to edit scene
                    SceneManager.LoadScene(4);
                });


        }

    }

    public void CreateNewLayoutPath()
    {
        //string folderPath = Path.Combine(Application.persistentDataPath, "UserLayouts");

        string timeStamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string fileName = $"Layout_{timeStamp}.json";

        string path = Path.Combine(folderPath, fileName);
        CreateTheJSON(path);
        LayoutMonitor.Instance.SetCurrentChosenLayout(fileName);
        

    }

    public void CreateTheJSON(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                ActualLayoutData newLayoutData = new ActualLayoutData();
                string json = JsonUtility.ToJson(newLayoutData, true);
                File.WriteAllText(path, json);
                Debug.Log("NEW JSON CREATED!! " + path);
            }
            else
            {
                Debug.LogWarning("File already exists: " + path);
            }
        }
        catch (Exception e) 
        {
        
            Debug.LogError("Failed to create JSON file: " + e.Message);
        }
    }

    void CopyDefaultLayoutsIfNeed()
    {
        Debug.Log("copying.......");
        string userLayoutsDir = Path.Combine(Application.persistentDataPath, "UserLayouts");

        if (!Directory.Exists(userLayoutsDir))
        {
            Directory.CreateDirectory(userLayoutsDir);
        }

        TextAsset[] defaultLayouts = Resources.LoadAll<TextAsset>("DefaultLayouts");

        foreach (TextAsset defaultLayout in defaultLayouts)
        {
            string fileName = defaultLayout.name + ".json";
            string targetPath = Path.Combine(userLayoutsDir, fileName);

            if (!File.Exists(targetPath))
            {
                File.WriteAllText(targetPath, defaultLayout.text);
                Debug.Log($"Copied default layout: {fileName}");
            }
        }
    }
}
