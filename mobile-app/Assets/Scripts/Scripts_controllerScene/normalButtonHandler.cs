using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[System.Serializable]
public class ButtonMessage
{
    public string sprite;
    public string type;
    public string action;

    public ButtonMessage(string sprite, string type, string action)
    {
        this.sprite = sprite;
        this.type = type;
        this.action = action;
    }
}

public class normalButtonHandler : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{

    //when the user press the button
    // it detects if it was being pressed and released immediately, 
    // or if it is being held down for a period

    // then it will send out a message, in a json
    // [the button sprite image name, "normal", click or hold or release] 

    //public float holdThreshold = 0.3f;
    //private bool isHolding = false;
    //private float holdTimer = 0f;
    //private bool holdTriggered = false;

    private string buttonSpriteName;

    //public BridgerScript theBridge;

    //private List<string> buttonStateMessage = new List<string>();

    void Start()
    {
        //get sprite name from 
        var img = GetComponent<Image>();
        if (img != null && img.sprite != null)
        {
            buttonSpriteName = img.sprite.name;
        }
        else
        {
            var spr = GetComponent<SpriteRenderer>();
            if (spr != null && spr.sprite != null)
            {
                buttonSpriteName = spr.sprite.name;
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        //isHolding = true;
        //holdTimer = 0f;
        //holdTriggered = false;

        SendButtonMessage(new ButtonMessage(buttonSpriteName, "normal", "click")); // send "down"
    }

    public void OnPointerUp(PointerEventData eventData)
    {

        SendButtonMessage(new ButtonMessage(buttonSpriteName, "normal", "release")); // send "up"

    }




    private void SendButtonMessage(ButtonMessage msg)
    {
        
        string bridgedAction = BridgerScript.Instance?.GetAction(msg.sprite);

        if (string.IsNullOrEmpty(bridgedAction))
        {
            Debug.LogWarning($"No bridged action for sprite: {msg.sprite}");
            return;
        }

        string state = msg.action == "click" ? "down" : msg.action == "release" ? "up" : msg.action;

        //string json = $"{{\"action\":\"{bridgedAction}\", \"state\":\"{state}\"}}";
        string json = $"[{{\"type\":\"button\", \"id\":\"{bridgedAction}\", \"state\":\"{state}\", \"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}]";


        Debug.Log($"Sending bridged JSON: {json}");

        if (BIGConnectionManager.Instance != null && BIGConnectionManager.Instance.IsConnected)
        {
            BIGConnectionManager.Instance.Send(json);
        }
        else
        {
            Debug.Log("WEBSOCKET NOT CONNECTED.");
        }
    }
}



