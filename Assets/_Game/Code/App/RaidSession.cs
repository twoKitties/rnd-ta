using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Transporting;
using _Game.Code.UI;
using UnityEngine;

// Both FishNet and Unity have a SceneManager and this file uses both. The alias keeps
// every call site saying which one it means.
using UnityScenes = UnityEngine.SceneManagement.SceneManager;

namespace _Game.Code.App
{
    /// <summary>
    /// One co-op session, from the menu to the end of a raid and back. Lives on the
    /// object <see cref="Bootstrapper"/> keeps alive for the whole launch, next to the
    /// NetworkManager, because it has to outlive Menu → Hub → Level → Hub.
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
        [Tooltip("The entry point: connecting happens here and nowhere else.")]
        [SerializeField] private string menuScene = "Menu";

        [Tooltip("Where players stand between raids and pick where to fly.")]
        [SerializeField] private string hubScene = "Hub";

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

        [Tooltip("How long a silent connection is kept before the transport declares the " +
                 "other end gone, seconds. Tugboat's own default is 1800 — half an hour — " +
                 "which is a debugger's value, not a game's. Raise it if you attach a " +
                 "breakpoint, because a stall longer than this now drops the session.")]
        [SerializeField] private float deadPeerTimeout = 8f;

        [Tooltip("How long a client may sit in a raid with no avatar of its own before " +
                 "it gives up and returns to the menu, seconds. Generous, because a " +
                 "flight legitimately takes the avatar away for a scene load or two.")]
        [SerializeField] private float orphanTimeout = 10f;

        /// <summary>True once a raid has started and the door on late join has closed.</summary>
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

        // We have been in a session at some point and have not been put back in the
        // menu yet. This is what the backstop in Update watches: a raid must not be
        // able to outlive the connection it belongs to.
        private bool _inSession;

        // When this client last lost its own avatar, or zero while it has one.
        private float _avatarLostAt;

        // "Never got one" has no callback of its own — LocalAvatarLost only fires for
        // an avatar we had.
        private bool _hasAvatar;

        // Why the last session ended, when it ended without being asked to. Held rather
        // than raised as an event: it happens in the Level, and the only thing that can
        // show it is the menu's popup, which does not exist until a scene later. An
        // event would be shouted into an empty room.
        private string _notice;

        /// <summary>True on the machine that is hosting — the one that owns the rules.</summary>
        public bool IsHost => InstanceFinder.IsServerStarted;

        /// <summary>The port this build hosts on. Read by the LAN beacon, which announces it.</summary>
        public ushort Port => port;

        /// <summary>
        /// The address to read out to the other players, as <c>ip:port</c>.
        ///
        /// Found by asking a UDP socket which interface it would use to reach the
        /// outside world, rather than by taking the first address the machine lists:
        /// a developer's machine has Bluetooth, Hyper-V, VPN and link-local adapters
        /// too, and picking the wrong one hands out an address nobody can reach. No
        /// packet is sent — connecting a UDP socket only chooses a route.
        /// </summary>
        public string LocalEndpoint => $"{LocalAddress()}:{port}";

