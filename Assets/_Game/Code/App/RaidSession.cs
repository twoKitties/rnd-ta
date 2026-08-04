using System;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using UnityEngine;

// Both FishNet and Unity have a SceneManager and this file uses both. The alias keeps
// every call site saying which one it means.
using UnityScenes = UnityEngine.SceneManagement.SceneManager;

namespace _Game.Code.App
{
    /// <summary>
    /// One co-op session, from the lobby to the end of a raid and back. Lives on the
    /// object <see cref="Bootstrapper"/> keeps alive for the whole launch, next to the
    /// NetworkManager, because it has to outlive Lobby → Level → Lobby.
    ///
    /// Everything here is a decision about the session rather than about the game:
    /// who is connected, which scene everyone is in, and when the door closes. The
    /// raid's own rules stay in the Level scene and know nothing about this.
    ///
    /// Deliberately not a NetworkBehaviour. It has no replicated state of its own —
    /// scene loads reach every client through FishNet's own scene manager, and the
    /// lobby roster is replicated by the object the server spawns for it. That keeps
    /// this class callable before anybody is connected at all, which is exactly when
    /// Host and Join have to work.
    /// </summary>
    public class RaidSession : MonoBehaviour
    {
        [Header("Scenes")]
        [SerializeField] private string lobbyScene = "Lobby";
        [SerializeField] private string levelScene = "Level";

        [Header("Connection")]
        [Tooltip("Both ends use this. Over Tailscale the address is the other machine's " +
                 "tailnet IP and no relay or port forwarding is involved.")]
        [SerializeField] private ushort port = 7770;

        [Tooltip("What Host connects its own client to. Loopback: the host is a player too.")]
        [SerializeField] private string hostAddress = "127.0.0.1";

        [Header("Prefabs")]
        [Tooltip("Spawned by the host when the session opens; carries the lobby roster " +
                 "to every client. Needs a NetworkObject.")]
        [SerializeField] private GameObject lobbyRosterPrefab;

        /// <summary>
        /// The session of this launch. A static, and allowed to be one: MECHANICS.md
        /// 7.3 forbids statics holding <em>player</em> state, and there is none here —
        /// this is the single process-wide object the UI in another scene has to be
        /// able to reach without searching for it (7.6).
        /// </summary>
        public static RaidSession Active { get; private set; }

        [Tooltip("How long a connection attempt may take before we call it dead, seconds. " +
                 "The transport gives up on its own after about five (LiteNetLib tries " +
                 "ten times), but only if it is answering at all — this is the backstop.")]
        [SerializeField] private float connectTimeout = 8f;

        /// <summary>True once the raid has started and the lobby has closed.</summary>
        public bool IsRaidRunning { get; private set; }

        /// <summary>A connection attempt is in flight. The UI shows this as "connecting".</summary>
        public bool IsConnecting { get; private set; }

        /// <summary>The connection is up. Raised on the machine that asked for it.</summary>
        public event Action Connected;

        /// <summary>
        /// The attempt did not get there, with something to show the player. Raised only
        /// for a connection we were still trying to make — losing a session that was
        /// working is a different thing and is not reported here.
        /// </summary>
        public event Action<string> ConnectFailed;

        // When the attempt gives up if nothing has answered. Zero means no attempt.
        private float _attemptDeadline;

        /// <summary>True on the machine that is hosting — the one that owns the rules.</summary>
        public bool IsHost => InstanceFinder.IsServerStarted;

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                // Two sessions cannot both be the session. The newcomer loses, so that
                // whatever is already connected stays connected.
                Destroy(gameObject);
                return;
            }

