using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SCRIPT 3: RealisticWeaponSystem
/// -----------------------------------------------------------------
/// Player prefab'ında PlayerNetworkController + GyroCalibrationHitbox ile
/// birlikte bulunur. Ekrandaki silah modeliyle (Animator, ParticleSystem,
/// AudioSource) etkileşime girer ve isabet algılamayı server üzerinde
/// doğrulatır (cheat/hile önleme için hasar kararı client'ta verilmez).
/// </summary>
[RequireComponent(typeof(PlayerNetworkController))]
[RequireComponent(typeof(GyroCalibrationHitbox))]
public class RealisticWeaponSystem : NetworkBehaviour
{
    [Header("Görsel Referanslar")]
    [SerializeField] private Animator weaponAnimator;          // Sürgü, horoz animasyonları
    [SerializeField] private ParticleSystem muzzleFlash;        // Namlu ateşi
    [SerializeField] private ParticleSystem shellEjectParticles; // Kovan atma (basit particle versiyonu)
    [SerializeField] private Rigidbody shellCasingPrefab;        // Kovan atma (fiziksel versiyon, opsiyonel)
    [SerializeField] private Transform shellEjectPoint;

    [Header("Ses")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip fireSound;
    [SerializeField] private AudioClip reloadSound;
    [SerializeField] private AudioClip emptyMagSound; // Şarjör boşken tetiğe basınca

    [Header("Ateş Etme Ayarları")]
    [SerializeField] private float fireRate = 0.15f;     // İki atış arası minimum süre
    [SerializeField] private int damagePerHit = 25;
    [Tooltip("Ateş eden oyuncunun nişan yönü ile rakibin hitbox yönü arasındaki, isabet sayılacak maksimum açı (derece).")]
    [SerializeField] private float hitAngleThresholdDegrees = 5f;

    [Header("Şarjör")]
    [SerializeField] private int magazineSize = 12;
    [SerializeField] private float reloadDuration = 1.5f;

    [Header("Titreşim")]
    [SerializeField] private bool useVibrationOnFire = true;

    private GyroCalibrationHitbox _gyro;
    private PlayerNetworkController _playerController;

    private float _lastFireTime = -999f;
    private int _currentAmmo;
    private bool _isReloading;

    private static readonly int FireTrigger = Animator.StringToHash("Fire");
    private static readonly int ReloadTrigger = Animator.StringToHash("Reload");

    private void Awake()
    {
        _gyro = GetComponent<GyroCalibrationHitbox>();
        _playerController = GetComponent<PlayerNetworkController>();
        _currentAmmo = magazineSize;
    }

    public override void OnNetworkSpawn()
    {
        // Sadece sahibi (kendi telefonu) bu silahın input'unu okur/kontrol eder.
        // Diğer client'larda bu obje varsa bile input dinlenmez (UI butonları sahibe özeldir).
        if (!IsOwner)
        {
            enabled = false; // Local update/input mantığını devre dışı bırak.
        }
    }

    #region UI Buton Girişleri (Tetik / Şarjör)

    /// <summary> Ekrandaki "Tetik" butonuna (PointerDown) bağlanır. </summary>
    public void OnTriggerPressed()
    {
        if (!IsOwner) return;
        if (_isReloading) return;

        if (Time.time - _lastFireTime < fireRate) return; // Ateş hızı sınırı

        if (_currentAmmo <= 0)
        {
            PlayEmptyMagFeedback();
            return;
        }

        if (!_playerController.IsCalibrated.Value)
        {
            Debug.LogWarning("[RealisticWeaponSystem] Önce kalibrasyon yapılmalı.");
            return;
        }

        Fire();
    }

    /// <summary> Ekrandaki "Şarjör" butonuna bağlanır. </summary>
    public void OnReloadPressed()
    {
        if (!IsOwner) return;
        if (_isReloading || _currentAmmo == magazineSize) return;

        StartCoroutine(ReloadRoutine());
    }

    #endregion

    #region Ateş Etme Mantığı

    private void Fire()
    {
        _lastFireTime = Time.time;
        _currentAmmo--;

        // --- a) Görsel: animasyon (sürgü geri gider, kapanır) ---
        if (weaponAnimator != null)
        {
            weaponAnimator.SetTrigger(FireTrigger);
        }

        // --- b) Efekt: namlu ateşi + kovan atma ---
        PlayMuzzleAndShellEffects();

        // --- c) Ses ---
        if (audioSource != null && fireSound != null)
        {
            audioSource.PlayOneShot(fireSound);
        }

        // --- d) Titreşim ---
        if (useVibrationOnFire)
        {
            Handheld.Vibrate(); // Basit API; ince ayarlı titreşim için native plugin gerekir.
        }

        // --- e) Ağ: isabet kontrolünü server'da doğrulat ---
        Vector3 aimDirection = _gyro.CurrentAimDirection;
        RequestFireServerRpc(aimDirection);
    }

    private void PlayMuzzleAndShellEffects()
    {
        if (muzzleFlash != null)
        {
            muzzleFlash.Play();
        }

        if (shellEjectParticles != null)
        {
            shellEjectParticles.Play();
        }

        // Fiziksel kovan (Rigidbody) kullanılacaksa, sahne içinde local (network'süz) bir obje olarak spawn edilir;
        // sadece görsel amaçlıdır, oyun mantığını etkilemez, bu yüzden ağ senkronizasyonu gerekmez.
        if (shellCasingPrefab != null && shellEjectPoint != null)
        {
            Rigidbody shell = Instantiate(shellCasingPrefab, shellEjectPoint.position, shellEjectPoint.rotation);
            shell.AddForce(shellEjectPoint.right * 1.5f + shellEjectPoint.up * 0.5f, ForceMode.Impulse);
            shell.AddTorque(Random.insideUnitSphere * 2f, ForceMode.Impulse);
            Destroy(shell.gameObject, 3f); // Sahneyi temiz tutmak için birkaç saniye sonra yok et.
        }
    }

    private void PlayEmptyMagFeedback()
    {
        if (audioSource != null && emptyMagSound != null)
        {
            audioSource.PlayOneShot(emptyMagSound);
        }
    }

    #endregion

    #region Şarjör Doldurma

    private System.Collections.IEnumerator ReloadRoutine()
    {
        _isReloading = true;

        if (weaponAnimator != null)
        {
            weaponAnimator.SetTrigger(ReloadTrigger);
        }

        if (audioSource != null && reloadSound != null)
        {
            audioSource.PlayOneShot(reloadSound);
        }

        yield return new WaitForSeconds(reloadDuration);

        _currentAmmo = magazineSize;
        _isReloading = false;
    }

    #endregion

    #region Server Tarafı İsabet Doğrulama

    /// <summary>
    /// Client, ateş ettiğinde nişan yönünü server'a gönderir.
    /// Server, rakip oyuncuyu bulur ve kalibre edilmiş HitboxDirection'ı ile
    /// bu nişan yönü arasındaki açıyı hesaplayarak isabeti KENDİSİ karar verir.
    /// (Hile önleme: hasar kararı asla client'ta verilmez.)
    /// </summary>
    [ServerRpc]
    private void RequestFireServerRpc(Vector3 shooterAimDirection, ServerRpcParams rpcParams = default)
    {
        PlayerNetworkController opponent = NetworkGameManager.Instance.GetOpponentOf(_playerController);
        if (opponent == null) return;

        Vector3 targetHitboxDirection = opponent.HitboxDirection.Value;

        // İki vektör arasındaki açısal fark (derece cinsinden).
        float angleDifference = Vector3.Angle(shooterAimDirection, targetHitboxDirection);

        bool isHit = angleDifference <= hitAngleThresholdDegrees;

        if (isHit)
        {
            opponent.ApplyDamageServerSide(damagePerHit);

            // Vurduğunu bilsin diye ateş eden oyuncuya (bu client'a) geri bildirim.
            ConfirmHitClientRpc(rpcParams.Receive.SenderClientId == 0 ? OwnerClientId : rpcParams.Receive.SenderClientId);
        }
    }

    /// <summary> Sadece isabet ettiren client'ta "vuruldu!" UI/ses geri bildirimi göstermek için. </summary>
    [ClientRpc]
    private void ConfirmHitClientRpc(ulong shooterClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != shooterClientId) return;

        // Burada "Hedef Vuruldu!" UI mesajı, farklı bir onay sesi vb. tetiklenebilir.
        Debug.Log("[RealisticWeaponSystem] İsabet onaylandı!");
    }

    #endregion
}
