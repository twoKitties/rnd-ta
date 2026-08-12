using System.Collections.Generic;
using _Game.Code.Player;
using _Game.Code.Spawning;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace _Game.Code.Hub
{
    /// <summary>
    /// The hub's entry point: the saucer between raids. It spawns the avatars and hands
    /// the local one to the hub's menu, and that is all — there is no AI here, no
    /// navmesh and no outcome to judge.
    ///
    /// Unlike <see cref="LevelBootstrapper"/> it does not wait for everybody. An avatar
    /// belongs to one connection, so the right moment to make it is that connection
    /// arriving in this scene, which also covers somebody joining while the others are
    /// already standing here.
    /// </summary>
    public class HubBootstrapper : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Scene")]
        [SerializeField] private Transform spawnsRoot;

        [SerializeField] private HubMenuUI hubMenu;

        /// <summary>
        /// The hub while it is loaded, or null when we are not in it. Static for the
        /// same reason LevelBootstrapper.Current is: an avatar arrives replicated and
        /// has to find the scene it landed in without searching for it.
        /// </summary>
        public static HubBootstrapper Current { get; private set; }

        private readonly List<Transform> _points = new List<Transform>();

        // Connections that already have an avatar here, so a presence event and the
        // sweep in Awake cannot make two.
        private readonly List<int> _served = new List<int>();

        // Who lays the hub out. Same split as the level: the server, or a process with
        // no networking at all.
        private static bool IsAuthority =>
            InstanceFinder.NetworkManager == null || !InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted;

        private static bool IsNetworked => InstanceFinder.NetworkManager != null && InstanceFinder.IsServerStarted;

        private void Awake()
        {
            Current = this;

            if (playerPrefab == null || spawnsRoot == null)
            {
                Debug.LogError("HubBootstrapper: playerPrefab or spawnsRoot is not assigned, nobody can be spawned.");
                return;
            }

            var points = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);
            foreach (var point in points)
            {
                if (point.Kind == SpawnKind.Player)
                {
                    _points.Add(point.transform);
                }
            }

            if (_points.Count == 0)
            {
                Debug.LogError("HubBootstrapper: no player spawn points under spawnsRoot.");
                return;
            }

            // A client creates nothing: every avatar, its own included, arrives
            // replicated.
            if (!IsAuthority)
            {
                return;
            }

            if (!IsNetworked)
            {
                SpawnAlone();
                return;
            }

            InstanceFinder.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChangeEnd;
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;

            foreach (var connection in InstanceFinder.ServerManager.Clients.Values)
            {
                if (connection.Scenes.Contains(gameObject.scene))
                {
                    Spawn(connection);
                }
            }
        }

        private void OnDestroy()
        {
            if (InstanceFinder.NetworkManager != null)
            {
                InstanceFinder.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChangeEnd;
                InstanceFinder.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }

            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>
        /// An avatar has arrived on this peer. Called by <see cref="LocalAvatar"/>, the
        /// same way the level is told: three of the four were not created here.
        /// </summary>
        public void AddPlayer(GameObject avatar)
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (avatar == null)
            {
                return;
            }

            var local = avatar.GetComponent<LocalAvatar>();
            if (local == null || !local.IsLocal || hubMenu == null)
            {
                return;
            }

            hubMenu.Bind(avatar);
        }

        /// <summary>An avatar has gone. Only ours is worth reacting to.</summary>
        public void RemovePlayer(GameObject avatar)
        {
            if (avatar == null || hubMenu == null)
            {
                return;
            }

            var local = avatar.GetComponent<LocalAvatar>();
            if (local != null && local.IsLocal)
            {
                hubMenu.Bind(null);
            }
        }

        private void OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
        {
            if (args.Added && args.Scene == gameObject.scene)
            {
                Spawn(args.Connection);
            }
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Started)
            {
                _served.Remove(connection.ClientId);
            }
        }

        private void Spawn(NetworkConnection connection)
        {
            if (connection == null || _served.Contains(connection.ClientId))
            {
                return;
            }

            var point = _points[_served.Count % _points.Count];
            var avatar = Instantiate(playerPrefab, point.position, point.rotation);
            avatar.name = playerPrefab.name;

            _served.Add(connection.ClientId);

            // Registration happens in LocalAvatar.OnStartClient, on every peer.
            InstanceFinder.ServerManager.Spawn(avatar, connection);
        }

        // Playing the hub on its own: one avatar, claimed outright, and no networking to
        // ask about ownership.
        private void SpawnAlone()
        {
            var point = _points[0];
            var avatar = Instantiate(playerPrefab, point.position, point.rotation);
            avatar.name = playerPrefab.name;

            var nob = avatar.GetComponent<NetworkObject>();
            if (nob != null)
            {
                // FishNet switches an unspawned NetworkObject's GameObject off in its own
                // Start; Instantiate has run Awake but not Start, so this is in time.
                nob.SetIsNetworked(false);
            }

            var local = avatar.GetComponent<LocalAvatar>();
            if (local != null)
            {
                local.Apply(true);
            }

            AddPlayer(avatar);
        }
    }
}