            Active = this;
        }

        private void OnEnable()
        {
            var manager = InstanceFinder.NetworkManager;
            if (manager == null)
            {
                Debug.LogError("RaidSession: no NetworkManager in the scene, nothing can connect.");
                return;
            }

            manager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            manager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            manager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }

        private void OnDisable()
        {
            var manager = InstanceFinder.NetworkManager;
            if (manager == null)
            {
                return;
            }

            manager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            manager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
            manager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }

        private void OnDestroy()
        {
            if (Active == this)
            {
                Active = null;
            }
        }

        /// <summary>
        /// Open a session on this machine and join it as a player. The host is one of
        /// the four, not a spectating server (MECHANICS.md 7 — "server-authoritative,
        /// host — один из игроков").
        /// </summary>
        public bool Host()
        {
            if (!InstanceFinder.ServerManager.StartConnection(port))
            {
                // Almost always the port already being in use — a second copy of the
                // game left running, which is exactly what happens while testing.
                Fail($"Не удалось занять порт {port}");
                return false;
            }

            BeginAttempt();
            if (InstanceFinder.ClientManager.StartConnection(hostAddress, port))
            {
                return true;
            }

            Fail("Не удалось подключиться к собственному хосту");
            return false;
        }

        /// <summary>
        /// Join somebody else's session. The address may carry a port —
        /// <c>100.64.0.2:7770</c> — and falls back to the shared default without one,
        /// because over Tailscale the address is the only thing that differs between
        /// machines and typing the port every time is a way to get it wrong.
        /// </summary>
        public bool Join(string endpoint)
        {
            string address;
            ushort chosenPort;
            if (!TryParseEndpoint(endpoint, out address, out chosenPort))
            {
                Fail("Адрес не разобран");
                return false;
            }

            BeginAttempt();
            if (InstanceFinder.ClientManager.StartConnection(address, chosenPort))
            {
                return true;
            }

            Fail("Соединение не найдено");
            return false;
        }

        /// <summary>
        /// Splits "address" or "address:port". Pure and public so the field can be
        /// validated as it is typed without opening a socket.
        /// </summary>
        public bool TryParseEndpoint(string endpoint, out string address, out ushort endpointPort)
        {
            address = null;
            endpointPort = port;

            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return false;
            }

            var text = endpoint.Trim();
            var colon = text.LastIndexOf(':');
            if (colon < 0)
            {
                address = text;
                return address.Length > 0;
            }

            address = text.Substring(0, colon).Trim();
            var portText = text.Substring(colon + 1).Trim();

            ushort parsed;
            // Invariant culture on purpose: this machine is set to Russian, and a
            // culture-sensitive parse of digits typed by a player is a trap we have
            // already been bitten by elsewhere in this project.
            if (!ushort.TryParse(portText, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed) || parsed == 0)
            {
                return false;
            }

            endpointPort = parsed;
            return address.Length > 0;
        }

        /// <summary>
        /// Leave, whatever we are. A client disconnects and finds itself back in the
        /// lobby through <see cref="OnClientConnectionState"/>; a host stops listening,
        /// which does the same to everybody else. Host migration does not exist, and
        /// this is where that decision lives.
        /// </summary>
        public void Leave()
        {
            if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.StopConnection();
            }

            if (InstanceFinder.IsServerStarted)
            {
                InstanceFinder.ServerManager.StopConnection(true);
            }

            IsRaidRunning = false;
            GoToLobbyAlone();
        }

        /// <summary>
        /// Take everybody into the level. The host's call, and the moment the lobby
        /// closes: from here a new connection is refused rather than dropped into a
        /// raid whose state it has no way of catching up with.
        /// </summary>
        public void StartRaid()
        {
            if (!IsHost)
            {
                return;
            }

            IsRaidRunning = true;
            LoadForEveryone(levelScene);
        }

        /// <summary>
        /// Play it again. Reloading the scene is the whole reset: every latched thing —
        /// the outcome, the delivered count, who is dead, which doors are open, where
        /// the actors stand — is scene state, and LevelBootstrapper builds it from
        /// scratch on the way in.
        /// </summary>
        public void Restart()
        {
            if (!IsHost)
            {
                return;
            }

            LoadForEveryone(levelScene);
        }

        /// <summary>Everybody back to the lobby, with the session still up.</summary>
        public void ReturnToLobby()
        {
            if (!IsHost)
            {
                return;
            }

            IsRaidRunning = false;
            LoadForEveryone(lobbyScene);
        }

        // Through FishNet's own scene manager rather than Unity's: it is what carries
        // the load to every client and what keeps spawned objects attached to the right
        // scene. ReplaceOption.All because a raid is not additive — the old scene must
        // be gone, or two levels' worth of actors exist at once.
        private void LoadForEveryone(string sceneName)
        {
            var data = new SceneLoadData(sceneName);
            data.ReplaceScenes = ReplaceOption.All;
            InstanceFinder.SceneManager.LoadGlobalScenes(data);
        }

        // Not through FishNet: by the time this runs we are not connected to anything,
        // so there is nobody to carry the load to.
        private void GoToLobbyAlone()
        {
            if (!string.IsNullOrEmpty(lobbyScene) && UnityScenes.GetActiveScene().name != lobbyScene)
            {
                UnityScenes.LoadScene(lobbyScene);
            }
        }

        // The roster is spawned rather than placed in the Lobby scene: it must not
        // exist before anybody is hosting, and it is shared state, so the server has to
        // be the one that creates it (MECHANICS.md 7.4).
        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started || lobbyRosterPrefab == null)
            {
                return;
            }

            var roster = Instantiate(lobbyRosterPrefab);
            roster.name = lobbyRosterPrefab.name;
            InstanceFinder.ServerManager.Spawn(roster);
        }

        // The door. A raid cannot absorb a latecomer: they would have no idea which
        // animals are already aboard, who is carrying what, which doors are open or
        // where Old Man is, and reconciling all of that is a bigger job than the raid
        // itself (netcode audit, 2026-08-04). So the honest answer is "not now".
        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (!IsRaidRunning || args.ConnectionState != RemoteConnectionState.Started)
            {
                return;
            }

            Debug.Log($"RaidSession: refusing connection {connection.ClientId}, the raid has already started.");
            connection.Disconnect(true);
        }

        // Covers every way a client can end up disconnected — leaving on purpose, the
        // host quitting, the network dropping — with one answer: you are in the lobby
        // now. Without this a dropped client would sit in a level nobody else is in.
        //
        // Stopped means two different things and the transport cannot tell them apart:
        // "never got there" and "was there and lost it". IsConnecting is what separates
        // them, and only the first is worth a popup.
        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
            {
                EndAttempt();
                var connected = Connected;
                if (connected != null)
                {
                    connected();
                }

                return;
            }

            if (args.ConnectionState != LocalConnectionState.Stopped)
            {
                return;
            }

            if (IsConnecting)
            {
                // A failed attempt leaves the server half of a would-be host listening,
                // and a stale listener makes the next Host fail on a busy port.
                StopServerIfStarted();
                Fail("Соединение не найдено");
                return;
            }

            IsRaidRunning = false;
            GoToLobbyAlone();
        }

        private void Update()
        {
            // The backstop. LiteNetLib gives up after about five seconds of its own
            // accord, but only if it is running at all — an address that is routable
            // and silent can otherwise leave the button spinning for ever.
            if (!IsConnecting || Time.unscaledTime < _attemptDeadline)
            {
                return;
            }

            if (InstanceFinder.IsClientStarted)
            {
                InstanceFinder.ClientManager.StopConnection();
            }

            StopServerIfStarted();
            Fail("Соединение не найдено");
        }

        private void BeginAttempt()
        {
            IsConnecting = true;
            _attemptDeadline = Time.unscaledTime + connectTimeout;
        }

        private void EndAttempt()
        {
            IsConnecting = false;
            _attemptDeadline = 0f;
        }

        private void StopServerIfStarted()
        {
            if (InstanceFinder.IsServerStarted)
            {
                InstanceFinder.ServerManager.StopConnection(true);
            }
        }

        private void Fail(string reason)
        {
            EndAttempt();
            Debug.LogWarning($"RaidSession: {reason}.");

            var failed = ConnectFailed;
            if (failed != null)
            {
                failed(reason);
            }
        }
    }
}
