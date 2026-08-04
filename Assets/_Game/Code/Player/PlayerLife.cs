using _Game.Code.Level;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Whether this avatar is still alive, and what dying does to it. Old Man's shot
    /// kills instantly and there is no respawn (MECHANICS.md 3.7): the player becomes
    /// a spectator, so the avatar keeps its camera and its HUD and loses everything
    /// else.
    ///
    /// Deliberately not SetActive(false) on the avatar: the camera lives inside
    /// Player.prefab, and switching the object off would black the screen out with no
    /// way to see what killed you or how the raid ends.
    ///
    /// Split for the netcode pass (MECHANICS.md 7.4) the same way Pet is: Kill is the
    /// authority's decision, ApplyDeath is the state change every peer will run.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerLife : MonoBehaviour
    {
        [Tooltip("Switched off on death. The camera and the HUD are deliberately not here.")]
        [SerializeField] private MonoBehaviour[] disableOnDeath;

        /// <summary>True once this avatar has been shot. LevelGoal counts the living.</summary>
        public bool IsDead { get; private set; }

        private LevelGoal _goal;

        /// <summary>
        /// Handed over by Bootstrapper right after this avatar is spawned, the same
        /// way PlayerInteractor is: the goal is a scene object, so a spawned prefab
        /// cannot reference it up front (MECHANICS.md 7.6).
        /// </summary>
        public void Bind(LevelGoal goal)
        {
            _goal = goal;
        }

        /// <summary>
        /// Kill this avatar. Idempotent: a second shot in the same frame — two
        /// listeners, or the host confirming what the client already showed — must not
        /// drop the carried animal twice.
        /// </summary>
        public void Kill()
        {
            if (IsDead)
            {
                return;
            }

            ApplyDeath();
        }

        private void ApplyDeath()
        {
            IsDead = true;

            // The animal is put down through the goal rather than straight through
            // Pet.Release, because a carrier shot inside the beam still hands it over
            // (MECHANICS.md 3.7) and LevelGoal is the only thing that knows where the
            // beam is. Unbound — a scene without a goal — it just drops.
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
