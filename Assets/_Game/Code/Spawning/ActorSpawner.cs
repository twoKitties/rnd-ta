using System.Collections.Generic;
using UnityEngine;

namespace _Game.Code.Spawning
{
    /// <summary>
    /// Places prefabs on spawn points, one point per prefab, points drawn at random
    /// without repeats. A plain class, not a MonoBehaviour: it holds no scene
    /// references, and Bootstrapper — the scene's entry point — owns it.
    ///
    /// Under MECHANICS.md 7.4 spawning is shared state, so once netcode lands only
    /// the host runs this and replicates the result. The shape of the code does not
    /// change: one owner, one instance, one call.
    /// </summary>
    public class ActorSpawner
    {
        private readonly System.Random _random;

        /// <param name="seed">Zero draws a fresh layout every run; anything else
        /// repeats the same one, which is what makes a spawn bug reproducible.</param>
        public ActorSpawner(int seed)
        {
            // System.Random, not UnityEngine.Random: the latter is one global
            // sequence for the whole process, and MECHANICS.md 7.3 keeps state off
            // statics. Tomorrow this instance lives on the host.
            _random = seed == 0 ? new System.Random() : new System.Random(seed);
        }

        /// <summary>
        /// Instantiates every prefab on a point of its own. Returns what was spawned,
        /// in the order the prefabs came in.
        /// </summary>
        public List<GameObject> Spawn(IReadOnlyList<GameObject> prefabs, IReadOnlyList<Transform> points)
        {
            var spawned = new List<GameObject>();
            if (prefabs == null || prefabs.Count == 0)
            {
                return spawned;
            }

            if (points == null || points.Count < prefabs.Count)
            {
                // Loud on purpose: an actor that silently never reaches the level
                // reads as a broken AI later and costs far more to find than this.
                Debug.LogError($"ActorSpawner: {prefabs.Count} prefab(s) to place but only " +
                               $"{(points == null ? 0 : points.Count)} spawn point(s). Some actors will be missing.");
            }

            // Fisher-Yates over the point indices, stopped once enough are drawn.
            var order = new int[points == null ? 0 : points.Count];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            var take = Mathf.Min(prefabs.Count, order.Length);
            for (var i = 0; i < take; i++)
            {
                var j = _random.Next(i, order.Length);
                (order[i], order[j]) = (order[j], order[i]);

                var prefab = prefabs[i];
                if (prefab == null)
                {
                    Debug.LogError($"ActorSpawner: prefab at index {i} is not assigned, skipped.");
                    continue;
                }

                var point = points[order[i]];
                var instance = Object.Instantiate(prefab, point.position, point.rotation);
                instance.name = prefab.name;
                spawned.Add(instance);
            }

            return spawned;
        }
    }
}
