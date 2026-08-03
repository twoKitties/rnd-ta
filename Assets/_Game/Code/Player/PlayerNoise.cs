using _Game.Code.Noise;
using UnityEngine;

namespace _Game.Code.Player
{
    /// <summary>
    /// Turns the avatar's movement state into noise. The four fields are the four
    /// rows of the "Передвижение и шум игрока" table in MECHANICS.md section 2 and
    /// the four values of <see cref="MoveState"/> — the table, the enum and these
    /// fields only ever change together.
    ///
    /// Noise comes from the state, never from <see cref="PlayerController.Speed"/>:
    /// hauling a Dog makes a player slow but no quieter.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(NoiseEmitter))]
    public class PlayerNoise : MonoBehaviour
    {
        [Header("Noise per state (MECHANICS.md section 2)")]
        [SerializeField] private float idleNoise;

        [Tooltip("Below every listener's threshold: Crouch is silent by construction.")]
        [SerializeField] private float crouchNoise = 8f;

        [SerializeField] private float walkNoise = 25f;
        [SerializeField] private float sprintNoise = 60f;

        private PlayerController _controller;
        private NoiseEmitter _emitter;

        private void Awake()
        {
            _controller = GetComponent<PlayerController>();
            _emitter = GetComponent<NoiseEmitter>();
        }

        private void Update()
        {
            _emitter.Emit(NoiseFor(_controller.State));
        }

        private float NoiseFor(MoveState state)
        {
            switch (state)
            {
                case MoveState.Crouch:
                    return crouchNoise;

                case MoveState.Walk:
                    return walkNoise;

                case MoveState.Sprint:
                    return sprintNoise;

                default:
                    return idleNoise;
            }
        }
    }
}
