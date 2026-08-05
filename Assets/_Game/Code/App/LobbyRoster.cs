using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

namespace _Game.Code.App
{
    /// <summary>One line of the lobby: who is here and whether they are ready.</summary>
    [Serializable]
    public struct LobbySlot
    {
        /// <summary>FishNet's id for the connection. Identity, not a display name.</summary>
        public int ClientId;

        public string Name;
        public bool Ready;
    }

    /// <summary>
    /// Who is in the lobby, as shared state. Spawned by the host when the session
    /// opens and alive for exactly as long as that session — through Lobby → Level →
    /// Lobby — because its prefab's NetworkObject is marked global and FishNet keeps a
    /// global object in the DDOL scene. It has to be: every scene change in a session
    /// goes out with ReplaceOption.All, which destroys everything in the scene being
    /// replaced, so a scene-local roster died on the host's first ReturnToLobby. The
    /// lobby then showed the Host/Connect screen to a machine that was still hosting,
    /// Host failed on its own busy port, and the only way into a second raid was to
    /// quit the app (measured 2026-08-05). It still says nothing about a raid in
    /// progress — the raid's own state is RaidState.
    ///
    /// The three parts of every change are kept apart the way the rest of this
    /// codebase keeps them (MECHANICS.md 7.4): the client <em>asks</em> through a
    /// ServerRpc, the host <em>decides</em>, and the SyncList is the state change every
    /// peer applies. A client cannot mark somebody else ready, because the connection
    /// the request came from is what names the slot — not an argument it could lie in.
    /// </summary>
    public class LobbyRoster : NetworkBehaviour
    {
        [Tooltip("Refused past this many. Four spawn points exist in the level and a " +
                 "fifth player would have nowhere to stand (MECHANICS.md section 2).")]
        [SerializeField] private int maxPlayers = 4;

        private readonly SyncList<LobbySlot> _slots = new SyncList<LobbySlot>();

        /// <summary>
        /// The roster of the session that is up, or null between sessions. Static for
        /// the same reason <see cref="RaidSession.Active"/> is: the lobby UI lives in
        /// another scene and this object is spawned by the network, so it cannot be
        /// wired to it in the editor and must not be searched for every frame
        /// (MECHANICS.md 7.6). It holds no per-player state — the players are in the
        /// replicated list, not in the static.
        /// </summary>
        public static LobbyRoster Current { get; private set; }

        /// <summary>Raised on every peer whenever the roster changes, for the UI.</summary>
        public event Action Changed;

        /// <summary>The lobby as it stands. Read-only: changing it is the host's job.</summary>
        public SyncList<LobbySlot> Slots => _slots;

        /// <summary>How many are here.</summary>
        public int Count => _slots.Count;

        /// <summary>
        /// Everybody present has said they are ready, and there is at least one of
        /// them. The rule behind the host's Start button, and pure, so the host can ask
        /// it again at the moment the button is actually pressed.
        /// </summary>
        public bool EveryoneReady()
        {
            if (_slots.Count == 0)
            {
                return false;
            }

            for (var i = 0; i < _slots.Count; i++)
            {
                if (!_slots[i].Ready)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>True if this connection has said it is ready. For the local UI.</summary>
        public bool IsReady(int clientId)
        {
            var index = IndexOf(clientId);
            return index >= 0 && _slots[index].Ready;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;

            // Whoever is already connected when this spawns — the host's own client
            // most of all, which connects before this object exists.
            foreach (var connection in ServerManager.Clients.Values)
            {
                Add(connection);
            }
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Current = this;
            _slots.OnChange += OnSlotsChanged;

            // The list arrives with the object, so the first paint has to be asked for
            // rather than waited for.
            Changed?.Invoke();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _slots.OnChange -= OnSlotsChanged;

            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>
        /// Ask to be marked ready or not ready. The connection it came from is the one
        /// that changes — <paramref name="sender"/> is filled in by FishNet, not by the
        /// caller, so this cannot be aimed at anybody else.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestReady(bool ready, NetworkConnection sender = null)
        {
            if (sender == null)
            {
                return;
            }

            var index = IndexOf(sender.ClientId);
            if (index < 0)
            {
                return;
            }

            var slot = _slots[index];
            slot.Ready = ready;
            _slots[index] = slot;
        }

        /// <summary>Ask to be shown under a different name. Same ownership rule.</summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestName(string name, NetworkConnection sender = null)
        {
            if (sender == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var index = IndexOf(sender.ClientId);
            if (index < 0)
            {
                return;
            }

            var slot = _slots[index];

            // Trimmed and capped here rather than in the UI: this is the side that has
            // to survive a client that does not use our UI at all.
            slot.Name = name.Trim();
            if (slot.Name.Length > 16)
            {
                slot.Name = slot.Name.Substring(0, 16);
            }

            _slots[index] = slot;
        }

        private void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                Add(connection);
                return;
            }

            Remove(connection.ClientId);
        }

        private void Add(NetworkConnection connection)
        {
            if (IndexOf(connection.ClientId) >= 0)
            {
                return;
            }

            if (_slots.Count >= maxPlayers)
            {
                // The level has four player spawn points; a fifth has nowhere to stand,
                // and finding that out after the raid starts is worse than being told
                // now.
                Debug.Log($"LobbyRoster: lobby is full, refusing connection {connection.ClientId}.");
                connection.Disconnect(true);
                return;
            }

            _slots.Add(new LobbySlot
            {
                ClientId = connection.ClientId,
                Name = $"Player {connection.ClientId}",
                Ready = false
            });
        }

        private void Remove(int clientId)
        {
            var index = IndexOf(clientId);
            if (index >= 0)
            {
                _slots.RemoveAt(index);
            }
        }

        private int IndexOf(int clientId)
        {
            for (var i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnSlotsChanged(SyncListOperation op, int index, LobbySlot previous, LobbySlot next, bool asServer)
        {
            // One event for the UI whatever happened: it repaints the whole short list
            // rather than trying to patch three rows.
            Changed?.Invoke();
        }
    }
}
