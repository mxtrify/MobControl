using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]

public class SpriteActionBridgeDict
{
    public List<SpriteActionPair> pairs;
}

[System.Serializable]

public class SpriteActionPair
{
    public string sprite;
    public string action;
}

public class BridgerScript : MonoBehaviour
{
    public static BridgerScript Instance { get; private set; }

    public string bridgerFileName = "bridger"; //is in Resources

    private Dictionary<string, string> spriteToAction;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); 

        LoadBridging();
    }

    public void LoadBridging()
    {
        TextAsset jsonFile = Resources.Load<TextAsset>(bridgerFileName);

        if (jsonFile == null)
        {
            
            return;
        }

        SpriteActionBridgeDict data = JsonUtility.FromJson<SpriteActionBridgeDict>(jsonFile.text);

        spriteToAction = new Dictionary<string, string>();
        foreach (var p in data.pairs)
        {
            spriteToAction[p.sprite] = p.action;

        }


    }

    public string GetAction(string sprite)
    {
        if (spriteToAction == null)
        {
            return null;
        }
        return spriteToAction.TryGetValue(sprite, out string action) ? action : null; 
    }

}