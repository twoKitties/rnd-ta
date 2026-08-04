using _Game.Code.Doors;
using UnityEngine;
using UnityEngine.AI;

namespace _Game.Code.AI
{
    /// <summary>
    /// Whether a path is walled off by a shut door. The navmesh cannot answer this:
    /// the eight leaves sit on the `Door` layer, which is outside the bake mask, so
    /// the mesh runs straight through every doorway and a NavMeshAgent walks through
    /// a closed door as if it were not there (MECHANICS.md 4.6). Carving the doorway
    /// with a NavMeshObstacle was rejected there — carving is global and would also
    /// stop Old Man from ever reaching a door to open it.
    ///
    /// So the rule lives in AI code, and it is the same rule for both sides: the
    /// animals treat what this returns as a wall, Old Man treats it as a door to
    /// open (5.5).
    ///
    /// Static and stateless; the RaycastHit buffer belongs to the calling brain, the
    /// way PlayerInteractor owns its own Collider buffer.
    /// </summary>
    public static class DoorGate
    {
        /// <summary>
        /// The first shut door standing across <paramref name="path"/>, or null if the
        /// way is clear.
        /// </summary>
        /// <param name="probeHeight">Height above the floor to sweep at, world metres.</param>
        /// <param name="doorMask">Just the Door layer.</param>
        /// <param name="buffer">Caller-owned scratch space; its length caps how many
        /// leaves one path segment may be tested against.</param>
        public static Door FirstClosedDoor(NavMeshPath path, float probeHeight, LayerMask doorMask,
            RaycastHit[] buffer)
        {
            if (path == null || buffer == null)
            {
                return null;
            }

            var corners = path.corners;
            for (var i = 1; i < corners.Length; i++)
            {
                var door = FirstClosedDoorBetween(corners[i - 1] + Vector3.up * probeHeight,
                    corners[i] + Vector3.up * probeHeight, doorMask, buffer);
                if (door != null)
                {
                    return door;
                }
            }

            return null;
        }

        /// <summary>
        /// The same question about one straight segment, both ends given in world
        /// space and already lifted to the probe height. Split out for the animals'
        /// per-frame check on the leg they are currently walking, which has a corner
        /// to aim at but no NavMeshPath to hand.
        /// </summary>
        public static Door FirstClosedDoorBetween(Vector3 from, Vector3 to, LayerMask doorMask,
            RaycastHit[] buffer)
        {
            if (buffer == null)
            {
                return null;
            }

            var travel = to - from;
            var distance = travel.magnitude;
            if (distance < 0.0001f)
            {
                return null;
            }

            // Every hit on the segment, not just the first: a path can pass an open
            // leaf standing in the room and then meet a shut one further along, and
            // a single Linecast would stop at the open one and report the way clear.
            var count = Physics.RaycastNonAlloc(from, travel / distance, buffer, distance, doorMask,
                QueryTriggerInteraction.Ignore);

            for (var h = 0; h < count; h++)
            {
                var door = buffer[h].collider.GetComponentInParent<Door>();
                if (door != null && !door.IsOpen)
                {
                    return door;
                }
            }

            return null;
        }
    }
}
