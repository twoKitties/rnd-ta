using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace _Game.Code.OldMan
{
    /// <summary>
    /// The shooting animation, built from IK rather than clips: the project owns no
    /// weapon clip at all (MECHANICS.md 5.6), and a clip could not track the victim
    /// anyway. While the brain aims, the rifle is raised from its authored hip-carry
    /// to a shouldered frame pointed at the target, both hands are pinned onto it
    /// with the humanoid IK pass, and the head and chest lean onto the same line;
    /// the shot itself is a decaying kick folded into that frame. When he is not
    /// aiming the weight blends to zero and the vendor animation runs untouched.
    ///
    /// The rifle stays a child of the root and is <em>placed</em> each LateUpdate,
    /// the same idiom as the carried pet on the player. It is not parented to a
    /// hand, so at weight zero the authored rest pose is restored exactly.
    ///
    /// A NetworkBehaviour for the same reason as <see cref="ShotFlash"/>: the brain
    /// runs on the server alone, so "he is aiming, at this point" must travel or
    /// only the host would see him shoulder the gun. Two SyncVars carry it; the
    /// kick arrives through ShotFlash's existing every-peer blink, so firing adds
    /// no second RPC. Off the network the brain is read directly — a level played
    /// on its own keeps working, the Door.Use idiom.
    /// </summary>
    public class RifleAim : NetworkBehaviour
    {
        [Tooltip("Read where the process simulates him; never read on a client.")]
        [SerializeField] private OldManBrain brain;

        [Tooltip("The rifle leaf under the prefab root. Placed, never reparented.")]
        [SerializeField] private Transform rifle;

        [Header("Aim frame")]
        [Tooltip("Butt of the stock while shouldered, in his root space. His root scale is 1.")]
        [SerializeField] private Vector3 shoulderOffset = new Vector3(0.18f, 1.35f, 0.08f);

        [Tooltip("Aimed height above the target spot, world m. The brain hands over the " +
                 "victim's feet; the alien is 0.5 m tall, so half of that is the chest.")]
        [SerializeField] private float aimHeight = 0.25f;

        [Header("Grips, rifle mesh space (barrel is -X, ~1.14 m long)")]
        [SerializeField] private Vector3 stockGrip = new Vector3(0.02f, -0.04f, 0f);
        [SerializeField] private Vector3 foreGrip = new Vector3(-0.45f, -0.04f, 0f);

        [Tooltip("Wrist correction per hand, degrees in the rifle's frame. Tuned by eye.")]
        [SerializeField] private Vector3 rightHandEuler = new Vector3(0f, 0f, -90f);
        [SerializeField] private Vector3 leftHandEuler = new Vector3(0f, 0f, 90f);

        [Header("Blending")]
        [Tooltip("Seconds to raise or lower the rifle. Shorter than the 0.4 s aim delay, " +
                 "so the victim sees the barrel settle on them before it fires.")]
        [SerializeField] private float blendTime = 0.15f;

        [Header("Recoil")]
        [Tooltip("Seconds the kick takes to decay.")]
        [SerializeField] private float recoilTime = 0.25f;

        [Tooltip("How far the rifle is driven back along the barrel at full kick, m.")]
        [SerializeField] private float recoilKick = 0.12f;

        [Tooltip("How far the barrel is thrown up at full kick, degrees.")]
        [SerializeField] private float recoilPitch = 12f;

        [Header("Look")]
        [Range(0f, 1f)] [SerializeField] private float bodyLook = 0.4f;
        [Range(0f, 1f)] [SerializeField] private float headLook = 0.9f;

        // Who decides is the object's spawn state, the Pet idiom: cache the
        // NetworkObject and ask it — NetworkBehaviour.IsSpawned throws on a
        // component that was never initialised, which is exactly the offline case.
        private readonly SyncVar<bool> _aiming = new SyncVar<bool>();
        private readonly SyncVar<Vector3> _aimPoint = new SyncVar<Vector3>();

        private NetworkObject _nob;
        private Animator _animator;

        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private bool _riflePlaced;

        private bool _wantAim;
        private Vector3 _point;
        private float _weight;
        private float _recoil;

        private void Awake()
        {
            _nob = GetComponent<NetworkObject>();
            _animator = GetComponent<Animator>();

            // Unity object: a destroyed one compares == null but is not a real null.
            if (rifle != null)
            {
                _restPosition = rifle.localPosition;
                _restRotation = rifle.localRotation;
            }
        }

        /// <summary>
        /// Where the barrel is pointed, world: the authority's word straight from the
        /// brain, a client's from the SyncVar. Read by ShotFlash when the blink lands
        /// to aim the pellet, so every peer flies it along its own rifle.
        /// </summary>
        public Vector3 AimPoint => _point;

        /// <summary>
        /// He fired. Reached on every peer through ShotFlash's blink, which already
        /// travels; locally it also holds the aim frame up while it decays, so the
        /// rifle kicks before it drops rather than dropping through the kick.
        /// </summary>
        public void Kick()
        {
            _recoil = 1f;
        }

        private void Update()
        {
            var spawned = _nob != null && _nob.IsSpawned;

            if (spawned && !_nob.IsServerInitialized)
            {
                // A client: the wire is the only truth, the brain here is off.
                _wantAim = _aiming.Value;
                _point = _aimPoint.Value;
            }
            else
            {
                _wantAim = brain != null && brain.isActiveAndEnabled && brain.State == OldManState.Aim;
                if (_wantAim)
                {
                    _point = brain.TargetSpot + Vector3.up * aimHeight;
                }

                if (spawned)
                {
                    // SyncVars send on change only, and the point matters only while
                    // aiming — a stale one is never read at weight zero.
                    _aiming.Value = _wantAim;
                    if (_wantAim)
                    {
                        _aimPoint.Value = _point;
                    }
                }
            }

            if (_recoil > 0f && recoilTime > 0f)
            {
                _recoil = Mathf.MoveTowards(_recoil, 0f, Time.deltaTime / recoilTime);
            }

            var hold = _wantAim || _recoil > 0.01f;
            var step = blendTime > 0f ? Time.deltaTime / blendTime : 1f;
            _weight = Mathf.MoveTowards(_weight, hold ? 1f : 0f, step);
        }

        // Needs IK Pass ticked on the controller's base layer — which is why the
        // prefab runs on the project's copy of the vendor controller, not the vendor's.
        private void OnAnimatorIK(int layerIndex)
        {
            if (_weight <= 0f)
            {
                _animator.SetLookAtWeight(0f);
                _animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
                _animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                _animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
                return;
            }

            Vector3 riflePosition;
            Quaternion rifleRotation;
            ComputeFrame(out riflePosition, out rifleRotation);

            _animator.SetLookAtPosition(_point);
            _animator.SetLookAtWeight(_weight, bodyLook, headLook, 0f, 0.5f);

            PinHand(AvatarIKGoal.RightHand, riflePosition, rifleRotation, stockGrip, rightHandEuler);
            PinHand(AvatarIKGoal.LeftHand, riflePosition, rifleRotation, foreGrip, leftHandEuler);
        }

        private void PinHand(AvatarIKGoal hand, Vector3 riflePosition, Quaternion rifleRotation,
            Vector3 grip, Vector3 wristEuler)
        {
            _animator.SetIKPosition(hand, riflePosition + rifleRotation * grip);
            _animator.SetIKRotation(hand, rifleRotation * Quaternion.Euler(wristEuler));
            _animator.SetIKPositionWeight(hand, _weight);
            _animator.SetIKRotationWeight(hand, _weight);
        }

        // After the animator, so the rifle lands on the frame the hands were pinned
        // to this frame — the carried-pet ordering.
        private void LateUpdate()
        {
            if (rifle == null)
            {
                return;
            }

            if (_weight <= 0f)
            {
                // Put back exactly what was authored, once, then stop touching it.
                if (_riflePlaced)
                {
                    rifle.localPosition = _restPosition;
                    rifle.localRotation = _restRotation;
                    _riflePlaced = false;
                }

                return;
            }

            Vector3 aimPosition;
            Quaternion aimRotation;
            ComputeFrame(out aimPosition, out aimRotation);

            var parent = rifle.parent;
            var restPosition = parent.TransformPoint(_restPosition);
            var restRotation = parent.rotation * _restRotation;

            rifle.SetPositionAndRotation(
                Vector3.Lerp(restPosition, aimPosition, _weight),
                Quaternion.Slerp(restRotation, aimRotation, _weight));
            _riflePlaced = true;
        }

        /// <summary>
        /// One formula produces the rifle's aimed pose and, through the grips, both
        /// hand targets — depending only on the root transform, the synced point and
        /// the kick, so every peer and both passes (IK, LateUpdate) agree.
        /// </summary>
        private void ComputeFrame(out Vector3 position, out Quaternion rotation)
        {
            var origin = transform.TransformPoint(shoulderOffset);

            var direction = _point - origin;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.forward;
            }
            else
            {
                direction.Normalize();
            }

            if (_recoil > 0f)
            {
                var right = Vector3.Cross(Vector3.up, direction);
                direction = Quaternion.AngleAxis(-recoilPitch * _recoil, right) * direction;
            }

            // The mesh's barrel runs down -X, so the same +90° yaw the rest pose
            // carries maps it onto the aim direction.
            rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, 90f, 0f);
            position = origin - rotation * stockGrip - direction * (recoilKick * _recoil);
        }
    }
}
