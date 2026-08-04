using FishNet.Object;
using UnityEngine;

namespace _Game.Code.App
{
    /// <summary>
    /// Marks the parts of an actor that only the server may run. Everybody else sees
    /// the result arrive as a replicated transform.
    ///
    /// This is what the animals and Old Man need and the players do not: a
    /// <see cref="UnityEngine.AI.NavMeshAgent"/> integrates locally, off its own
    /// pathfinding and its own frame timing, so four machines running one would give
    /// four different animals with the same name. One machine decides where the Dog
    /// runs; the rest are shown.
    ///
    /// Written as one list of references rather than a check inside each brain,
    /// deliberately: the AI code is the most tuned thing in this project and the
    /// cheapest way to leave its behaviour untouched is not to edit it at all. It is
    /// the same idiom as <see cref="_Game.Code.Player.LocalAvatar"/> and
    /// PlayerLife.disableOnDeath.
    /// </summary>
    public class ServerSimulated : NetworkBehaviour
    {
        [Tooltip("Switched off on every peer that is not the server: the brain and the " +
                 "NavMeshAgent it drives.")]
        [SerializeField] private Behaviour[] serverOnly;

        public override void OnStartClient()
        {
            base.OnStartClient();

            // A host is both, and must keep simulating. IsServerInitialized rather than
            // IsServerStarted: this asks whether *this object* is live on the server,
            // which is the question that matters for the components on it.
            if (IsServerInitialized)
            {
                return;
            }

            for (var i = 0; i < serverOnly.Length; i++)
            {
                // Unity object: a destroyed one compares == null but is not a real
                // null, so `?.` and `??` would lie about it.
                if (serverOnly[i] != null)
                {
                    serverOnly[i].enabled = false;
                }
            }
        }
    }
}
