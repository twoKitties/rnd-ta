using _Game.Code.Noise;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
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
    /// </summary>
    public class Door : NetworkBehaviour
    {
        [Header("Swing (MECHANICS.md section 2)")]
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float swingSpeed = 220f;

        [Header("Noise (MECHANICS.md section 2)")]
        [Tooltip("As loud as a step: above Old Man's threshold, below the animals'.")]
        [SerializeField] private float useNoise = 25f;

        [Header("Authority")]
        [Tooltip("How far from the leaf a request is still believed, world m. Generous " +
                 "against the player's own reach of 1.5: this is a sanity check against " +
                 "a door being opened across the house, not a second reach rule.")]
        [SerializeField] private float serverReach = 3f;

        // The one piece of this leaf that travels. Every peer applies it, so a door
        // opened by one player is open for everybody — and, just as importantly, for
        // the pets' pathing, which reads IsOpen (MECHANICS.md 4.6).
        private readonly SyncVar<float> _swing = new SyncVar<float>();

        /// <summary>
        /// True once the leaf has been asked to open. The pets' AI (block 4) reads
        /// this: the navmesh runs straight through every doorway, so nothing except
        /// this flag stops an agent from walking through a shut door.
        /// </summary>
        public bool IsOpen { get; private set; }

        // The leaf's closed pose is local Y = 0 for all eight doors in the house —
        // measured 2026-08-03, at zero every leaf sits flush in its frame. The angle
        // is tracked in this field rather than read back from localEulerAngles,
        // because that readback goes through the quaternion and jumps at 0/360.
        private float _angle;
        private float _targetAngle;

        /// <summary>
        /// Open the leaf away from <paramref name="actor"/>, or shut it if it is
        /// already open. Actor-agnostic on purpose: the player reaches it through
        /// PlayerInteractor, Old Man will call it from his AI in block 5, and the
        /// animals never call it at all — that is the whole "animals cannot open
        /// doors" rule. Under MECHANICS.md 7.4 the caller is the one making a
        /// request; this method is what the host will run once netcode lands.
        /// </summary>
        public void Use(Transform actor)
        {
            // No networking at all — the level opened on its own. We are our own
            // authority, and this is the whole of it.
            if (!IsSpawned)
            {
                EmitNoise(actor);
                ApplySwing(SwingFor(actor));
                return;
            }

            if (IsServerInitialized)
            {
                ServerUse(actor);
                return;
            }

            // A client only asks. It does not send where it is standing either — the
            // host works that out from the avatar this connection owns, so the side the
            // leaf swings to cannot be argued about (MECHANICS.md 7.4).
            RequestUse();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestUse(NetworkConnection sender = null)
        {
            if (sender == null || sender.FirstObject == null)
            {
                return;
            }

            var actor = sender.FirstObject.transform;

            // The host re-runs the rule it would have run for itself, including the
            // reach check the asking client already did — a request is not a fact.
            if (Vector3.Distance(actor.position, transform.position) > serverReach)
            {
                return;
            }

            ServerUse(actor);
        }

        // The decision, taken once, on the machine that owns the rules.
        private void ServerUse(Transform actor)
        {
            // Emitted here rather than on the asking client: Old Man listens on the
            // server, and noise made on somebody else's machine is noise he never
            // hears. The noise still belongs to the actor, not to the leaf
            // (MECHANICS.md 7.5) — it is the actor's copy on this machine that emits.
            EmitNoise(actor);

            // The state change travels as one float and every peer applies it below.
            _swing.Value = SwingFor(actor);
        }

        private void EmitNoise(Transform actor)
        {
            // The side effect is the right one — Old Man walks to where the player
            // stood, not to where the leaf hangs. An actor with no emitter (Old Man
            // himself) opens doors quietly.
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
        /// only thing a netcode pass has to carry — one float. Every peer runs it, so
        /// a leaf opened by one player is open for the pets' pathing on every machine.
        /// </summary>
        public void ApplySwing(float targetAngle)
        {
            _targetAngle = targetAngle;
            IsOpen = !Mathf.Approximately(targetAngle, 0f);
        }

        private void Awake()
        {
            // Driven from the replicated value rather than from whoever asked, so the
            // server's own copy and every client's go through exactly one line.
            _swing.OnChange += OnSwingChanged;
        }

        private void OnDestroy()
        {
            _swing.OnChange -= OnSwingChanged;
        }

        private void OnSwingChanged(float previous, float next, bool asServer)
        {
            ApplySwing(next);
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
