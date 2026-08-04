using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
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
    /// The level's entry point. Every reference the level needs at startup is wired
    /// here, and everything that has to happen before gameplay starts happens here —
    /// today that means placing the actors on their spawn points.
    ///
    /// Every actor in play is spawned from a prefab here, so systems must take them
    /// from this component rather than looking them up by name or type
    /// (MECHANICS.md 7.6) — an actor that does not exist until Awake cannot be
    /// referenced by another prefab up front.
    ///
    /// Named for the level on purpose: the application's own entry point is
    /// <see cref="App.LevelBootstrapper"/> in the Loading scene, and it is a different
    /// thing with a different lifetime — this one dies with the raid.
    /// </summary>
    public class LevelBootstrapper : MonoBehaviour
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

        [Tooltip("Carries the raid's counters and outcome to every client. Spawned by " +
                 "the server only; with no networking the goal keeps them itself.")]
        [SerializeField] private GameObject raidStatePrefab;

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

        // Gathered in Awake, used when the level is actually laid out — which on a
        // networked raid is several frames later.
        private SpawnPoint[] _points;

        private bool _laidOut;

        /// <summary>
        /// The raid's entry point, for the avatars that arrive replicated and have to
        /// find it. Static for the same reason RaidSession.Active is, and it holds no
        /// per-player state — the players are in the lists below (MECHANICS.md 7.3).
        /// </summary>
        public static LevelBootstrapper Current { get; private set; }

        // Who lays the level out. Only the server does; a client receives every actor
        // as a replicated object and must not create a second set of its own. With no
        // networking at all — pressing Play straight into this scene — we are our own
        // server and everything happens locally, which is what keeps the game playable
        // in one process.
        private static bool IsAuthority =>
            InstanceFinder.NetworkManager == null || !InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted;

        private static bool IsNetworked => InstanceFinder.NetworkManager != null && InstanceFinder.IsServerStarted;

        private void Awake()
        {
            Current = this;

            if (spawnsRoot == null)
            {
                Debug.LogError("LevelBootstrapper: spawnsRoot is not assigned, nothing can be spawned.");
                return;
            }

            var points = spawnsRoot.GetComponentsInChildren<SpawnPoint>(true);

            // One spawner for the whole scene, so a fixed seed produces one
            // reproducible layout across all three groups rather than three
            // independent ones. Today this runs locally; under MECHANICS.md 7.4 the
            // host is the only one that will run it once netcode lands, and it is the
            // resolved Seed — never zero — that it would send.
            _spawner = new ActorSpawner(seed);

            _points = points;

            // Off the network there is nobody to wait for, and on a client nothing is
            // created here anyway — both lay out immediately and behave exactly as
            // before.
            if (!IsNetworked)
            {
                LayOut();
                return;
            }

            // On the server, waiting is the whole point. FishNet activates this scene —
            // which is what runs this Awake — and only *then* broadcasts "load Level"
            // to the clients (Scened/SceneManager.cs, the LoadScenesBroadcast after the
            // load loop). Spawning here would send every actor to peers that have no
            // scene to put them in: measured 2026-08-05, the client's animals landed in
            // the Lobby with no navmesh, its avatar fell through a floor that did not
            // exist yet, and — worst — when Level finally arrived FishNet disabled every
            // scene NetworkObject in it to await spawn messages the server had already
            // sent. The house is one of those objects, so it stayed switched off for
            // good, taking the floor with it.
            InstanceFinder.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChangeEnd;
            InstanceFinder.ServerManager.OnRemoteConnectionState += OnConnectionStateForLayout;
            LayOutWhenEveryoneHasTheScene();
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (Current == this)
            {
                Current = null;
            }
        }

        private void Unsubscribe()
        {
            if (InstanceFinder.NetworkManager == null)
            {
                return;
            }

            InstanceFinder.SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChangeEnd;
            InstanceFinder.ServerManager.OnRemoteConnectionState -= OnConnectionStateForLayout;
        }

        // A connection finished loading this scene. Asked again rather than counted,
        // because the answer is "all of them", not "one more".
        private void OnClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
        {
            LayOutWhenEveryoneHasTheScene();
        }

        // Somebody dropped while the others were still loading. Without this the raid
        // would wait for a connection that no longer exists, for ever.
        private void OnConnectionStateForLayout(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Started)
            {
                LayOutWhenEveryoneHasTheScene();
            }
        }

        private void LayOutWhenEveryoneHasTheScene()
        {
            if (_laidOut)
            {
                return;
            }

            var scene = gameObject.scene;
            foreach (var connection in InstanceFinder.ServerManager.Clients.Values)
            {
                if (!connection.Scenes.Contains(scene))
                {
                    return;
                }
            }

            Unsubscribe();
            LayOut();
        }

        /// <summary>
        /// Build the raid: the actors, the registry, the replicated state and the
        /// bindings, in that order. Split out of Awake so that the moment it happens is
        /// a decision rather than a side effect of the scene activating.
        /// </summary>
        private void LayOut()
        {
            _laidOut = true;

            if (IsAuthority)
            {
                SpawnPlayers(PointsOf(_points, SpawnKind.Player));

                _pets.AddRange(Place(petPrefabs, PointsOf(_points, SpawnKind.Pet)));

                var oldMen = Place(new[] { oldManPrefab }, PointsOf(_points, SpawnKind.OldMan));
                OldMan = oldMen.Count > 0 ? oldMen[0] : null;
            }

            CollectActors();

            // Before the goal is bound: binding sets the animal count, and that number
            // has to land in the replicated state rather than in a local field.
            SpawnRaidState();

            // Before the players are added: the goal holds the live player list rather
            // than a copy of it, so it must be bound once and then simply sees whoever
            // joins later.
            if (levelGoal != null)
            {
                levelGoal.Bind(_sensedPlayers, _pets);
            }

            BindBrains();
        }

        // Only the server makes it, and only when there is a network to carry it. Off
        // the network LevelGoal keeps the same four numbers in its own fields, which is
        // what keeps this level playable on its own.
        private void SpawnRaidState()
        {
            if (!IsNetworked || raidStatePrefab == null)
            {
                return;
            }

            var state = Instantiate(raidStatePrefab);
            state.name = raidStatePrefab.name;
            InstanceFinder.ServerManager.Spawn(state);
        }

        /// <summary>
        /// One avatar per connection, each owned by the connection it was made for —
        /// ownership is what later tells that machine which of the four is theirs.
        ///
        /// Off the network there are no connections, so we make the one avatar this
        /// process is going to look through and claim it outright; that is the case
        /// that keeps the level playable on its own.
        /// </summary>
        private void SpawnPlayers(IReadOnlyList<Transform> playerPoints)
        {
            if (!IsNetworked)
            {
                var solo = Place(new[] { playerPrefab }, playerPoints);
                if (solo.Count == 0)
                {
                    return;
                }

                var avatar = solo[0];
                var local = avatar.GetComponent<LocalAvatar>();
                if (local != null)
                {
                    local.Apply(true);
                }

                AddPlayer(avatar);
                return;
            }

            var connections = InstanceFinder.ServerManager.Clients;
            var drawn = _spawner.Draw(connections.Count, playerPoints.Count);
            var i = 0;

            foreach (var connection in connections.Values)
            {
                if (i >= drawn.Length)
                {
                    // Four spawn points, and the lobby already refuses a fifth player.
                    // Loud rather than silent: an avatar that never appears reads as a
                    // networking bug and costs far more to find than this line.
                    Debug.LogError($"LevelBootstrapper: no spawn point left for connection {connection.ClientId}.");
                    break;
                }

                var point = playerPoints[drawn[i]];
                var avatar = Instantiate(playerPrefab, point.position, point.rotation);
                avatar.name = playerPrefab.name;

                // Registration happens in LocalAvatar.OnStartClient, on every peer,
                // including this one — spawning is what triggers it.
                InstanceFinder.ServerManager.Spawn(avatar, connection);
                i++;
            }
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
        public void AddPlayer(GameObject avatar)
        {
            // Unity object: a destroyed one compares == null but is not a real null.
            if (avatar == null)
            {
                return;
            }

            // Called once per peer per avatar, and a client sees its own avatar arrive
            // the same way it sees everybody else's — so the same avatar must not be
            // able to enter the roster twice.
            for (var existing = 0; existing < _sensedPlayers.Count; existing++)
            {
                if (_sensedPlayers[existing].Transform == avatar.transform)
                {
                    return;
                }
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

            // Whether this one is ours was decided by ownership, in LocalAvatar itself.
            // Asked rather than told, because the entry point does not create three of
            // the four avatars and has no way of knowing.
            var local = avatar.GetComponent<LocalAvatar>();
            if (local == null || !local.IsLocal)
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
                Debug.LogError($"LevelBootstrapper: {prefabs.Count} prefab(s) to place but only " +
                               $"{(points == null ? 0 : points.Count)} spawn point(s). Some actors will be missing.");
            }

            for (var i = 0; i < drawn.Length; i++)
            {
                var prefab = prefabs[i];
                if (prefab == null)
                {
                    Debug.LogError($"LevelBootstrapper: prefab at index {i} is not assigned, skipped.");
                    continue;
                }

                var point = points[drawn[i]];
                var instance = Instantiate(prefab, point.position, point.rotation);
                instance.name = prefab.name;

                // Owned by nobody: the animals and Old Man belong to the rules, not to
                // a player. Spawning is still the server's, so every peer gets the same
                // three animals rather than three of its own.
                if (IsNetworked && instance.GetComponent<NetworkObject>() != null)
                {
                    InstanceFinder.ServerManager.Spawn(instance);
                }

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
