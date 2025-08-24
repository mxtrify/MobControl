using UnityEngine;
using UnityEngine.UI;
using TMPro;

using System.Collections;
using System.Threading.Tasks;

public class InputCapture : MonoBehaviour
{

    public TMP_InputField ipField;
    public TMP_InputField portField;
    public TMP_InputField tokenField;

    private string ipText;
    private string portText;
    private string tokenText;


    void Start()
    {
        ipField.onValueChanged.AddListener(OnipFieldChanged);
        portField.onValueChanged.AddListener(OnportFieldChanged);
        tokenField.onValueChanged.AddListener(OntokenFieldChanged);
    }
    void OnipFieldChanged(string value)
    {
        ipText = value;

    }

    void OnportFieldChanged(string value)
    {
        portText = value;
    }

    void OntokenFieldChanged(string value)
    {
        tokenText = value;

    }


    public async void ManualConnect()
    {
        Debug.Log("IP Address entered: " + ipText);
        Debug.Log("Port entered: " + portText);
        Debug.Log("Token entered: " + tokenText);

        await Connectdddd(ipText, portText, tokenText);
    }

    public async Task Connectdddd(string ip, string port, string token)
    {
        string wsUrl = $"ws://{ip}:{port}/ws?token={token}";
        Debug.Log("Connecting to: " + wsUrl);

        if (BIGConnectionManager.Instance != null)
        {
            await BIGConnectionManager.Instance.Connectreally(wsUrl);
            Debug.Log("Connection attempt finished.");
            
        }
        else
        {
            Debug.LogWarning("BIGConnectionManager.Instance is null!");
            
        }


    }
}