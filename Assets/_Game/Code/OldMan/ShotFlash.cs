using FishNet;
using FishNet.Object;
using UnityEngine;

namespace _Game.Code.OldMan
{
    /// <summary>
    /// Old Man firing, as seen, heard and felt: a light blinked for a few frames,
    /// the bang, the recoil kick handed to <see cref="RifleAim"/>, and — since
    /// 2026-08-06 — the <see cref="ShotPellet"/> grown from the muzzle, which is
    /// what carries the kill now. All of it rides this component's one RPC so
    /// firing travels once (MECHANICS.md 5.2 and 5.6). The sound is the loudest
    /// half of the show: the flash is over in 0.06 s and a victim facing away
    /// misses it entirely.
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

        [Tooltip("Kicked when the flash blinks, so the recoil rides the same RPC. " +
                 "Also where the pellet's aim is read from — see below.")]
        [SerializeField] private RifleAim rifleAim;

        [Tooltip("The shot itself since 2026-08-06: grown from the muzzle on every " +
                 "peer, armed only where the process decides deaths. Not optional — " +
                 "unwired, he fires blanks, and loudly says so.")]
        [SerializeField] private ShotPellet pelletPrefab;

        [Header("Sound")]
        [Tooltip("The shot. Its own source, not shared with the footsteps: it needs a " +
                 "rolloff that carries across the level, and FootstepAudio rewrites the " +
                 "source's pitch on every step.")]
        [SerializeField] private AudioSource shotSource;

        [SerializeField] private AudioClip shot;

        // Whether this process decides deaths: the server, or a process with no
        // networking at all. The same formula PlayerLife and LevelBootstrapper use —
        // asked of the process, because the pellet is armed at launch, before it can
        // know anything about spawn state.
        private static bool DecidesHere =>
            InstanceFinder.NetworkManager == null || !InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted;

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
            // The sound first, and on its own exit: it is the louder half of the show
            // by far. The flash lasts 0.06 s and a victim facing away never sees it at
            // all, so letting a missing Light take the shot's sound with it would
            // silence the only feedback most deaths ever get.
            // Unity objects: a destroyed one compares == null but is not a real null.
            if (shotSource != null && shot != null)
            {
                shotSource.PlayOneShot(shot);
            }

            // Before the kick: the pellet leaves along the aim, not the recoil throw.
            LaunchPellet();

            // Unity object: a destroyed one compares == null but is not a real null.
            if (rifleAim != null)
            {
                rifleAim.Kick();
            }

            if (flash == null)
            {
                return;
            }

            flash.enabled = true;
            _offAt = Time.time + flashTime;
        }

        /// <summary>
        /// The muzzle is the flash's own transform — it hangs on the barrel tip and
        /// travels with the raised rifle — and the aim is RifleAim's synced point,
        /// so every peer computes the same line without a byte added to the RPC.
        /// </summary>
        private void LaunchPellet()
        {
            // Unity objects: a destroyed one compares == null but is not a real null.
            if (pelletPrefab == null || rifleAim == null || flash == null)
            {
                // Loud on purpose: the kill left the brain on 2026-08-06 and rides
                // this pellet, so an unwired reference is not a missing visual — it
                // is Old Man firing blanks for the whole match.
                Debug.LogError($"{name}: pellet not wired, the shot can hit nobody (MECHANICS.md 5.2).");
                return;
            }

            var muzzle = flash.transform.position;
            var direction = rifleAim.AimPoint - muzzle;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.forward;
            }

            var pellet = Instantiate(pelletPrefab);
            var boot = LevelBootstrapper.Current;
            pellet.Launch(muzzle, direction, DecidesHere,
                boot == null ? null : boot.SensedPlayers);
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
