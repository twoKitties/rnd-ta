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
    /// </summary>
    public class Door : MonoBehaviour
    {
        [Header("Swing (MECHANICS.md section 2)")]
        [SerializeField] private float openAngle = 90f;
        [SerializeField] private float swingSpeed = 220f;

        [Header("Noise (MECHANICS.md section 2)")]
        [Tooltip("As loud as a step: above Old Man's threshold, below the animals'.")]
        [SerializeField] private float useNoise = 25f;

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
            // The noise belongs to whoever pulled the handle, not to the leaf:
            // MECHANICS.md 7.5 keeps noise on actors. The side effect is the right
            // one — Old Man walks to where the player stood, not to where the leaf
            // hangs. An actor with no emitter (Old Man himself) opens doors quietly.
            var noise = actor.GetComponent<NoiseEmitter>();
            if (noise != null)
            {
                noise.Emit(useNoise);
            }

            if (IsOpen)
            {
                _targetAngle = 0f;
                IsOpen = false;
                return;
            }

            // The leaf is shut here, so its own space is the closed frame, and local
            // +Z is the face the actor is standing on. A +90 turn about Y sends the
            // leaf from +X to -Z, i.e. away from an actor on the +Z side. Both the
            // side test and the resulting swing live in that one local frame, so the
            // two mirrored modules (Door_2B (3), Door_2B (5)) come out right without
            // a special case: their negative scale flips geometry and test together.
            var side = transform.InverseTransformPoint(actor.position).z;

            _targetAngle = side >= 0f ? openAngle : -openAngle;
            IsOpen = true;
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
