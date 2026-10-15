using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// SCRIPT 1: NetworkGameManager
/// -----------------------------------------------------------------
/// Yerel ağ (LAN) üzerinden Host/Client bağlantısını yönetir.
/// 1v1 maç akışını (Bekleme -> Geri Sayım -> Oynanış -> Bitiş) kontrol eder.
/// Sahnedeki tek bir NetworkManager objesine bağlanmalıdır.
/// Singleton pattern kullanır, sahne boyunca tek instance yaşar (DontDestroyOnLoad).
/// </summary>
public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    public enum MatchState { WaitingForPlayers, Countdown, Playing, GameOver }

    // Tüm client'lara senkronize olan maç durumu.
    public NetworkVariable<MatchState> CurrentState = new NetworkVariable<MatchState>(
        MatchState.WaitingForPlayers,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    [Header("Maç Ayarları")]
    [SerializeField] private int maxPlayers = 2;
    [SerializeField] private float countdownDuration = 3f;

    // Sunucu tarafında oyuncu kayıt defteri: 1v1 olduğundan sadece 2 slot var.
    // RealisticWeaponSystem, ateş anında "rakibi" bulmak için buradan sorgu yapar.
    private readonly List<PlayerNetworkController> _registeredPlayers = new List<PlayerNetworkController>();

    private float _countdownTimer;

    private void Awake()
    {
        // Basit singleton koruması.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnect;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnect;
        }
    }

    #region Host / Client Başlatma (UI butonlarından çağrılır)

    /// <summary> LAN üzerinde Host olarak başlat (kendi IP'sinde dinler). </summary>
    public void StartAsHost()
    {
        NetworkManager.Singleton.StartHost();
    }

    /// <summary>
    /// LAN üzerinde belirtilen IP'ye Client olarak bağlan.
    /// Unity Transport (UTP) kullanıldığı varsayılıyor.
    /// </summary>
    public void StartAsClient(string ipAddress, ushort port = 7777)
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
        {
            transport.SetConnectionData(ipAddress, port);
        }
        NetworkManager.Singleton.StartClient();
    }

    public void ShutdownNetwork()
    {
        NetworkManager.Singleton.Shutdown();
    }

    #endregion

    #region Oyuncu Kayıt Sistemi

    /// <summary>
    /// Her PlayerNetworkController, OnNetworkSpawn içinde kendini burada kaydettirir.
    /// Sadece sunucu tarafında anlamlıdır.
    /// </summary>
    public void RegisterPlayer(PlayerNetworkController player)
    {
        if (!IsServer) return;

        if (!_registeredPlayers.Contains(player))
        {
            _registeredPlayers.Add(player);
        }

        if (_registeredPlayers.Count >= maxPlayers)
        {
            BeginCountdown();
        }
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        if (!IsServer) return;
        _registeredPlayers.RemoveAll(p => p == null || p.OwnerClientId == clientId);
        CurrentState.Value = MatchState.WaitingForPlayers;
    }

    /// <summary>
    /// 1v1 mantığı: Verilen oyuncunun rakibini döndürür.
    /// RealisticWeaponSystem, isabet kontrolü için bunu server tarafında çağırır.
    /// </summary>
    public PlayerNetworkController GetOpponentOf(PlayerNetworkController shooter)
    {
        foreach (var p in _registeredPlayers)
        {
            if (p != shooter) return p;
        }
        return null;
    }

    #endregion

    #region Maç Akışı (Sadece Server)

    private void BeginCountdown()
    {
        CurrentState.Value = MatchState.Countdown;
        _countdownTimer = countdownDuration;
    }

    private void Update()
    {
        if (!IsServer) return;

        if (CurrentState.Value == MatchState.Countdown)
        {
            _countdownTimer -= Time.deltaTime;
            if (_countdownTimer <= 0f)
            {
                CurrentState.Value = MatchState.Playing;
            }
        }
    }

    /// <summary>
    /// Bir oyuncunun canı sıfırlandığında PlayerNetworkController bunu çağırır.
    /// </summary>
    public void ReportPlayerDefeated(PlayerNetworkController defeatedPlayer)
    {
        if (!IsServer) return;
        CurrentState.Value = MatchState.GameOver;

        var winner = GetOpponentOf(defeatedPlayer);
        if (winner != null)
        {
            winner.AddScoreServerSide(1);
        }
    }

    /// <summary> Round'u tekrar başlatmak için (Rematch butonu vb.) </summary>
    public void RestartMatch()
    {
        if (!IsServer) return;
        foreach (var p in _registeredPlayers)
        {
            p.ResetForNewRoundServerSide();
        }
        BeginCountdown();
    }

    #endregion
}
