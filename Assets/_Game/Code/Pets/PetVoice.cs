using _Game.Code.Noise;
using UnityEngine;

namespace _Game.Code.Pets
{
    /// <summary>
    /// An animal's voice (MECHANICS.md 4.4): a Dog barks when it notices someone and
    /// whines while it is carried, a Parrot screams when it is caught. Which animal
    /// makes which sound is data — the clip slots on its prefab — so the three
    /// species still differ by values rather than by code.
    ///
    /// A voice is not only a sound: it contributes noise like a step does, so a
    /// barking Dog brings Old Man. That is why an unassigned clip stays completely
    /// silent instead of emitting noise nobody can hear — an inaudible sound that
    /// still kills the player would read as a bug, and the project has no audio
    /// assets at all yet. Drop clips in and the noise starts with them.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class PetVoice : MonoBehaviour
    {
        [Header("Clips — empty means this animal stays silent")]
        [Tooltip("On noticing a player. The Dog's bark.")]
        [SerializeField] private AudioClip noticed;

        [Tooltip("Repeated while being carried. The Dog's whine.")]
        [SerializeField] private AudioClip carried;

        [Tooltip("On being picked up. The Parrot's scream.")]
        [SerializeField] private AudioClip caught;

        [Header("Noise (MECHANICS.md section 2)")]
        [Tooltip("A voice is as loud as a step: heard by Old Man, not by the other animals.")]
        [SerializeField] private float noiseLevel = 25f;

        [Header("Timing, s (MECHANICS.md section 2)")]
        [Tooltip("Shortest gap between two of the same animal's calls.")]
        [SerializeField] private float cooldown = 3f;

        [Tooltip("How often it complains while being carried.")]
        [SerializeField] private float carriedInterval = 4f;

        private AudioSource _source;
        private NoiseEmitter _noise;
        private float _quietUntil;
        private float _nextComplaint;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _noise = GetComponent<NoiseEmitter>();
        }

        /// <summary>It has just seen a player.</summary>
        public void Noticed()
        {
            Say(noticed);
        }

        /// <summary>It has just been picked up.</summary>
        public void Caught()
        {
            // Bypasses the cooldown: being grabbed is the one moment the player must
            // hear, and it happens once per pickup anyway.
            _quietUntil = 0f;
            Say(caught);
            _nextComplaint = Time.time + carriedInterval;
        }

        /// <summary>Called every frame it is being carried; it paces itself.</summary>
        public void WhileCarried()
        {
            if (Time.time < _nextComplaint)
            {
                return;
            }

            _nextComplaint = Time.time + carriedInterval;
            Say(carried);
        }

        /// <summary>
        /// Would a call be made right now, or would the cooldown — or a missing clip —
        /// swallow it. Asked before the call travels: <see cref="Pet.AnnounceNoticed"/>
        /// fires every frame its condition holds, and the cooldown lives at the far end,
        /// so without this the refusal costs a reliable packet to every observer for
        /// nothing (audit 2026-08-05, ~60 a second per animal per client, and every one
        /// of them wasted while the clips are unassigned).
        /// </summary>
        public bool WillNotice => noticed != null && Time.time >= _quietUntil;

        private void Say(AudioClip clip)
        {
            if (clip == null || Time.time < _quietUntil)
            {
                return;
            }

            _quietUntil = Time.time + cooldown;
            _source.PlayOneShot(clip);

            // Unity object: a destroyed one compares == null but is not a real null, so
            // `?.` and `??` would lie about it.
            if (_noise != null)
            {
                _noise.Emit(noiseLevel);
            }
        }
    }
}
