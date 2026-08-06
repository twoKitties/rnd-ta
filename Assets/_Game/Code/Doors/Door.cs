using System.Collections.Generic;
using FishNet;
using _Game.Code.Noise;
using UnityEngine;

namespace _Game.Code.Doors
{
    /// <summary>
    /// A swinging door leaf. Angles and speed come from the "Двери" tunables table
    /// in MECHANICS.md section 2 — change them there first, one at a time.
    ///
    /// Goes on the leaf itself (`Interior_Door` / `Exterior_Door` inside
    /// HouseOneFloor.prefab), because the pack authors those with the pivot on the
    /// hinge: the leaf occupies local +X from the pivot, so a local Y rotation is
    /// the whole mechanic and no extra rig is needed.
    ///
    /// A plain MonoBehaviour on purpose, and it must stay one. It was a
    /// NetworkBehaviour until 2026-08-05: that made every leaf a **scene**
    /// NetworkObject, FishNet auto-added a ninth to the root of HouseOneFloor, and a
    /// scene NetworkObject deactivates its own GameObject when no NetworkManager is
    /// running (NetworkObject.cs, TryStartDeactivation) — so pressing Play straight
    /// into Level switched the whole house off and the player fell through the floor.
    /// The replicated half now lives on <see cref="DoorState"/>, a spawned object,
    /// which is the pattern RaidState and LobbyRoster already use.
    /// </summary>
    public class Door : MonoBehaviour
    {
        [Header("Swing (MECHANICS.md section 2)")]
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float swingSpeed = 220f;

        [Header("Noise (MECHANICS.md section 2)")]
        [Tooltip("As loud as a step: above Old Man's threshold, below the animals'.")]
        [SerializeField] private float useNoise = 25f;

        [Header("Sound")]
        [Tooltip("The hinge. On the leaf rather than on whoever opened it: Door.EmitNoise " +
                 "runs on the server only, and it is the hinge that creaks anyway.")]
        [SerializeField] private AudioSource creakSource;

        [SerializeField] private AudioClip creak;

        [Header("Authority")]
        [Tooltip("How far from the leaf a request is still believed, world m. Generous " +
                 "against the player's own reach of 1.5: this is a sanity check against " +
                 "a door being opened across the house, not a second reach rule.")]
        [SerializeField] private float serverReach = 3f;

        private static readonly List<Door> Leaves = new List<Door>();

        /// <summary>
        /// Every leaf currently in the scene, in no particular order — the doors are
        /// scene objects and cannot be listed by LevelBootstrapper the way spawned
        /// actors are (MECHANICS.md 7.6). <see cref="DoorState"/> is the only reader,
        /// and it sorts this into an order every machine agrees on.
        /// </summary>
        public static IReadOnlyList<Door> All => Leaves;

        /// <summary>
        /// True once the leaf has been asked to open. The pets' AI (block 4) reads
        /// this: the navmesh runs straight through every doorway, so nothing except
        /// this flag stops an agent from walking through a shut door.
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>How far away a request for this leaf is still believed, world m.</summary>
        public float ServerReach => serverReach;

        // The leaf's closed pose is local Y = 0 for all eight doors in the house —
        // measured 2026-08-03, at zero every leaf sits flush in its frame. The angle
        // is tracked in this field rather than read back from localEulerAngles,
        // because that readback goes through the quaternion and jumps at 0/360.
        private float _angle;
        private float _targetAngle;

        /// <summary>
        /// Open the leaf away from <paramref name="actor"/>, or shut it if it is
        /// already open. Actor-agnostic on purpose: the player reaches it through
        /// PlayerInteractor, Old Man through his AI, and the animals never call it at
        /// all — that is the whole "animals cannot open doors" rule. Under
        /// MECHANICS.md 7.4 the caller is the one making a request; who decides is
        /// <see cref="DoorState"/> when there is a network, and this leaf itself when
        /// there is not.
        /// </summary>
        public void Use(Transform actor)
        {
            var state = DoorState.Current;
            if (state != null)
            {
                state.Use(this, actor);
                return;
            }

            // Networked, but the state object has not spawned yet — the level is still
            // being laid out. Dropped rather than applied: a leaf swung locally here
            // would never be corrected, because the authority does not know it moved.
            if (IsNetworked)
            {
                return;
            }

            // No networking at all — the level opened on its own. We are our own
            // authority, and this is the whole of it.
            EmitNoise(actor);
            ApplySwing(SwingFor(actor));
        }

