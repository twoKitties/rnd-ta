using System.Collections.Generic;
using _Game.Code.AI;
using _Game.Code.Level;
using _Game.Code.Noise;
using _Game.Code.Pets;
using _Game.Code.Player;
using _Game.Code.Spawning;
using _Game.Code.UI;
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
        [SerializeField] private LevelGoal levelGoal;

        [Header("Spawning")]
        [Tooltip("0 draws a fresh layout every run; any other value repeats the same one.")]
        [SerializeField] private int seed;

        /// <summary>The local player's avatar, or null if it could not be placed.</summary>
        public GameObject Player { get; private set; }

        /// <summary>The three pets, in the order their prefabs are listed.</summary>
        public IReadOnlyList<GameObject> Pets => _pets;

        public GameObject OldMan { get; private set; }

        /// <summary>
        /// The players as the AI sees them. Built once here so that no brain does
        /// GetComponent in Update and none of them searches the scene (7.6).
        /// </summary>
        public IReadOnlyList<SensedPlayer> SensedPlayers => _sensedPlayers;

        /// <summary>
        /// Everything that can make a noise a listener might react to: the players and
        /// the animals. Old Man is not here — he has no emitter, so he cannot hear
        /// himself open a door.
        /// </summary>
        public IReadOnlyList<NoiseEmitter> NoiseSources => _noiseSources;

        private readonly List<GameObject> _pets = new List<GameObject>();
        private readonly List<SensedPlayer> _sensedPlayers = new List<SensedPlayer>();
        private readonly List<NoiseEmitter> _noiseSources = new List<NoiseEmitter>();

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

            BindGoal(players);
            BindBrains(players);
        }

        // Everything the AI needs to know about the rest of the level. Pushed in from
        // here rather than looked up, for the same reason as BindGoal: an actor is a
        // spawned prefab and cannot hold a reference to another spawned prefab.
        private void BindBrains(IReadOnlyList<GameObject> players)
        {
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i] == null)
                {
                    continue;
                }

                _sensedPlayers.Add(new SensedPlayer(players[i]));
                CollectNoiseSource(players[i]);
            }

            for (var i = 0; i < _pets.Count; i++)
            {
                if (_pets[i] == null)
                {
                    continue;
                }

                CollectNoiseSource(_pets[i]);
            }

            // A second pass over the animals, after every source is in the list: a Dog
            // must be able to hear a Parrot that was created after it.
            for (var i = 0; i < _pets.Count; i++)
            {
                if (_pets[i] == null)
                {
                    continue;
                }

                var brain = _pets[i].GetComponent<PetBrain>();
                if (brain != null)
                {
                    brain.Bind(_sensedPlayers, _noiseSources);
                }
            }
        }

        private void CollectNoiseSource(GameObject actor)
        {
            var emitter = actor.GetComponent<NoiseEmitter>();
            if (emitter != null)
            {
                _noiseSources.Add(emitter);
            }
        }

        // The goal and the beam are scene objects, so a spawned avatar cannot hold a
        // reference to them up front — the entry point hands it over instead.
        private void BindGoal(IReadOnlyList<GameObject> players)
        {
            if (levelGoal == null)
            {
                return;
            }

            levelGoal.Bind(players, _pets);

            if (Player == null)
            {
                return;
            }

            var interactor = Player.GetComponent<PlayerInteractor>();
            if (interactor != null)
            {
                interactor.Bind(levelGoal);
            }

            var status = Player.GetComponentInChildren<LevelStatusUI>(true);
            if (status != null)
            {
                status.Bind(levelGoal);
            }
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
