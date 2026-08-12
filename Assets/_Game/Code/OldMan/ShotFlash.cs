using FishNet;
using FishNet.Object;
using UnityEngine;

namespace _Game.Code.OldMan
{
    /// <summary>
    /// Old Man firing, as seen, heard and felt: a light blinked for a few frames,
    /// the bang, the recoil kick handed to <see cref="RifleRig"/>, and — since
    /// 2026-08-06 — the <see cref="ShotPellet"/> grown from the muzzle, which is
    /// what carries the kill now. All of it rides this component's one RPC — which
    /// since 2026-08-12 carries the aim point too — so firing travels once
    /// (MECHANICS.md 5.2 and 5.6). The sound is the loudest
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
        [Tooltip("Blinked when he fires, and read as the muzzle the pellet leaves from. " +
                 "Not optional: without it he fires blanks.")]
        [SerializeField] private Light flash;

        [Tooltip("How long the flash stays lit, s (MECHANICS.md section 2).")]
        [SerializeField] private float flashTime = 0.06f;

        [Tooltip("Kicked when the flash blinks, and where the aim point is read before " +
                 "it is sent with the shot.")]
        [SerializeField] private RifleRig rifleAim;

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
            // Sent with the shot rather than read at the far end: a client's RifleRig
            // has it from a SyncVar going at its own rate, and the gap is a pellet that
            // visibly misses and kills anyway.
            // Unity object: destroyed compares == null but is not null.
            var aimPoint = rifleAim != null ? rifleAim.AimPoint : transform.position + transform.forward;

            var nob = NetworkObject;
            if (nob == null || !nob.IsSpawned)
            {
                Blink(aimPoint);
                return;
            }

            if (nob.IsServerInitialized)
            {
                ObserversFire(aimPoint);
            }
        }

        // RunLocally so the server takes the same path as everybody else and the blink
        // is produced in exactly one place.
        [ObserversRpc(RunLocally = true)]
        private void ObserversFire(Vector3 aimPoint)
        {
            Blink(aimPoint);
        }

        private void Blink(Vector3 aimPoint)
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
            LaunchPellet(aimPoint);

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
        /// Muzzle is the flash's own transform (it hangs on the barrel tip); the aim
        /// arrives with the shot, so every peer flies the same line.
        /// </summary>
        private void LaunchPellet(Vector3 aimPoint)
        {
            // Unity objects: a destroyed one compares == null but is not a real null.
            if (pelletPrefab == null || flash == null)
            {
                // Loud on purpose: the kill left the brain on 2026-08-06 and rides
                // this pellet, so an unwired reference is not a missing visual — it
                // is Old Man firing blanks for the whole match.
                Debug.LogError($"{name}: pellet not wired, the shot can hit nobody (MECHANICS.md 5.2).");
                return;
            }

            var muzzle = flash.transform.position;
            var direction = aimPoint - muzzle;
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
