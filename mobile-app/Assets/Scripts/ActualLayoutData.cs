using System;
using System.Collections.Generic;
using UnityEngine;

//this is like the data for a layout
//inside got many many icons

[Serializable]
public class ActualLayoutData
{
    public string createdDate; //this one will be the display name of the layout
    public string lastModified; // this one will be recorded, but probably wouldn't use
    public string userNo = ""; //default empty, only entered if user log in

    public List<IconData> iconList = new List<IconData>();
    private const int DEFAULT_PIXEL_VALUE = 120;

    //constructor
    public ActualLayoutData()
    {
        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        createdDate = now;
        lastModified = now;
    }

    public void UpdateLastModified()
    {
        lastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    //methods
    //to add icon to the layout
    public void AddIcon(string iconFilename, float posX, float posY)
    {
        //check if .png
        if (!iconFilename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError($"Invalid icon file format: {iconFilename}. Only .png files are allowed.");
            return;
        }

        //check for duplicates
        bool exists = iconList.Exists(icon =>
            icon.iconFilename.Equals(iconFilename, StringComparison.OrdinalIgnoreCase) &&
            icon.posX == posX &&
            icon.posY == posY
        );

        if (exists)
        {
            Debug.LogWarning($"Icon already exists: {iconFilename} at ({posX}, {posY}) — skipping.");
            return;
        }

        iconList.Add(new IconData(iconFilename, posX, posY, DEFAULT_PIXEL_VALUE, DEFAULT_PIXEL_VALUE));
        UpdateLastModified();
    }

    public void ResizingAllIcons(int changedW, int changedH)
    {
        foreach(IconData icon in iconList)
        {
            icon.sizeX = changedW;
            icon.sizeY = changedH;
        }

        Debug.Log("Icons  resized.");
    }

    [Serializable]
    public class IconData
    {
        public string iconFilename;
        public float posX;
        public float posY;
        public int sizeX;
        public int sizeY;

        //construct
        public IconData(string fileName, float x, float y, int w, int h)
        {
            iconFilename = fileName;
            posX = x;
            posY = y;
            sizeX = w;
            sizeY = h;
        }
    }
}

