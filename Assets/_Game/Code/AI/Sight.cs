using UnityEngine;

namespace _Game.Code.AI
{
    /// <summary>
    /// The one line-of-sight rule in the game, shared by Old Man (MECHANICS.md 5.2)
    /// and by the animals (their own sight range and cone, section 2). Both ask the
    /// same three questions in the same order — range, cone, occlusion — so that
    /// "he could not possibly have seen me" means the same thing to both.
    ///
    /// Static because it holds nothing: no per-actor state lives here, which is what
    /// MECHANICS.md 7.3 forbids. Every input is passed in by the caller.
    /// </summary>
    public static class Sight
    {
        /// <summary>
        /// Can an eye at <paramref name="eye"/> looking along <paramref name="forward"/>
        /// see <paramref name="target"/>?
        /// </summary>
        /// <param name="coneAngle">Full cone, degrees — the caller's tunable is the full angle.</param>
        /// <param name="blockers">What breaks the line: BlockedArea + Door.</param>
        public static bool CanSee(Vector3 eye, Vector3 forward, Vector3 target, float range, float coneAngle,
            LayerMask blockers)
        {
            var toTarget = target - eye;
            var distance = toTarget.magnitude;
            if (distance > range)
            {
                return false;
            }

            // Standing on top of the target: nothing sensible to measure an angle
            // against, and it is plainly seen.
            if (distance < 0.0001f)
            {
                return true;
            }

            // The cone is measured on the horizontal plane only. The alien is 0.5 m
            // tall and Old Man's eyes are at 1.6 m, so a true 3D cone would lose the
            // player at arm's length purely from looking level — the height difference
            // alone is 50° at two metres. Occlusion below is what handles "under the
            // table"; the cone is about which way the actor faces.
            var flatForward = new Vector3(forward.x, 0f, forward.z);
            var flatToTarget = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flatForward.sqrMagnitude > 0.0001f && flatToTarget.sqrMagnitude > 0.0001f &&
                Vector3.Angle(flatForward, flatToTarget) > coneAngle * 0.5f)
            {
                return false;
            }

            // Linecast rather than a sphere or capsule cast, for the reason recorded in
            // Pet.ClampedCarryPosition: a swept shape that already overlaps something at
            // the start reports no hit at all, and an eye inside a head is exactly that
            // case.
            return !Physics.Linecast(eye, target, blockers, QueryTriggerInteraction.Ignore);
        }
    }
}
