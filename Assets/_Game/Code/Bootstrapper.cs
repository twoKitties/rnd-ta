using System.Collections.Generic;
using _Game.Code.AI;
using _Game.Code.Level;
using _Game.Code.Noise;
using _Game.Code.OldMan;
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
    /// Every actor in play is spawned from a prefab here, so systems must take them
    /// from this component rather than looking them up by name or type
    /// (MECHANICS.md 7.6) — an actor that does not exist until Awake cannot be
    /// referenced by another prefab up front.
    /// </summary>
    public class Bootstrapper : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject playerPrefab;
        [SerializeField] private GameObject[] petPrefabs;
        [SerializeField] private GameObject oldManPrefab;

        [Header("Scene")]
        [SerializeField] private Transform spawnsRoot;

        [Tooltip("Old Man's round. Its direct children are the points, walked in " +
                 "hierarchy order — reorder them in the Hierarchy to reroute him.")]
        [SerializeField] private Transform patrolRoot;

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
        private readonly List<Transform> _patrolPoints = new List<Transform>();

        private ActorSpawner _spawner;

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
            // host is the only one that will run it once netcode lands, and it is the
            // resolved Seed — never zero — that it would send.
            _spawner = new ActorSpawner(seed);

            var avatars = Place(new[] { playerPrefab }, PointsOf(points, SpawnKind.Player));

            _pets.AddRange(Place(petPrefabs, PointsOf(points, SpawnKind.Pet)));

            var oldMen = Place(new[] { oldManPrefab }, PointsOf(points, SpawnKind.OldMan));
            OldMan = oldMen.Count > 0 ? oldMen[0] : null;

            CollectActors();

            // Before the players are added: the goal holds the live player list rather
            // than a copy of it, so it must be bound once and then simply sees whoever
            // joins later.
            if (levelGoal != null)
            {
                levelGoal.Bind(_sensedPlayers, _pets);
            }

            // Exactly one avatar exists today and it is ours. Tomorrow this loop runs
            // once per connection and only one of them passes true.
            for (var i = 0; i < avatars.Count; i++)
            {
                AddPlayer(avatars[i], i == 0);
            }

            BindBrains();
        }

        /// <summary>
        /// Take an avatar into the level: the AI starts sensing it, its noise starts
        /// being heard, and the outcome starts counting it. <paramref name="isLocal"/>
        /// says whether this is the avatar the person at this screen looks through —
        /// camera, ear, HUD and input belong to that one alone.
        ///
        /// Public because the roster is not fixed at Awake any more: a player can
        /// arrive after the level has started, and both brains hold the live list, so
        /// appending here is all it takes for them to see the newcomer.
        /// </summary>
        public void AddPlayer(GameObject avatar, bool isLocal)
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (avatar == null)
            {
                return;
            }

            _sensedPlayers.Add(new SensedPlayer(avatar));
            CollectNoiseSource(avatar);

            // Every avatar, not just the local one: a carrier shot inside the beam
            // still hands its animal over (MECHANICS.md 3.7), and PlayerLife needs the
            // goal to know that.
            var life = avatar.GetComponent<PlayerLife>();
            if (life != null && levelGoal != null)
            {
                life.Bind(levelGoal);
            }

            var local = avatar.GetComponent<LocalAvatar>();
            if (local != null)
            {
                local.Apply(isLocal);
            }

            if (!isLocal)
            {
                return;
            }

            Player = avatar;
            BindLocal(avatar);
        }

        /// <summary>
        /// Drop an avatar from the level — the player left. The animal they were
        /// carrying goes through <see cref="PlayerLife.Kill"/> rather than being
        /// dropped by the pet noticing a destroyed carrier, because leaving while
        /// standing in the beam still hands it over (MECHANICS.md 3.7); Kill is
        /// idempotent, so calling it on somebody already shot changes nothing.
        /// </summary>
        public void RemovePlayer(GameObject avatar)
        {
            if (avatar == null)
            {
                return;
            }

            var life = avatar.GetComponent<PlayerLife>();
            if (life != null)
            {
                life.Kill();
            }

            for (var i = _sensedPlayers.Count - 1; i >= 0; i--)
            {
                if (_sensedPlayers[i].Transform == avatar.transform)
                {
                    _sensedPlayers.RemoveAt(i);
                }
            }

            var emitter = avatar.GetComponent<NoiseEmitter>();
            if (emitter != null)
            {
                _noiseSources.Remove(emitter);
            }

            if (Player == avatar)
            {
                Player = null;
            }
        }

        // Applies a drawn layout: the indices are the decision, this is the state
        // change (MECHANICS.md 7.4). Kept here rather than in ActorSpawner so that the
        // host can one day send the indices and every peer run this half unchanged.
        private List<GameObject> Place(IReadOnlyList<GameObject> prefabs, IReadOnlyList<Transform> points)
        {
            var spawned = new List<GameObject>();
            if (prefabs == null || prefabs.Count == 0)
            {
                return spawned;
            }

            var drawn = _spawner.Draw(prefabs.Count, points == null ? 0 : points.Count);
            if (drawn.Length < prefabs.Count)
            {
                // Loud on purpose: an actor that silently never reaches the level
                // reads as a broken AI later and costs far more to find than this.
                Debug.LogError($"Bootstrapper: {prefabs.Count} prefab(s) to place but only " +
                               $"{(points == null ? 0 : points.Count)} spawn point(s). Some actors will be missing.");
            }

            for (var i = 0; i < drawn.Length; i++)
            {
                var prefab = prefabs[i];
                if (prefab == null)
                {
                    Debug.LogError($"Bootstrapper: prefab at index {i} is not assigned, skipped.");
                    continue;
                }

                var point = points[drawn[i]];
                var instance = Instantiate(prefab, point.position, point.rotation);
                instance.name = prefab.name;
                spawned.Add(instance);
            }

            return spawned;
        }

        private void CollectActors()
        {
            // Direct children, in hierarchy order: that order is the round, and the
            // markers carry nothing but a Transform, so there is no component to look
            // for. Any number of them is fine — the user adds yard points by dropping
            // them under this root.
            if (patrolRoot != null)
            {
                for (var i = 0; i < patrolRoot.childCount; i++)
                {
                    _patrolPoints.Add(patrolRoot.GetChild(i));
                }
            }

            for (var i = 0; i < _pets.Count; i++)
            {
                if (_pets[i] == null)
                {
                    continue;
                }

                CollectNoiseSource(_pets[i]);
            }
        }

        // Everything the AI needs to know about the rest of the level. Pushed in from
        // here rather than looked up, for the same reason as BindLocal: an actor is a
        // spawned prefab and cannot hold a reference to another spawned prefab.
        private void BindBrains()
        {
            // Every noise source is already in the list by now, which is what lets a
            // Dog hear a Parrot that was created after it.
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

            if (OldMan == null)
            {
                return;
            }

            var oldManBrain = OldMan.GetComponent<OldManBrain>();
            if (oldManBrain != null)
            {
                oldManBrain.Bind(_sensedPlayers, _patrolPoints, _noiseSources);
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
        // reference to them up front — the entry point hands it over instead. These
        // two are interaction and HUD, so they are the local avatar's alone.
        private void BindLocal(GameObject avatar)
        {
            if (levelGoal == null)
            {
                return;
            }

            var interactor = avatar.GetComponent<PlayerInteractor>();
            if (interactor != null)
            {
                interactor.Bind(levelGoal);
            }

            var status = avatar.GetComponentInChildren<LevelStatusUI>(true);
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
