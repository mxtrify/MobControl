using UnityEngine;
using UnityEngine.UI;
using ZXing;
using System.Collections;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class QRCodeScanner : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject qrPanel;
    public RawImage cameraFeed;
    public Text statusText; // the log text,

    private WebCamTexture camTexture;
    private bool isScanning = false;

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
        }
#endif
    }

    public void StartScan()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
       
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            statusText.text = "Camera permission denied.";
            return;
        }
#endif

        if (camTexture == null)
        {
            camTexture = new WebCamTexture();
        }

        if (camTexture == null || WebCamTexture.devices.Length == 0)
        {
            statusText.text = "No camera available.";
            return;
        }

        cameraFeed.texture = camTexture;
        cameraFeed.material.mainTexture = camTexture;
        camTexture.Play();

        
        cameraFeed.rectTransform.localEulerAngles = new Vector3(0, 0, -camTexture.videoRotationAngle);
        cameraFeed.rectTransform.localScale = camTexture.videoVerticallyMirrored ? new Vector3(1, -1, 1) : Vector3.one;

        qrPanel.SetActive(true);
        statusText.text = "Looking for QR Code...";
        isScanning = true;

        StartCoroutine(WaitAndStartScanLoop());
    }

    IEnumerator WaitAndStartScanLoop()
    {
        yield return new WaitForSeconds(1f);
        if (camTexture.width <= 16 || camTexture.height <= 16)
        {
            statusText.text = "Camera failed to initialise properly.";
            StopScan();
            yield break;
        }
        StartCoroutine(ScanLoop());
    }

    public void StopScan()
    {
        isScanning = false;

        if (camTexture != null && camTexture.isPlaying)
            camTexture.Stop();

        qrPanel.SetActive(false);
    }

    IEnumerator ScanLoop()
    {
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions
            {
                PossibleFormats = new System.Collections.Generic.List<ZXing.BarcodeFormat>
                {
                    ZXing.BarcodeFormat.QR_CODE
                },
                TryHarder = true
            }
        };

        while (isScanning)
        {
            if (camTexture.width < 100 || camTexture.height < 100)
            {
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            try
            {
                Color32[] pixels = camTexture.GetPixels32();
                var result = reader.Decode(pixels, camTexture.width, camTexture.height);

                if (result != null)
                {
                    statusText.text = "QR Code found! Connecting...";
                    StopScan();

                    
                    HandleBIGConnection(result.Text.Trim());
                    yield break;
                }
            }
            catch
            {
                statusText.text = "Scanner error.";
                StopScan();
                yield break;
            }

            yield return new WaitForSeconds(0.3f);
        }
    }

    private async void HandleBIGConnection(string qrData)
    {
        try
        {
            var uri = new System.Uri(qrData);
            string ip = uri.Host;
            int port = uri.Port;

            string token = null;
            if (!string.IsNullOrEmpty(uri.Query))
            {
                string query = uri.Query.TrimStart('?');
                foreach (var param in query.Split('&'))
                {
                    var parts = param.Split('=');
                    if (parts.Length == 2 && parts[0] == "token")
                    {
                        token = parts[1];
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(ip) && port > 0 && !string.IsNullOrEmpty(token))
            {
                string connectUrl = $"ws://{ip}:{port}/ws?token={token}";
                Debug.Log("Connecting to: " + connectUrl);

                await BIGConnectionManager.Instance.Connectreally(connectUrl);
                //statusText.text = "Connected successfully!";
            }
            else
            {
                statusText.text = "Invalid QR code format";
                StartScan();
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("Connection failed: " + ex.Message);
            statusText.text = "Connection failed. Scan another QR code.";
            StartScan();
        }
    }
}
