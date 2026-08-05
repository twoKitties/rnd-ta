using FishNet.Object;
using UnityEngine;

namespace _Game.Code.OldMan
{
    /// <summary>
    /// Everything a shot from Old Man looks and sounds like: a light blinked for a few
    /// frames, and the bang. There is no shooting animation and that is a decision, not
    /// an omission (MECHANICS.md 5.6) — which makes these two the only thing a player
    /// ever gets of the shot that killed them, and the sound the more important of
    /// them, because the flash is over in 0.06 s and a victim facing away misses it.
    ///
    /// It is a component of its own, and a NetworkBehaviour, because
    /// <see cref="OldManBrain"/> runs on the server alone (ServerSimulated): a light
    /// switched on there is a light nobody else sees, so until 2026-08-05 a client was
    /// killed by nothing at all. The brain still decides *when*; this owns *showing*,
    /// the same split as Pet.ApplyCarry against PetBrain.
    /// </summary>
    public class ShotFlash : NetworkBehaviour
    {
        [Tooltip("Blinked when he fires. Optional — without it the shot is the log alone.")]
        [SerializeField] private Light flash;

        [Tooltip("How long the flash stays lit, s (MECHANICS.md section 2).")]
        [SerializeField] private float flashTime = 0.06f;

        [Header("Sound")]
        [Tooltip("The shot. Its own source, not shared with the footsteps: it needs a " +
                 "rolloff that carries across the level, and FootstepAudio rewrites the " +
                 "source's pitch on every step.")]
        [SerializeField] private AudioSource shotSource;

        [SerializeField] private AudioClip shot;

        private float _offAt;

        private void Awake()
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (flash != null)
            {
                flash.enabled = false;
            }
        }

        /// <summary>
        /// He fired. Called by the brain, on the machine that decided it; every peer
        /// blinks. Off the network there is only this machine, and it blinks alone.
        /// </summary>
        public void Fire()
        {
            var nob = NetworkObject;
            if (nob == null || !nob.IsSpawned)
            {
                Blink();
                return;
            }

            if (nob.IsServerInitialized)
            {
                ObserversFire();
            }
        }

        // RunLocally so the server takes the same path as everybody else and the blink
        // is produced in exactly one place.
        [ObserversRpc(RunLocally = true)]
        private void ObserversFire()
        {
            Blink();
        }

        private void Blink()
        {
            // The sound first, and on its own exit: it is the louder half of the two by
            // far. The flash lasts 0.06 s and a victim facing away never sees it at all,
            // so letting a missing Light take the shot's sound with it would silence the
            // only feedback most deaths ever get.
            // Unity objects: a destroyed one compares == null but is not a real null.
            if (shotSource != null && shot != null)
            {
                shotSource.PlayOneShot(shot);
            }

            if (flash == null)
            {
                return;
            }

            flash.enabled = true;
            _offAt = Time.time + flashTime;
        }

        // Update rather than a coroutine: the blink is two or three frames long, and a
        // coroutine per shot would allocate for something a comparison already does.
        private void Update()
        {
            if (flash == null || !flash.enabled)
            {
                return;
            }

            if (Time.time >= _offAt)
            {
                flash.enabled = false;
            }
        }
    }
}
