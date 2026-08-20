using _Game.Code.Player;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace _Game.Code.Level
{
    /// <summary>
    /// The four numbers every screen has to agree on: how many animals are aboard, how
    /// many there are, and whether the raid is over and how (MECHANICS.md section 6).
    ///
    /// Its own prefab, spawned by the server, rather than a NetworkBehaviour bolted
    /// onto the scene's entry point. That was tried on 2026-08-05 and reverted: a
    /// scene object whose NetworkObject fails to come up is **deactivated** by FishNet,
    /// and <see cref="LevelGoal"/> shares its GameObject with LevelBootstrapper, so the
    /// whole level went with it. The pattern that works here is LobbyRoster's — shared
    /// state on a spawned object, and the scene object holding a plain reference.
    ///
    /// <see cref="LevelGoal"/> is still where the rules live. This only carries their
    /// result across the wire.
    /// </summary>
    public class RaidState : NetworkBehaviour
    {
        private readonly SyncVar<int> _delivered = new SyncVar<int>();
        private readonly SyncVar<int> _total = new SyncVar<int>();
        private readonly SyncVar<bool> _isWon = new SyncVar<bool>();
        private readonly SyncVar<bool> _isLost = new SyncVar<bool>();
        private readonly SyncVar<float> _duration = new SyncVar<float>();

        // Server-side only: the clock the duration above is measured against.
        private float _startedAt;

        /// <summary>
        /// The raid that is running, or null when there is no networking at all — and
        /// null is a legitimate answer, not a failure: the level played on its own
        /// keeps its numbers in <see cref="LevelGoal"/> itself.
        /// </summary>
        public static RaidState Current { get; private set; }

        public int Delivered => _delivered.Value;
        public int Total => _total.Value;
        public bool IsWon => _isWon.Value;
        public bool IsLost => _isLost.Value;

        /// <summary>How long the raid took, seconds. Meaningful once it is over.</summary>
        public float Duration => _duration.Value;

        /// <summary>True where the decisions are taken. Safe to ask any time.</summary>
        public bool IsAuthority => IsServerInitialized;

        public override void OnStartServer()
        {
            base.OnStartServer();

            // This object is spawned as the level is laid out, so its own lifetime is
            // the raid's — no separate start signal is needed.
            _startedAt = Time.time;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Current = this;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();

            if (Current == this)
            {
                Current = null;
            }
        }

        /// <summary>How many animals this raid is about. Set once, when the level is laid out.</summary>
        public void SetTotal(int total)
        {
            if (!IsAuthority)
            {
                return;
            }

            _total.Value = total;
            _delivered.Value = 0;
        }

        /// <summary>One more animal aboard. The counter is the authority's alone.</summary>
        public void AddDelivery()
        {
            if (!IsAuthority)
            {
                return;
            }

            _delivered.Value = _delivered.Value + 1;
        }

        /// <summary>
        /// A client is putting its animal down and wants to know whether that counted.
        /// It lands here because <see cref="LevelGoal"/> is a plain scene component and
        /// cannot carry an RPC of its own — the same division as DoorState and the eight
        /// door leaves.
        ///
        /// The server re-runs the whole rule rather than believing the answer: it checks
        /// that the avatar really belongs to the connection that asked, and then asks the
        /// goal, on this machine, whether those hands are standing in the beam
        /// (MECHANICS.md 4.5, 7.4).
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestRelease(NetworkObject carrier, NetworkConnection sender = null)
        {
            if (carrier == null || sender == null || carrier.Owner != sender)
            {
                return;
            }

            var goal = LevelGoal.Current;
            var hands = carrier.GetComponent<PlayerHands>();

            // Unity objects: a destroyed one compares == null but is not a real null.
            if (goal != null && hands != null)
            {
                goal.ReleaseCarried(hands);
            }
        }

        /// <summary>Latch the outcome so it stops being recomputed and starts being shown.</summary>
        public void SetOutcome(bool won, bool lost)
        {
            if (!IsAuthority || _isWon.Value || _isLost.Value)
            {
                return;
            }

            _isWon.Value = won;
            _isLost.Value = lost;
            _duration.Value = Time.time - _startedAt;
        }
    }
}
