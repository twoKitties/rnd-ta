using UnityEngine;

namespace _Game.Code.Level
{
    /// <summary>
    /// The saucer's beam, standing on the players' spawn point. Releasing an animal
    /// inside it hands the animal over, and the match is won with every player
    /// standing in it (MECHANICS.md 4.5 and section 6). The radius comes from
    /// section 2 and must match the visible column: what the player sees is what
    /// counts.
    ///
    /// A radius rather than a trigger collider on purpose. "Inside the beam" has to
    /// give the same answer wherever it is asked, and OnTriggerEnter depends on
    /// physics ordering and on who owns the collider — a bad basis for a rule the
    /// host will have to arbitrate once netcode lands.
    /// </summary>
    public class BeamZone : MonoBehaviour
    {
        [Header("MECHANICS.md section 2")]
        [SerializeField] private float radius = 1f;

        public float Radius => radius;

        /// <summary>
        /// Is that point inside the beam. Measured horizontally: height is not part
        /// of the rule, and a player standing on the porch step is still in the beam.
        /// </summary>
        public bool Contains(Vector3 worldPosition)
        {
            return Contains(worldPosition, 0f);
        }

        /// <summary>
        /// The same test with an allowance on the radius. Zero is the rule itself and
        /// is what the player is shown; anything above it is the authority judging a
        /// position it only knows late — a client-authoritative avatar reaches the
        /// server interpolated and behind, and the beam's edge is exactly where the
        /// players stand.
        /// </summary>
        public bool Contains(Vector3 worldPosition, float extraRadius)
        {
            var here = transform.position;
            var dx = worldPosition.x - here.x;
            var dz = worldPosition.z - here.z;
            var reach = radius + extraRadius;
            return dx * dx + dz * dz <= reach * reach;
        }

        private void OnDrawGizmos()
        {
            // Drawn so the zone can be seen in the editor instead of guessed at.
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            var center = transform.position;
            var previous = center + new Vector3(radius, 0f, 0f);
            for (var i = 1; i <= 48; i++)
            {
                var angle = i / 48f * Mathf.PI * 2f;
                var next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
