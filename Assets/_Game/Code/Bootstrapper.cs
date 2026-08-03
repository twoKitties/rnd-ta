using System.Collections.Generic;
using _Game.Code.Spawning;
using UnityEngine;

namespace _Game.Code
{
    /// <summary>
    /// The scene's entry point. Every reference the level needs at startup is wired
    /// here, and everything that has to happen before gameplay starts happens here —
    /// today that means placing the actors on their spawn points.
    ///
    /// The pets and Old Man standing in the scene above the roof are scale
    /// references, not gameplay: systems must take actors from this component, not
    /// look them up by name or type.
    /// </summary>
    public class Bootstrapper : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject[] petPrefabs;
        [SerializeField] private GameObject oldManPrefab;

        [Header("Scene")]
        [SerializeField] private Transform spawnsRoot;

        [Header("Spawning")]
        [Tooltip("0 draws a fresh layout every run; any other value repeats the same one.")]
        [SerializeField] private int seed;

        /// <summary>The local player's avatar, or null if it could not be placed.</summary>
        public GameObject Player { get; private set; }

        /// <summary>The three pets, in the order their prefabs are listed.</summary>
        public IReadOnlyList<GameObject> Pets => _pets;

        public GameObject OldMan { get; private set; }

        private readonly List<GameObject> _pets = new List<GameObject>();

        private void Awake()
        {
            if (spawnsRoot == null)
            {
                Debug.LogError("Bootstrapper: spawnsRoot is not assigned, nothing can be spawned.");
                return;
            }

            var points = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);

            // One spawner for the whole scene, so a fixed seed produces one
            // reproducible layout across all three groups rather than three
            // independent ones. Today this runs locally; under MECHANICS.md 7.4 the
            // host is the only one that will run it once netcode lands.
            var spawner = new ActorSpawner(seed);

            var players = spawner.Spawn(new[] { playerPrefab }, PointsOf(points, SpawnKind.Player));
            Player = players.Count > 0 ? players[0] : null;

            _pets.AddRange(spawner.Spawn(petPrefabs, PointsOf(points, SpawnKind.Pet)));

            var oldMen = spawner.Spawn(new[] { oldManPrefab }, PointsOf(points, SpawnKind.OldMan));
            OldMan = oldMen.Count > 0 ? oldMen[0] : null;
        }

        private static List<Transform> PointsOf(IReadOnlyList<SpawnPoint> points, SpawnKind kind)
        {
            var of = new List<Transform>();
            foreach (var point in points)
            {
                if (point.Kind == kind)
                {
                    of.Add(point.transform);
                }
            }

            return of;
        }
    }
}
