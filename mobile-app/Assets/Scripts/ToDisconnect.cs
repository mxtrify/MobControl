using UnityEngine;

public class ToDisconnect : MonoBehaviour
{
    public void MakeDisconnection()
    {
        if (BIGConnectionManager.Instance != null)
        {
            BIGConnectionManager.Instance.Disconnect();
            Debug.Log("User triggered disconnect.");
        }
        else
        {
            Debug.LogWarning("No BIGConnectionManager instance found.");
        }
    }
}