        private static bool IsNetworked =>
            InstanceFinder.NetworkManager != null &&
            (InstanceFinder.IsClientStarted || InstanceFinder.IsServerStarted);

        /// <summary>
        /// The noise using this leaf makes. Emitted where the decision is taken — Old
        /// Man listens on the server, and noise made on somebody else's machine is
        /// noise he never hears. It still belongs to the actor, not to the leaf
        /// (MECHANICS.md 7.5), so it is the actor's own emitter that makes it and Old
        /// Man walks to where the player stood rather than to where the leaf hangs.
        /// An actor with no emitter (Old Man himself) opens doors quietly.
        /// </summary>
        public void EmitNoise(Transform actor)
        {
            if (actor == null)
            {
                return;
            }

            var noise = actor.GetComponent<NoiseEmitter>();
            if (noise != null)
            {
                noise.Emit(useNoise);
            }
        }

        /// <summary>
        /// The rule, on its own: where this leaf would end up if
        /// <paramref name="actor"/> used it right now. Zero means shut.
        ///
        /// Pure and side-effect free (MECHANICS.md 7.4), because the side depends on
        /// where the actor stands and only one machine may decide that — an actor
        /// standing in the plane of the leaf gives opposite signs on two machines, and
        /// the door would swing two different ways.
        /// </summary>
        public float SwingFor(Transform actor)
        {
            if (IsOpen || actor == null)
            {
                return 0f;
            }

            // The leaf is shut here, so its own space is the closed frame, and local
            // +Z is the face the actor is standing on. A +90 turn about Y sends the
            // leaf from +X to -Z, i.e. away from an actor on the +Z side. Both the
            // side test and the resulting swing live in that one local frame, so the
            // two mirrored modules (Door_2B (3), Door_2B (5)) come out right without
            // a special case: their negative scale flips geometry and test together.
            var side = transform.InverseTransformPoint(actor.position).z;

            return side >= 0f ? openAngle : -openAngle;
        }

        /// <summary>
        /// The state change: the only thing that writes <see cref="IsOpen"/>, and the
        /// only thing that travels — one float. Every peer runs it, so a leaf opened
        /// by one player is open for the pets' pathing on every machine.
        ///
        /// <paramref name="silent"/> is for the case where this is not a door being
        /// used but a door being *described*: a client that has just joined is told the
        /// state of all eight at once, and without this it would walk into a volley of
        /// eight creaks from doors that were opened minutes ago.
        /// </summary>
        public void ApplySwing(float targetAngle, bool silent = false)
        {
            // Asked before the write. A leaf can be handed the angle it already has —
            // the same value written to the replicated list again — and a hinge that
            // creaks without moving reads as a ghost.
            var moved = !Mathf.Approximately(targetAngle, _targetAngle);

            _targetAngle = targetAngle;
            IsOpen = !Mathf.Approximately(targetAngle, 0f);

            // Unity objects: a destroyed one compares == null but is not a real null.
            if (moved && !silent && creakSource != null && creak != null)
            {
                creakSource.PlayOneShot(creak);
            }
        }

        private void Awake()
        {
            Leaves.Add(this);
        }

        private void OnDestroy()
        {
            Leaves.Remove(this);
        }

        // Update, not FixedUpdate: the leaf is a plain collider with no Rigidbody, so
        // it is not simulated — the player's collision against it is resolved by
        // CharacterController.Move, which also runs in Update.
        private void Update()
        {
            if (_angle == _targetAngle)
            {
                return;
            }

            _angle = Mathf.MoveTowards(_angle, _targetAngle, swingSpeed * Time.deltaTime);
            transform.localRotation = Quaternion.Euler(0f, _angle, 0f);
        }
    }
}
