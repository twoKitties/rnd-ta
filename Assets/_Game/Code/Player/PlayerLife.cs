using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using _Game.Code.Level;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Whether this avatar is still alive, and what dying does to it. Old Man's pellet
    /// kills on impact and there is no respawn (MECHANICS.md 3.7): the player becomes
    /// a spectator, so the avatar keeps its camera and its HUD and loses everything
    /// else.
    ///
    /// Deliberately not SetActive(false) on the avatar: the camera lives inside
    /// Player.prefab, and switching the object off would black the screen out with no
    /// way to see what killed you or how the raid ends.
    ///
    /// **Death is replicated, and it has to be.** Old Man runs on the server alone
    /// (ServerSimulated), so his shot only ever reaches this component there. Until
    /// 2026-08-05 that was the whole story and it broke three things at once, measured
    /// on a build-plus-editor session: a shot client went on playing while the server
    /// counted them dead, so Old Man appeared to hear them and never fire; the raid
    /// was declared lost the moment the host died, because the server already had
    /// nobody living; and the host had no living teammate left to spectate.
    ///
    /// Split the same way Pet is (MECHANICS.md 7.4): <see cref="Kill"/> is the
    /// authority's decision, the replicated flag carries it, and ApplyDeath is the
    /// state change every peer runs.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerLife : NetworkBehaviour
    {
        [Tooltip("Switched off on death. The camera and the HUD are deliberately not here.")]
        [SerializeField] private MonoBehaviour[] disableOnDeath;

        /// <summary>True once this avatar has been shot. LevelGoal counts the living.</summary>
        public bool IsDead { get; private set; }

        // The one thing that travels. Every peer reacts to it below — the authority
        // included, so there is exactly one path to a dead player.
        private readonly SyncVar<bool> _dead = new SyncVar<bool>();

        private LevelGoal _goal;

        /// <summary>
        /// Where death is decided: the server, or a process with no networking at all.
        /// Asked of the process rather than of this object's spawn state, because it is
        /// also the question during despawn — a player leaving is killed on the way out
        /// (MECHANICS.md 3.7), and by then the object's own answer is already changing.
        /// </summary>
        private static bool DecidesHere =>
            InstanceFinder.NetworkManager == null || !InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted;

        private void Awake()
        {
            _dead.OnChange += OnDeadChanged;
        }

        private void OnDestroy()
        {
            _dead.OnChange -= OnDeadChanged;
        }

        private void OnDeadChanged(bool previous, bool next, bool asServer)
        {
            if (next)
            {
                ApplyDeath();
            }
        }

        /// <summary>
        /// Handed over by LevelBootstrapper right after this avatar is spawned, the same
        /// way PlayerInteractor is: the goal is a scene object, so a spawned prefab
        /// cannot reference it up front (MECHANICS.md 7.6).
        /// </summary>
        public void Bind(LevelGoal goal)
        {
            _goal = goal;
        }

        /// <summary>
        /// Kill this avatar. Idempotent: a second shot in the same frame — two
        /// listeners, or the host's flag arriving after its own local apply — must not
        /// drop the carried animal twice.
        ///
        /// A client never decides this and its call is dropped. It does not need to
        /// ask, either: the only two things that kill are Old Man, who lives on the
        /// server, and a player leaving, which the server sees as a despawn.
        /// </summary>
        public void Kill()
        {
            if (IsDead || !DecidesHere)
            {
                return;
            }

            // Cached through the NetworkObject rather than NetworkBehaviour.IsSpawned:
            // that property throws when the behaviour was never initialised, which is
            // exactly the case in a level played with no networking at all.
            var nob = NetworkObject;
            if (nob != null && nob.IsSpawned)
            {
                _dead.Value = true;
                return;
            }

            // No network, or the object is already on its way out — either way this
            // machine is the only one that will ever run it.
            ApplyDeath();
        }

        private void ApplyDeath()
        {
            if (IsDead)
            {
                return;
            }

            IsDead = true;

            // The authority's alone: releasing inside the beam hands the animal over and
            // increments a counter, and running that on four machines would count it
            // four times. Everybody else sees the animal leave these hands anyway —
            // Pet's carrier is replicated and puts it down on its own.
            if (DecidesHere)
            {
                // Through the goal rather than straight through Pet.Release, because a
                // carrier shot inside the beam still hands it over (MECHANICS.md 3.7)
                // and LevelGoal is the only thing that knows where the beam is.
                // Unbound — a scene without a goal — it just drops.
                var hands = GetComponent<PlayerHands>();
                if (hands != null && !hands.IsEmpty)
                {
                    if (_goal != null)
                    {
                        _goal.ReleaseCarried(hands);
                    }
                    else
                    {
                        hands.Carried.Release();
                    }
                }
            }

            for (var i = 0; i < disableOnDeath.Length; i++)
            {
                // Unity object: a destroyed one compares == null but is not a real null,
                // so `?.` and `??` would lie about it.
                if (disableOnDeath[i] != null)
                {
                    disableOnDeath[i].enabled = false;
                }
            }

            // Stops the corpse from walking and from blocking anyone in a doorway.
            var body = GetComponent<CharacterController>();
            if (body != null)
            {
                body.enabled = false;
            }

            Debug.Log($"{name} was shot. No respawn (MECHANICS.md 3.7).");
        }
    }
}
