using UnityEngine;

namespace _Game.Code.Noise
{
    /// <summary>
    /// How loud one actor is right now, 0..100, and how far that carries. Numbers
    /// come from the "Передвижение и шум игрока" tables in MECHANICS.md section 2 —
    /// change them there first, one at a time.
    ///
    /// One of these per actor, never a global value and never a static bus
    /// (MECHANICS.md 7.5): a shared scale would punish a quiet player for a loud
    /// one, and would leave a listener no way to pick which source to walk to.
    /// Listeners poll emitters; emitters know nothing about who is listening.
    /// </summary>
    public class NoiseEmitter : MonoBehaviour
    {
        [Header("Noise (MECHANICS.md section 2)")]
        [Tooltip("Units per second the noise falls towards zero once the action stops.")]
        [SerializeField] private float decayPerSecond = 40f;

        [Tooltip("Hearing radius per unit of noise, metres. One rule for every source.")]
        [SerializeField] private float metresPerUnit = 0.4f;

        /// <summary>Current loudness, 0..100.</summary>
        public float Level { get; private set; }

        /// <summary>How far this noise can be heard right now, world metres.</summary>
        public float HearingRadius => Level * metresPerUnit;

        /// <summary>
        /// Report an action's loudness. The loudest claim in a frame wins rather than
        /// the last one: a sprinting player who also opens a door is as loud as the
        /// sprint, not as loud as whichever component happened to run second.
        /// </summary>
        public void Emit(float level)
        {
            if (level > Level)
            {
                Level = level;
            }
        }

        // Decay in LateUpdate, not Update, so that a sustained action (walking) can
        // re-Emit its level in Update and the value a listener reads that frame is
        // the action's own number rather than the number minus one frame of decay.
        // Script execution order between two Updates is undefined; this makes the
        // order irrelevant.
        private void LateUpdate()
        {
            if (Level <= 0f)
            {
                return;
            }

            Level = Mathf.MoveTowards(Level, 0f, decayPerSecond * Time.deltaTime);
        }
    }
}
