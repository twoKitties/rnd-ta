using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace _Game.Code.OldMan
{
    /// <summary>
    /// Old Man's rifle, built from IK rather than clips: the project owns no weapon
    /// clip at all (MECHANICS.md 5.6), and a clip could not track the victim anyway.
    ///
    /// Two frames and one blend between them. The <em>carry</em> frame is the low
    /// ready he walks his whole round in — barrel forward and down, both hands on
    /// the gun — hung off the chest bone so it rides the torso's bob; the <em>aim</em>
    /// frame is the shouldered pose pointed at the target, in root space so the
    /// look-at's own spine rotation cannot feed back into it. Both hands are pinned
    /// by the humanoid IK pass in both, which is what stops the vendor clips
    /// swinging two empty arms next to a floating rifle (fixed 2026-08-11); only the
    /// head and chest lean is faded in with the aim. The shot is a decaying kick
    /// folded into the aim frame.
    ///
    /// The rifle stays a child of the root and is <em>placed</em> each LateUpdate,
    /// the same idiom as the carried pet on the player. The authored transform is
    /// now only a rest pose for the disabled component.
    ///
    /// A NetworkBehaviour for the same reason as <see cref="ShotFlash"/>: the brain
    /// runs on the server alone, so "he is aiming, at this point" must travel or
    /// only the host would see him shoulder the gun. Carry needs no wire of its own
    /// — it is what every peer does when the aim flag is false. The kick arrives
    /// through ShotFlash's existing every-peer blink, so firing adds no second RPC.
    /// Off the network the brain is read directly — a level played on its own keeps
    /// working, the Door.Use idiom.
    /// </summary>
    public class RifleRig : NetworkBehaviour
    {
        [Tooltip("Read where the process simulates him; never read on a client.")]
        [SerializeField] private OldManBrain brain;

        [Tooltip("The rifle leaf under the prefab root. Placed, never reparented.")]
        [SerializeField] private Transform rifle;

        [Header("Carry frame")]
        [Tooltip("Butt of the stock at low ready, relative to the chest bone's position, " +
                 "in his root space. His root scale is 1.")]
        [SerializeField] private Vector3 carryOffset = new Vector3(0f, 0.02f, 0.04f);

        [Tooltip("Barrel below horizontal while carried, degrees.")]
        [SerializeField] private float carryPitch = 20f;

        // Negative, and it has to be: this rig's arms reach 0.465 m from the shoulder
        // while the grips are 0.34 m apart on a 1.14 m rifle, so a barrel pointed
        // straight ahead puts the foregrip 0.8 m from the left shoulder and the arm
        // tears off the gun. Across the body it lands at 0.42. Measured 2026-08-11.
        [Tooltip("Barrel across the body while carried, degrees. Negative points it left.")]
        [SerializeField] private float carryYaw = -30f;

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
        [Tooltip("Seconds to raise or lower the rifle between carry and aim. Shorter than " +
                 "the 0.4 s aim delay, so the victim sees the barrel settle on them before it fires.")]
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
        private Transform _chest;

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
                    // aiming — a stale one is never read at the carry frame.
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
            Vector3 riflePosition;
            Quaternion rifleRotation;
            Pose(out riflePosition, out rifleRotation);

            // Only the lean is faded: the hands are on the gun at carry too.
            if (_weight > 0f)
            {
                _animator.SetLookAtPosition(_point);
                _animator.SetLookAtWeight(_weight, bodyLook, headLook, 0f, 0.5f);
            }
            else
            {
                _animator.SetLookAtWeight(0f);
            }

            PinHand(AvatarIKGoal.RightHand, riflePosition, rifleRotation, stockGrip, rightHandEuler);
            PinHand(AvatarIKGoal.LeftHand, riflePosition, rifleRotation, foreGrip, leftHandEuler);
        }

        private void PinHand(AvatarIKGoal hand, Vector3 riflePosition, Quaternion rifleRotation,
            Vector3 grip, Vector3 wristEuler)
        {
            _animator.SetIKPosition(hand, riflePosition + rifleRotation * grip);
            _animator.SetIKRotation(hand, rifleRotation * Quaternion.Euler(wristEuler));
            _animator.SetIKPositionWeight(hand, 1f);
            _animator.SetIKRotationWeight(hand, 1f);
        }

        // After the animator, so the rifle lands on the frame the hands were pinned
        // to this frame — the carried-pet ordering.
        private void LateUpdate()
        {
            if (rifle == null)
            {
                return;
            }

            Vector3 position;
            Quaternion rotation;
            Pose(out position, out rotation);

            rifle.SetPositionAndRotation(position, rotation);
            _riflePlaced = true;
        }

        // Whatever moves it puts it back. The rig never returns to the authored pose
        // in play — it is where the prefab draws the rifle, not a state he is ever in.
        private void OnDisable()
        {
            if (rifle == null || !_riflePlaced)
            {
                return;
            }

            rifle.localPosition = _restPosition;
            rifle.localRotation = _restRotation;
            _riflePlaced = false;
        }

        /// <summary>
        /// The rifle's pose this frame — the carry lifted towards the aim by the blend
        /// weight. One formula for both passes (IK, LateUpdate) and every peer, so the
        /// hands and the gun cannot disagree.
        /// </summary>
        private void Pose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 carryPosition;
            Quaternion carryRotation;
            CarryFrame(out carryPosition, out carryRotation);

            if (_weight <= 0f)
            {
                position = carryPosition;
                rotation = carryRotation;
                return;
            }

            Vector3 aimPosition;
            Quaternion aimRotation;
            AimFrame(out aimPosition, out aimRotation);

            position = Vector3.Lerp(carryPosition, aimPosition, _weight);
            rotation = Quaternion.Slerp(carryRotation, aimRotation, _weight);
        }

        // Low ready: the origin rides the chest so the gun bobs with him, the direction
        // stays in root space — taking the chest's rotation too would let the spine's
        // own animation wave the barrel about.
        private void CarryFrame(out Vector3 position, out Quaternion rotation)
        {
            // Resolved on use, not in Awake: the Animator may not have built its
            // humanoid mapping yet by then, and a null cached there would leave him
            // carrying the gun off the fallback point for the whole raid.
            if (_chest == null && _animator != null)
            {
                _chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            }

            var chest = _chest != null
                ? _chest.position
                : transform.TransformPoint(new Vector3(0f, shoulderOffset.y, 0f));

            var origin = chest + transform.rotation * carryOffset;
            var direction = transform.rotation * Quaternion.Euler(carryPitch, carryYaw, 0f) * Vector3.forward;

            FrameFrom(origin, direction, out position, out rotation);
        }

        private void AimFrame(out Vector3 position, out Quaternion rotation)
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

            FrameFrom(origin, direction, out position, out rotation);
            position -= direction * (recoilKick * _recoil);
        }

        // The stock grip lands on the origin in both frames, so the right hand holds
        // the same point of the weapon whether it is carried or shouldered.
        private void FrameFrom(Vector3 origin, Vector3 direction, out Vector3 position,
            out Quaternion rotation)
        {
            // The mesh's barrel runs down -X, so the same +90° yaw the rest pose
            // carries maps it onto the aim direction.
            rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0f, 90f, 0f);
            position = origin - rotation * stockGrip;
        }
    }
}
