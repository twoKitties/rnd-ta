using UnityEngine;

namespace _Game.Code.Level
{
    /// <summary>
    /// Keeps the saucer alive: a slow bob around the height it was placed at, and a
    /// slow spin about its own vertical axis.
    ///
    /// Purely cosmetic. It moves nothing the rules depend on — the beam zone stands
    /// on the ground and is measured horizontally (see BeamZone) — so there is
    /// nothing here for a netcode pass to synchronise: every peer can run its own
    /// Time.time and no one is worse off.
    ///
    /// A script rather than an animation clip: the project has no clips at all, and
    /// two sine waves are not worth an asset, an Animator and the "why is the saucer
    /// not where I put it" that comes with them.
    /// </summary>
    public class UfoDrift : MonoBehaviour
    {
        [Header("Bob")]
        [SerializeField] private float bobAmplitude = 0.15f;
        [SerializeField] private float bobPeriod = 8f;

        [Header("Spin")]
        [Tooltip("Degrees per second about the saucer's vertical axis. In Unity that " +
                 "is Y — the model is flat in Y (10.54 x 5.75 x 10.57).")]
        [SerializeField] private float spinDegreesPerSecond = 6f;

        private Vector3 _origin;

        private void Awake()
        {
            // Where the saucer was placed in the scene is the height it hovers around.
            _origin = transform.position;
        }

        private void Update()
        {
            if (bobPeriod > 0f)
            {
                var phase = Time.time * (Mathf.PI * 2f / bobPeriod);
                transform.position = _origin + Vector3.up * (Mathf.Sin(phase) * bobAmplitude);
            }

            transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
