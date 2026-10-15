using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// BONUS SCRIPT: CameraBackgroundFeed
/// -----------------------------------------------------------------
/// AR Foundation KULLANMADAN, sadece telefonun arka kamerasını WebCamTexture
/// ile okuyup bir RawImage üzerine basar. Silah modeli bu RawImage'ın
/// ÖNÜNDE (üstünde, ayrı bir Canvas/Layer'da) durur, böylece "gerçek dünya
/// üzerine bindirilmiş silah" görsel efekti elde edilir.
///
/// Not: Bu basit yöntemde gerçek AR (dünya takibi, plane detection vb.) YOKTUR;
/// sadece canlı kamera görüntüsü arka planda oynar. MVP için yeterlidir.
/// </summary>
public class CameraBackgroundFeed : MonoBehaviour
{
    [SerializeField] private RawImage backgroundRawImage;
    [SerializeField] private AspectRatioFitter aspectFitter;
    [SerializeField] private int preferredWidth = 1280;
    [SerializeField] private int preferredHeight = 720;

    private WebCamTexture _webCamTexture;

    private void Start()
    {
        RequestCameraAndStart();
    }

    private void RequestCameraAndStart()
    {
#if UNITY_ANDROID
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }
#endif
        StartBackCamera();
    }

    private void StartBackCamera()
    {
        WebCamDevice[] devices = WebCamTexture.devices;
        string chosenDeviceName = null;

        // Arka (back-facing) kamerayı bul.
        foreach (var device in devices)
        {
            if (!device.isFrontFacing)
            {
                chosenDeviceName = device.name;
                break;
            }
        }

        if (chosenDeviceName == null && devices.Length > 0)
        {
            chosenDeviceName = devices[0].name; // Bulunamazsa ilk kamerayı kullan.
        }

        if (chosenDeviceName == null)
        {
            Debug.LogWarning("[CameraBackgroundFeed] Hiçbir kamera bulunamadı.");
            return;
        }

        _webCamTexture = new WebCamTexture(chosenDeviceName, preferredWidth, preferredHeight);
        backgroundRawImage.texture = _webCamTexture;
        _webCamTexture.Play();
    }

    private void Update()
    {
        // Kamera görüntüsünün gerçek boyut oranını RawImage'a uygula (esneme olmasın).
        if (_webCamTexture != null && _webCamTexture.width > 100 && aspectFitter != null)
        {
            aspectFitter.aspectRatio = (float)_webCamTexture.width / _webCamTexture.height;

            // Telefonun döndürülmüş olabileceği durumlar için görüntüyü düzelt.
            int orientationAngle = -_webCamTexture.videoRotationAngle;
            backgroundRawImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, orientationAngle);

            float scaleY = _webCamTexture.videoVerticallyMirrored ? -1f : 1f;
            backgroundRawImage.rectTransform.localScale = new Vector3(1f, scaleY, 1f);
        }
    }

    private void OnDestroy()
    {
        if (_webCamTexture != null && _webCamTexture.isPlaying)
        {
            _webCamTexture.Stop();
        }
    }
}
