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

            // The animal falls where it is and goes back to fleeing — except inside the
            // beam, where LevelGoal's rule still applies. Pet.Release is what knows the
            // difference, so it is asked rather than reimplemented here.
            var hands = GetComponent<PlayerHands>();
            if (hands != null && !hands.IsEmpty)
            {
                hands.Carried.Release();
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
