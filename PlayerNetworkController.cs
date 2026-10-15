using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SCRIPT 1 (devam): PlayerNetworkController
/// -----------------------------------------------------------------
/// Player prefab'ının üzerine konur. Her oyuncunun ağ üzerinden
/// senkronize edilen halini (can, skor, kalibre edilmiş hitbox yönü) tutar.
/// GyroCalibrationHitbox ve RealisticWeaponSystem bu bileşenle konuşur.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Ayarlar")]
    [SerializeField] private int maxHealth = 100;

    // Can ve skor sadece server tarafından değiştirilir, herkes okuyabilir.
    public NetworkVariable<int> Health = new NetworkVariable<int>(
        100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Score = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Oyuncunun kalibre edilmiş "vücut hedef yönü" (dünya uzayında normalize vektör).
    // Sahibi (Owner) tarafından yazılır, herkes okuyabilir.
    // RealisticWeaponSystem, rakip ateş ettiğinde bu değeri açı karşılaştırması için kullanır.
    public NetworkVariable<Vector3> HitboxDirection = new NetworkVariable<Vector3>(
        Vector3.forward, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // Kalibrasyonun yapılıp yapılmadığı bilgisi (UI'da "Kalibre Edilmedi" uyarısı için).
    public NetworkVariable<bool> IsCalibrated = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    public event System.Action<int> OnHealthChanged;
    public event System.Action<int> OnScoreChanged;
    public event System.Action OnHit; // Local feedback (ekran kızarması, hit-marker vs.)

    public override void OnNetworkSpawn()
    {
        Health.Value = maxHealth;
        Health.OnValueChanged += (oldVal, newVal) => OnHealthChanged?.Invoke(newVal);
        Score.OnValueChanged += (oldVal, newVal) => OnScoreChanged?.Invoke(newVal);

        // Sadece server, GameManager'a kayıt yapar (her iki oyuncu spawn olunca eşleşme başlar).
        if (IsServer)
        {
            NetworkGameManager.Instance.RegisterPlayer(this);
        }
    }

    /// <summary>
    /// Hasar uygulama işlemi SADECE server tarafında çalışır.
    /// RealisticWeaponSystem, isabeti server üzerinde doğruladıktan sonra bunu çağırır.
    /// </summary>
    public void ApplyDamageServerSide(int damage)
    {
        if (!IsServer) return;
        if (Health.Value <= 0) return;

        Health.Value = Mathf.Max(0, Health.Value - damage);

        // Sadece bu oyuncunun sahibine (isabet alan kişiye) hit efektini bildir.
        NotifyHitClientRpc(ClientRpcParamsFor(OwnerClientId));

        if (Health.Value <= 0)
        {
            NetworkGameManager.Instance.ReportPlayerDefeated(this);
        }
    }

    public void AddScoreServerSide(int amount)
    {
        if (!IsServer) return;
        Score.Value += amount;
    }

    public void ResetForNewRoundServerSide()
    {
        if (!IsServer) return;
        Health.Value = maxHealth;
    }

    /// <summary> Sadece isabet alan client'ta tetiklenir (hit-marker / ekran efekti). </summary>
    [ClientRpc]
    private void NotifyHitClientRpc(ClientRpcParams clientRpcParams = default)
    {
        OnHit?.Invoke();
    }

    private ClientRpcParams ClientRpcParamsFor(ulong targetClientId)
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { targetClientId }
            }
        };
    }
}
