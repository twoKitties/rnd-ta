using System.Collections.Generic;
using System.Text;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace _Game.Code.Doors
{
    /// <summary>
    /// The eight door leaves' shared state: one target angle each, and the authority
    /// that decides them (MECHANICS.md 3.8, 7.4).
    ///
    /// It is a separate object rather than a NetworkBehaviour on each leaf, and that
    /// is the whole point. The leaves are **scene** objects; a scene NetworkObject
    /// deactivates its own GameObject when no NetworkManager is running, and putting
    /// one on each leaf made FishNet auto-add a ninth to the root of
    /// HouseOneFloor.prefab — so on 2026-08-05 the entire house switched itself off in
    /// single player and on every client. Replicated state on a **spawned** object,
    /// with the scene objects staying plain, is the pattern that works here; RaidState
    /// and LobbyRoster are the same shape, and this rides on RaidState's prefab
    /// because it is spawned at exactly the same moment by the same code.
    ///
    /// The leaves themselves keep the rules (<see cref="Door.SwingFor"/>) and the
    /// picture (<see cref="Door.ApplySwing"/>). This only decides and carries.
    /// </summary>
    public class DoorState : NetworkBehaviour
    {
        [Tooltip("How many door leaves the house is expected to register — purely a " +
                 "check, because CollectDoors snapshots Door.All and a leaf whose Awake " +
                 "has not run yet is silently missing from it. That leaf then just never " +
                 "opens, and if the shortfall happens on the server every machine " +
                 "inherits it.")]
        [SerializeField] private int expectedLeaves = 8;

        // One entry per leaf, in the order of _doors below. A SyncList rather than a
        // SyncVar per leaf because the leaves are found at runtime: they live in the
        // scene, so no serialized field can name them.
        private readonly SyncList<float> _swings = new SyncList<float>();

        // The same eight leaves in the same order on every machine — see SortKey.
        private Door[] _doors;

        /// <summary>
        /// The running raid's door authority, or null when there is no networking at
        /// all. Null is a legitimate answer, not a failure: a level played on its own
        /// lets each leaf decide for itself (<see cref="Door.Use"/>).
        /// </summary>
        public static DoorState Current { get; private set; }

        private void Awake()
        {
            // Driven from the replicated value rather than from whoever asked, so the
            // server's own copy and every client's go through exactly one line.
            _swings.OnChange += OnSwingsChanged;
        }

        private void OnDestroy()
        {
            _swings.OnChange -= OnSwingsChanged;
            Release();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            CollectDoors();
            Current = this;

            // Sized here and never resized: the leaves are scene objects, so their
            // number is fixed for the life of the level.
            for (var i = _swings.Count; i < _doors.Length; i++)
            {
                _swings.Add(0f);
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            CollectDoors();
            Current = this;

            // The indices only mean anything while both ends count the same leaves. The
            // guard on Count is not paranoia: on a listen host OnStartServer has already
            // sized the list, and on a pure client the initial SyncList payload arrives
            // before this callback — so a count of zero here means something else
            // entirely, and ApplyAll's Mathf.Min already tolerates it.
            if (_swings.Count > 0 && _doors.Length != _swings.Count)
            {
                Debug.LogError($"DoorState: {_doors.Length} door leaves here but the server sent " +
                               $"{_swings.Count} swings. The two snapshots disagree about the house, " +
                               "so an index no longer names the same door on both machines.");
            }

            // A late joiner walks into a house whose doors are already half open. The
            // full collection arrives with the spawn message, so this is the moment it
            // becomes the picture.
            ApplyAll();
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            Release();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Release();
        }

        private void Release()
        {
            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>
        /// Somebody used <paramref name="door"/>. On the server that is the decision;
        /// on a client it is a request and nothing changes locally until it comes back
        /// (MECHANICS.md 7.4).
        /// </summary>
        public void Use(Door door, Transform actor)
        {
            var index = IndexOf(door);
            if (index < 0)
            {
                return;
            }

            if (IsServerInitialized)
            {
                ServerUse(index, actor);
                return;
            }

            // The client sends the leaf and nothing else. Not where it is standing
            // either — the host works that out from the avatar this connection owns, so
            // the side the leaf swings to cannot be argued about.
            RequestUse(index);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestUse(int index, NetworkConnection sender = null)
        {
            if (_doors == null || index < 0 || index >= _doors.Length)
            {
                return;
            }

            if (sender == null || sender.FirstObject == null)
            {
                return;
            }

            var actor = sender.FirstObject.transform;
            var door = _doors[index];

            // The host re-runs the rule it would have run for itself, including the
            // reach check the asking client already did — a request is not a fact.
            if (Vector3.Distance(actor.position, door.transform.position) > door.ServerReach)
            {
                return;
            }

            ServerUse(index, actor);
        }

        // The decision, taken once, on the machine that owns the rules.
        private void ServerUse(int index, Transform actor)
        {
            var door = _doors[index];

            door.EmitNoise(actor);

            // The state change travels as one float and every peer applies it below —
            // this machine included, through OnChange.
            _swings[index] = door.SwingFor(actor);
        }

        private void OnSwingsChanged(SyncListOperation op, int index, float previous, float next, bool asServer)
        {
            if (_doors == null)
            {
                return;
            }

            // Every operation is answered the same way rather than switched on: there
            // are eight leaves, this runs when somebody opens a door, and Complete
            // (the whole collection at once) has to be handled anyway.
            if (index < 0 || index >= _doors.Length || index >= _swings.Count)
            {
                ApplyAll();
                return;
            }

            _doors[index].ApplySwing(next);
        }

        private void ApplyAll()
        {
            if (_doors == null)
            {
                return;
            }

            var count = Mathf.Min(_doors.Length, _swings.Count);
            for (var i = 0; i < count; i++)
            {
                _doors[i].ApplySwing(_swings[i]);
            }
        }

        private int IndexOf(Door door)
        {
            if (_doors == null || door == null)
            {
                return -1;
            }

            for (var i = 0; i < _doors.Length; i++)
            {
                if (_doors[i] == door)
                {
                    return i;
                }
            }

            return -1;
        }

        // Called on both start callbacks, so on a host it must be idempotent.
        private void CollectDoors()
        {
            if (_doors != null)
            {
                return;
            }

            var doors = new List<Door>(Door.All);

            // An index means nothing unless every machine numbers the leaves the same
            // way, and registration order is Awake order, which Unity does not promise.
            // The sibling-index path does promise it: the leaves are scene objects, so
            // every peer loads the identical hierarchy, and the path is unique by
            // construction. Compared ordinally — a culture-aware compare is not the
            // same order on a Russian machine as on an English one.
            var keys = new Dictionary<Door, string>(doors.Count);
            foreach (var door in doors)
            {
                keys[door] = SortKey(door.transform);
            }

            doors.Sort((a, b) => string.CompareOrdinal(keys[a], keys[b]));
            _doors = doors.ToArray();

            // Door.All is filled in each leaf's Awake and Unity does not promise Awake
            // order between root objects, so this can run before every leaf has woken.
            // Nothing fails loudly when it does: a missing leaf is simply not in the
            // snapshot and never opens, and on the server the SyncList is sized short,
            // so ApplyAll's Mathf.Min leaves the tail shut on every machine for the rest
            // of the raid. Reads as "this door is broken" unless it is said here.
            if (_doors.Length != expectedLeaves)
            {
                Debug.LogError($"DoorState: found {_doors.Length} door leaves but expected " +
                               $"{expectedLeaves}. Doors missing from the snapshot never open, " +
                               "and on the server the swing list is sized short for everyone.");
            }
        }

        private static string SortKey(Transform leaf)
        {
            var key = new StringBuilder();
            for (var t = leaf; t != null; t = t.parent)
            {
                // Fixed width so the string order is the hierarchy order rather than
                // "10" sorting before "2". Six digits is far past any sane sibling count.
                key.Insert(0, t.GetSiblingIndex().ToString("D6") + "/");
            }

            return key.ToString();
        }
    }
}
