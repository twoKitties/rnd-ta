using _Game.Code.Doors;
using _Game.Code.Level;
using _Game.Code.Pets;
using UnityEngine;

namespace _Game.Code.Player
{
    public enum InteractionKind
    {
        None,
        Pet,
        Door
    }

    /// <summary>
    /// What Interact would do right now. The verb and the subject are semantic —
    /// the name of the button belongs to the UI, not to the simulation
    /// (MECHANICS.md 7.1).
    /// </summary>
    public readonly struct InteractionTarget
    {
        public readonly InteractionKind Kind;
        public readonly string Verb;
        public readonly string Subject;

        public InteractionTarget(InteractionKind kind, string verb, string subject)
        {
            Kind = kind;
            Verb = verb;
            Subject = subject;
        }

        public bool Exists => Kind != InteractionKind.None;
    }

    /// <summary>
    /// Works out what is available in front of the player each frame, and acts on it
    /// when Interact is pressed: an animal to pick up or put down, or a door.
    /// Distances come from MECHANICS.md section 2.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private Transform lookSource;
        [SerializeField] private PlayerHands hands;

        // World metres, and they stay world metres even though the prefab root is
        // scaled 0.1: the physics queries work in world space and lookSource hands
        // out a world position and direction. See the local-vs-world table in
        // MECHANICS.md section 2.
        [Header("Distances, m (MECHANICS.md section 2)")]
        [SerializeField] private float reach = 1.5f;

        // Search wide, let the animal authorise narrow: the real capture distance
        // lives on Pet, which is what the host will re-check. Keep this at or above
        // it, or animals inside their own capture distance never get found.
        [SerializeField] private float petSearchRadius = 2f;

        [Header("Masks")]
        // Door + BlockedArea. BlockedArea is in the mask so that a door cannot be
        // used through a wall — the ray stops on the frame first. The same mask
        // stops an animal from being grabbed through a wall.
        [SerializeField] private LayerMask blockers;

        // Just the Pet layer. Measured 2026-08-03: an unmasked query returns up to 57
        // colliders in a furnished room, and this one runs every frame to feed the
        // prompt. Masked it can only ever return the three animals.
        [SerializeField] private LayerMask petMask;

        /// <summary>What Interact would do this frame. Read by the HUD.</summary>
        public InteractionTarget Current { get; private set; }

        private PlayerController _controller;
        private Pet _pet;
        private Door _door;
        private LevelGoal _goal;
        private readonly Collider[] _nearbyPets = new Collider[8];

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
        }

        /// <summary>
        /// Handed over by LevelBootstrapper right after this avatar is spawned: the goal
        /// is a scene object, so a spawned prefab cannot reference it up front.
        /// </summary>
        public void Bind(LevelGoal goal)
        {
            _goal = goal;
        }

        // Switched off on death (PlayerLife). Resolve stops running, so the last
        // target would stay on offer and the HUD would keep showing "[E] Grab Kitty"
        // over a corpse — the exact "the button does nothing" reading the prompt was
        // added to prevent. Nothing can actually be interacted with anyway: the
        // press is read through PlayerController, whose actions are disabled too.
        private void OnDisable()
        {
            Current = default;
        }

        private void Update()
        {
            Resolve();

            if (!_controller.Intent.Interact)
            {
                return;
            }

            switch (Current.Kind)
            {
                case InteractionKind.Pet:
                    // Hands full means this is the animal being carried, and the press
                    // puts it down; otherwise it is the one in front of the player.
                    if (hands.IsEmpty)
                    {
                        _pet.TryTake(hands);
                    }
                    else if (_goal != null)
                    {
                        // Through the goal, because putting an animal down inside the
                        // beam is how it is handed over (MECHANICS.md 4.5).
                        _goal.ReleaseCarried(hands);
                    }
                    else
                    {
                        hands.Carried.Release();
                    }

                    break;

                case InteractionKind.Door:
                    _door.Use(transform);
                    break;
            }
        }

        private void Resolve()
        {
            _pet = null;
            _door = null;

            if (hands == null || lookSource == null)
            {
                Current = default;
                return;
            }

            // Carrying: the only thing on offer is putting the animal down. Doors are
            // not available with full hands — MECHANICS.md 3.4.
            if (!hands.IsEmpty)
            {
                Current = new InteractionTarget(InteractionKind.Pet, "Release", hands.Carried.name);
                return;
            }

            // An animal beats a door on purpose: the animal is running away, the door
            // will wait.
            _pet = FindPet();
            if (_pet != null)
            {
                Current = new InteractionTarget(InteractionKind.Pet, "Grab", _pet.name);
                return;
            }

            _door = FindDoor();
            if (_door != null)
            {
                Current = new InteractionTarget(InteractionKind.Door, _door.IsOpen ? "Close" : "Open", "door");
                return;
            }

            Current = default;
        }

        private Door FindDoor()
        {
            if (!Physics.Raycast(lookSource.position, lookSource.forward, out var hit, reach, blockers,
                    QueryTriggerInteraction.Ignore))
            {
                return null;
            }

            return hit.collider.GetComponent<Door>();
        }

        /// <summary>
        /// Nearest animal within reach that is in front of the player and not behind a
        /// wall. By radius rather than by a ray: MECHANICS.md sizes the capture as a
        /// distance, and a fleeing Parrot is 17 cm of target.
        /// </summary>
        private Pet FindPet()
        {
            var count = Physics.OverlapSphereNonAlloc(transform.position, petSearchRadius, _nearbyPets,
                petMask, QueryTriggerInteraction.Ignore);

            Pet best = null;
            var bestDistance = float.MaxValue;
            var eye = lookSource.position;

            for (var i = 0; i < count; i++)
            {
                var pet = _nearbyPets[i].GetComponentInParent<Pet>();
                if (pet == null || !pet.CanBeTakenBy(hands))
                {
                    continue;
                }

                var toPet = pet.transform.position - transform.position;
                if (Vector3.Dot(toPet, lookSource.forward) <= 0f)
                {
                    continue;
                }

                var distance = toPet.sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                // Aim at the middle of the body. Aiming near the feet made the line
                // graze the furniture the animal stands next to — a Kitty by the bed
                // was unreachable because a blanket clipped the line.
                if (Physics.Linecast(eye, pet.AimPoint, blockers, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                best = pet;
                bestDistance = distance;
            }

            return best;
        }
    }
}
