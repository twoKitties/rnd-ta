using System.Collections.Generic;
using _Game.Code.Noise;
using UnityEngine;

namespace _Game.Code.AI
{
    /// <summary>
    /// The listener's half of the noise rule (MECHANICS.md 5.3 and 4.3): walk the
    /// emitters, keep the ones this listener can hear, answer with the loudest.
    /// Listeners poll emitters and never the other way round — MECHANICS.md 7.5.
    ///
    /// Static and stateless: the source list belongs to whoever is listening.
    /// </summary>
    public static class Hearing
    {
        /// <summary>
        /// The loudest source above <paramref name="threshold"/> whose own hearing
        /// radius reaches <paramref name="listener"/>, or null.
        /// </summary>
        /// <param name="self">The listener's own emitter, skipped. May be null.</param>
        public static NoiseEmitter Loudest(IReadOnlyList<NoiseEmitter> sources, Vector3 listener, float threshold,
            NoiseEmitter self)
        {
            if (sources == null)
            {
                return null;
            }

            NoiseEmitter best = null;
            var bestLevel = threshold;

            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];

                // Unity object: a destroyed one compares == null but is not a real null,
                // so `?.` and `??` would lie about it. A delivered animal is switched
                // off rather than destroyed, and must fall out here too.
                if (source == null || !source.isActiveAndEnabled || source == self)
                {
                    continue;
                }

                if (source.Level < bestLevel)
                {
                    continue;
                }

                // The radius is the source's, not the listener's: a sprint carries 24 m
                // and a step 10 m, and that is the whole point of deriving the radius
                // from the level.
                if (Vector3.Distance(source.transform.position, listener) > source.HearingRadius)
                {
                    continue;
                }

                best = source;
                bestLevel = source.Level;
            }

            return best;
        }
    }
}