        /// <summary>
        /// Every IPv4 address this machine has that somebody could plausibly connect
        /// to, best guess first. Public because when the first guess is wrong the
        /// player needs to see the alternatives rather than be told one wrong number.
        /// </summary>
        public static List<string> LocalAddresses()
        {
            var wifi = new List<string>();
            var wired = new List<string>();
            var other = new List<string>();

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var entry in nic.GetIPProperties().UnicastAddresses)
                {
                    if (entry.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var text = entry.Address.ToString();

                    // 169.254.x is what Windows invents for an adapter with no DHCP —
                    // a cable that is not plugged in, Bluetooth, an idle virtual NIC.
                    // Nobody can reach it.
                    if (text.StartsWith("169.254"))
                    {
                        continue;
                    }

                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        wifi.Add(text);
                    }
                    else if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                    {
                        wired.Add(text);
                    }
                    else
                    {
                        other.Add(text);
                    }
                }
            }

            // Wi-Fi and cable first, tunnels last. Measured 2026-08-05 on this machine:
            // asking the routing table which interface reaches the internet returned the
            // VPN's 10.69.64.9, not the 192.168.0.63 the other machine in the room has
            // to use — an active VPN owns the default route and would hand out an
            // address nobody on the LAN can connect to.
            var all = new List<string>();
            all.AddRange(wifi);
            all.AddRange(wired);
            all.AddRange(other);
            return all;
        }

        private string LocalAddress()
        {
            var all = LocalAddresses();
            return all.Count == 0 ? "?" : all[0];
        }

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

            // Measured 2026-08-05, and it is why a client whose host disappeared sat in
            // a dead level until the application was killed: Tugboat leaves LiteNetLib's
            // DisconnectTimeout at its own MAX_TIMEOUT_SECONDS of 1800, so a peer that
            // stops answering without saying goodbye — a build closed by the task
            // manager, a cable pulled — is not noticed for half an hour. Nothing above
            // the transport can tell: FishNet still reports the client as started, so
            // every "are we connected" check, including this class's own backstop,
            // answers yes.
            //
            // A graceful close does send a disconnect and was always seen promptly;
            // this is only about the ungraceful one.
            var transport = manager.TransportManager.Transport;
            if (transport != null)
            {
                transport.SetTimeout(deadPeerTimeout, true);
                transport.SetTimeout(deadPeerTimeout, false);
            }
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
                Fail($"Could not take port {port}");
                return false;
            }

            // Every candidate, not just the one shown in the hub: with a VPN up the
            // best guess can still be the wrong one, and the player needs to be able to
            // try the next.
            // The timeout is in the line on purpose: when it is wrong, every symptom is
            // "the other machine hangs", and the only way to tell a wrong value from a
            // wrong cause is to read it out of the log of the run that failed.
            Debug.Log($"RaidSession: hosting on port {port}, dead-peer timeout {deadPeerTimeout} s. " +
                      $"Addresses: {string.Join(", ", LocalAddresses())}");

            BeginAttempt();
            if (InstanceFinder.ClientManager.StartConnection(hostAddress, port))
            {
                return true;
            }

            Fail("Could not connect to host");
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
                Fail("Address not recognized");
                return false;
            }

            Debug.Log($"RaidSession: joining {address}:{chosenPort}, dead-peer timeout {deadPeerTimeout} s.");

            BeginAttempt();
            if (InstanceFinder.ClientManager.StartConnection(address, chosenPort))
            {
                return true;
            }

            Fail("Connection not found");
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
        /// menu through <see cref="OnClientConnectionState"/>; a host stops listening,
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
            GoToMenuAlone();
        }

        /// <summary>
        /// Why the last session ended, if it ended on its own — and clears it, so it is
        /// shown once and never again. Read by the menu when it comes up, because that
        /// is the first moment there is anything on screen able to say it.
        ///
        /// Empty after a session the player ended themselves: being told "the connection
        /// was lost" after pressing Leave is worse than being told nothing.
        /// </summary>
        public string ConsumeNotice()
        {
            var notice = _notice;
            _notice = null;
            return notice;
        }

        /// <summary>
        /// This client's own avatar has arrived. Told rather than looked for: the level
        /// knows about the session, the session must not have to know about the level.
        /// </summary>
        public void LocalAvatarSpawned()
        {
            _hasAvatar = true;
            _avatarLostAt = 0f;

            if (LoadingScreen.Active != null)
            {
                LoadingScreen.Active.AvatarReady();
            }
        }

        /// <summary>
        /// This client's own avatar has been despawned, and it did not ask for that.
        ///
        /// This is the signal that survives when nothing else does. A host tearing its
        /// server down despawns every object first and says goodbye second, and the
        /// goodbye is a single unreliable UDP packet — measured 2026-08-05, the despawns
        /// arrived and the disconnect did not, so FishNet went on reporting a healthy
        /// connection while the client sat in a level with no avatar, no camera and no
        /// menu. Losing our own avatar without leaving is not something that happens in
        /// a working raid, so it is enough on its own.
        ///
        /// The host is exempt: it cannot be orphaned by itself.
        /// </summary>
        public void LocalAvatarLost()
        {
            _hasAvatar = false;

            if (IsHost || _avatarLostAt > 0f)
            {
                return;
            }

            _avatarLostAt = Time.unscaledTime;
        }

        /// <summary>
        /// Take everybody into a location. The host's call, and the moment the door on
        /// late join closes: from here a new connection is refused rather than dropped
        /// into a raid whose state it has no way of catching up with.
        /// </summary>
        public void StartRaid(string locationScene)
        {
            if (!IsHost || string.IsNullOrEmpty(locationScene))
            {
                return;
            }

            IsRaidRunning = true;
            LoadForEveryone(locationScene, LoadingScreen.Wait.AvatarReady);
        }

        /// <summary>
        /// Everybody into the hub, with the session still up. Both the way in from the
        /// menu and the way out of a finished raid — and, because the hub is a real
        /// scene load, the way a lost raid is played again: everything latched is scene
        /// state and the next flight builds it from scratch.
        /// </summary>
        public void GoToHub()
        {
            if (!IsHost)
            {
                return;
            }

            IsRaidRunning = false;
            LoadForEveryone(hubScene, LoadingScreen.Wait.AvatarReady);
        }

        // Through FishNet's own scene manager rather than Unity's: it is what carries
        // the load to every client and what keeps spawned objects attached to the right
        // scene. ReplaceOption.All because a raid is not additive — the old scene must
        // be gone, or two levels' worth of actors exist at once.
        //
        // destination is what the loading screen waits for; it travels because a client
        // never calls these methods itself.
        private void LoadForEveryone(string sceneName, LoadingScreen.Wait destination)
        {
            var data = new SceneLoadData(sceneName);
            data.ReplaceScenes = ReplaceOption.All;

            // ClientParams is the half of LoadParams that travels; ServerParams is [NonSerialized].
            data.Params.ClientParams = new[] { (byte)destination };

            InstanceFinder.SceneManager.LoadGlobalScenes(data);
        }

        // Not through FishNet: by the time this runs we are not connected to anything,
        // so there is nobody to carry the load to.
        private void GoToMenuAlone()
        {
            _inSession = false;
            _avatarLostAt = 0f;
            _hasAvatar = false;

            // Also re-targets a screen still waiting for an avatar that will never arrive.
            if (LoadingScreen.Active != null)
            {
                LoadingScreen.Active.Show(LoadingScreen.Wait.MainMenu);
            }

            if (!string.IsNullOrEmpty(menuScene) && UnityScenes.GetActiveScene().name != menuScene)
            {
                UnityScenes.LoadScene(menuScene);
            }
        }

        // The roster is spawned rather than placed in the Menu scene: it must not
        // exist before anybody is hosting, and it is shared state, so the server has to
        // be the one that creates it (MECHANICS.md 7.4). Its prefab is global, so it
        // outlives the menu the way the session itself does.
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
        // host quitting, the network dropping — with one answer: you are in the menu
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
                _inSession = true;
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
                Fail("Connection not found");
                return;
            }

            // Still in a session means we did not ask for this: Leave clears the flag
            // before the transport ever reports Stopped, so a player who chose to go
            // gets no popup and a player who was dropped does.
            if (_inSession)
            {
                _notice = "Connection lost";
            }

            IsRaidRunning = false;
            GoToMenuAlone();
        }

        private void Update()
        {
            // The session is gone and we are still somewhere it put us. Watched every
            // frame rather than handled in the disconnect callback alone, because that
            // callback is one link in a chain FishNet runs while it is tearing every
            // spawned object down — and a client left standing in a level with no host,
            // no avatar and therefore no menu has no way out at all (reported
            // 2026-08-05, host quitting the build). This asks the only question that
            // matters and cannot be missed: are we still connected to anything.
            if (_inSession && !IsConnecting && !InstanceFinder.IsClientStarted && !InstanceFinder.IsServerStarted)
            {
                IsRaidRunning = false;
                _notice = "Connection lost";
                GoToMenuAlone();
                return;
            }

            // A connection the server never spawned for: no camera, no HUD, so no menu
            // and no way out. Every scene but the menu spawns avatars, and there will be
            // more than one location — so the test is "not the menu", not a scene name.
            if (_inSession && !IsHost && !_hasAvatar && _avatarLostAt <= 0f &&
                UnityScenes.GetActiveScene().name != menuScene)
            {
                _avatarLostAt = Time.unscaledTime;
            }

            // Orphaned: our avatar went away and no new one arrived. See LocalAvatarLost
            // for why this exists at all — it is the only signal that reaches a client
            // whose host tore the session down and whose goodbye packet was lost.
            if (_avatarLostAt > 0f && !IsConnecting && Time.unscaledTime - _avatarLostAt > orphanTimeout)
            {
                Debug.LogWarning($"RaidSession: no avatar for {orphanTimeout} s and no new one arriving — " +
                                 "treating the session as over and returning to the menu.");

                // Set before Leave, which goes through GoToMenuAlone and would otherwise
                // look exactly like the player having chosen to leave.
                _notice = "Game ended by host";
                Leave();
                return;
            }

            // The other backstop. LiteNetLib gives up after about five seconds of its
            // own accord, but only if it is running at all — an address that is routable
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
            Fail("Connection not found");
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
