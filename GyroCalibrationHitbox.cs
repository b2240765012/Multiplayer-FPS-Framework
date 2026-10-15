using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SCRIPT 2: GyroCalibrationHitbox
/// -----------------------------------------------------------------
/// Player prefab'ında PlayerNetworkController ile BİRLİKTE bulunur.
/// Sadece bu bileşenin sahibi (IsOwner) tarafından gyro okunur ve işlenir;
/// böylece her telefon sadece kendi sensörünü okur.
///
/// Akış:
/// 1) Oyuncu Sağ/Sol el seçer (SetHand).
/// 2) Telefonu göğsünde tutup "Kalibre Et" butonuna basar (Calibrate).
///    Bu an, referans (sıfır) açı olarak kaydedilir.
/// 3) Her frame, güncel gyro açısı ile referans arasındaki fark hesaplanır
///    ve buna göre oyuncunun "vücut hedef yönü" (HitboxDirection) güncellenip
///    ağ üzerinden (NetworkVariable, Owner-write) diğer oyuncuya iletilir.
/// </summary>
[RequireComponent(typeof(PlayerNetworkController))]
public class GyroCalibrationHitbox : NetworkBehaviour
{
    public enum Hand { Right, Left }

    [Header("El Seçimi")]
    [SerializeField] private Hand selectedHand = Hand.Right;

    [Header("Hitbox Ofset Ayarları")]
    [Tooltip("Kalibrasyon anındaki merkez noktasına göre, seçilen ele göre yatayda ek ofset (derece).")]
    [SerializeField] private float handOffsetDegrees = 8f;

    [Header("Referanslar")]
    [SerializeField] private Transform deviceForwardReference; // Genelde ana kamera / telefon transformu

    private PlayerNetworkController _playerController;

    // Kalibrasyon anındaki gyro attitude'u (referans / sıfır nokta).
    private Quaternion _calibrationReference = Quaternion.identity;
    private bool _hasCalibrated;

    // Dışarıdan (RealisticWeaponSystem) okunabilecek, telefonun O ANKİ dünya-uzayı yönü.
    // Ateş ederken "biz nereye nişan alıyoruz" bilgisini bu sağlar.
    public Vector3 CurrentAimDirection { get; private set; } = Vector3.forward;

    private void Awake()
    {
        _playerController = GetComponent<PlayerNetworkController>();
    }

    public override void OnNetworkSpawn()
    {
        // Gyro sadece sahip cihazda aktif edilmeli.
        if (IsOwner)
        {
            if (SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
            }
            else
            {
                Debug.LogWarning("[GyroCalibrationHitbox] Bu cihaz gyroskop desteklemiyor.");
            }
        }
    }

    /// <summary> UI'daki Sağ/Sol El butonlarından çağrılır. </summary>
    public void SetHand(int handIndex)
    {
        selectedHand = (Hand)handIndex;
    }

    /// <summary>
    /// UI'daki "Kalibre Et" butonundan çağrılır.
    /// Oyuncu telefonu göğsünde, hedef doğrultusuna bakacak şekilde tutarken basılmalıdır.
    /// </summary>
    public void Calibrate()
    {
        if (!IsOwner) return;
        if (!Input.gyro.enabled)
        {
            Debug.LogWarning("[GyroCalibrationHitbox] Gyro etkin değil, kalibrasyon yapılamıyor.");
            return;
        }

        _calibrationReference = GyroToUnity(Input.gyro.attitude);
        _hasCalibrated = true;

        _playerController.IsCalibrated.Value = true;

        // Kalibrasyon anında hitbox'ı da mevcut referansa göre sıfırlıyoruz.
        UpdateHitboxDirection();
    }

    private void Update()
    {
        // Sadece sahibi işlem yapar; NetworkVariable zaten diğer client'lara otomatik senkronize olur.
        if (!IsOwner || !_hasCalibrated) return;

        UpdateAimDirection();
        UpdateHitboxDirection();
    }

    /// <summary>
    /// Telefonun şu anki dünya-uzayı ileri vektörünü hesaplar (ateş doğrultusu için).
    /// </summary>
    private void UpdateAimDirection()
    {
        Quaternion currentAttitude = GyroToUnity(Input.gyro.attitude);

        // Kalibrasyon referansına göre göreli (relative) rotasyon.
        Quaternion relativeRotation = Quaternion.Inverse(_calibrationReference) * currentAttitude;

        CurrentAimDirection = (relativeRotation * Vector3.forward).normalized;
    }

    /// <summary>
    /// Oyuncunun "vücut hedef noktası" yönünü hesaplar ve ağa yazar.
    /// Mantık: Kalibrasyon anı = rakibin tam karşısında, 0 derece.
    /// El seçimine göre küçük bir yatay ofset uygulanır (silahı tutan elin
    /// gövdeye göre simetrik olmayan konumunu telafi eder).
    /// </summary>
    private void UpdateHitboxDirection()
    {
        float offset = selectedHand == Hand.Right ? handOffsetDegrees : -handOffsetDegrees;

        // Kalibrasyon anındaki "düz ileri" yönüne, el ofsetini yatay eksende ekliyoruz.
        Quaternion hitboxRotation = _calibrationReference * Quaternion.Euler(0f, offset, 0f);
        Vector3 hitboxDirection = (hitboxRotation * Vector3.forward).normalized;

        // Owner-write yetkili NetworkVariable; otomatik olarak diğer client'lara replicate olur.
        _playerController.HitboxDirection.Value = hitboxDirection;
    }

    /// <summary>
    /// Unity'nin sağ-el koordinat sistemi ile iOS/Android gyro'sunun
    /// kullandığı sol-el koordinat sistemi arasındaki dönüşüm.
    /// Bu, endüstri standardı bir dönüşümdür (Unity dokümantasyonunda da önerilir).
    /// NOT: Cihaz Landscape modda tutulacaksa ek 90 derecelik eksen düzeltmesi gerekebilir,
    /// gerçek cihazda test ederek 'landscapeCorrection' değerini ayarlayın.
    /// </summary>
    private static Quaternion GyroToUnity(Quaternion gyroQuaternion)
    {
        return new Quaternion(gyroQuaternion.x, gyroQuaternion.y, -gyroQuaternion.z, -gyroQuaternion.w);
    }

#if UNITY_EDITOR
    [Header("Editör Test Modu (Gyro'suz)")]
    [SerializeField] private bool useMouseSimulationInEditor = true;

    /// <summary>
    /// Editörde gyro olmadığı için, fare hareketiyle test edebilmek için basit bir simülasyon.
    /// Gerçek cihazda bu blok devre dışı kalır (yalnızca UNITY_EDITOR derleme sembolünde çalışır).
    /// </summary>
    private void LateUpdate()
    {
        if (!IsOwner || !useMouseSimulationInEditor || Application.isPlaying == false) return;
        if (Input.gyro.enabled) return; // Gerçek gyro varsa simülasyonu kullanma.

        // Basit fare tabanlı test: sağ tık basılıyken yönü hesaplamak isterseniz burayı genişletin.
    }
#endif
}
