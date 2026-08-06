using System;
using UnityEngine;

namespace _Game.Code.Audio
{
    /// <summary>
    /// The clips, per surface, in one asset. Authored data rather than fields on a
    /// component, because there are two users of exactly the same 19 clips — the
    /// player's avatar and Old Man — and a copy on each prefab is a copy that drifts.
    ///
    /// Adding a surface, or giving Old Man boots of his own, is a second asset and no
    /// code at all.
    /// </summary>
    [CreateAssetMenu(fileName = "Footsteps-Bank", menuName = "rnd-ta/Footstep Bank")]
    public class FootstepBank : ScriptableObject
    {
        [Serializable]
        private struct Set
        {
            public SurfaceKind Kind;
            public AudioClip[] Clips;
        }

        [SerializeField] private Set[] sets;

        /// <summary>
        /// A clip for this surface, never the same one twice running — one clip at two
        /// steps a second is heard as a metronome within a few paces, which is worse
        /// than silence. <paramref name="last"/> is the caller's memory of what it
        /// played, so two actors on the same bank do not share a sequence.
        ///
        /// Null when the surface has no clips, and the caller stays silent — an
        /// unassigned bank is silence, the same rule PetVoice follows.
        /// </summary>
        public AudioClip Pick(SurfaceKind kind, ref int last)
        {
            if (sets == null)
            {
                return null;
            }

            for (var i = 0; i < sets.Length; i++)
            {
                if (sets[i].Kind != kind)
                {
                    continue;
                }

                var clips = sets[i].Clips;
                if (clips == null || clips.Length == 0)
                {
                    return null;
                }

                if (clips.Length == 1)
                {
                    last = 0;
                    return clips[0];
                }

                // Random, then nudged off the previous one. UnityEngine.Random is
                // deliberate here: MECHANICS.md 7.7 asks for a reproducible, shared
                // sequence for the spawn layout, and this is the opposite — every
                // machine may and should pick its own.
                var index = UnityEngine.Random.Range(0, clips.Length);
                if (index == last)
                {
                    index = (index + 1) % clips.Length;
                }

                last = index;
                return clips[index];
            }

            return null;
        }
    }
}
