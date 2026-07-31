using System.Collections;
using UnityEngine;

namespace StylizedcharacterCollection1
{
    public class CharacterSelectionController : MonoBehaviour
    {
        [SerializeField] private GameObject[] characters;
        [SerializeField] private GameObject[] characterUIs;

        [Header("Camera")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Transform[] cameraTargets;
        [SerializeField] private float cameraMoveDuration = 0.5f;

        private Coroutine cameraCoroutine;

        private int currentIndex = 0;
        private Animator currentAnimator;

        [SerializeField] private float switchDelay = 0.5f;
        private bool isSwitching = false;

        void Start()
        {
            ShowCharacter(currentIndex);
        }

        void ShowCharacter(int index)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                characters[i].SetActive(i == index);

                if (characterUIs != null && characterUIs.Length > i)
                    characterUIs[i].SetActive(i == index);
            }

            currentAnimator = characters[index].GetComponent<Animator>();

            // Smooth Camera Movement
            if (cameraTransform != null &&
                cameraTargets != null &&
                cameraTargets.Length > index &&
                cameraTargets[index] != null)
            {
                if (cameraCoroutine != null)
                    StopCoroutine(cameraCoroutine);

                cameraCoroutine = StartCoroutine(MoveCamera(cameraTargets[index]));
            }
        }

        IEnumerator MoveCamera(Transform target)
        {
            Vector3 startPos = cameraTransform.position;
            Quaternion startRot = cameraTransform.rotation;

            float elapsed = 0f;

            while (elapsed < cameraMoveDuration)
            {
                elapsed += Time.deltaTime;

                float t = Mathf.Clamp01(elapsed / cameraMoveDuration);

                
                t = t * t * (3f - 2f * t);

                cameraTransform.position = Vector3.Lerp(startPos, target.position, t);
                cameraTransform.rotation = Quaternion.Slerp(startRot, target.rotation, t);

                yield return null;
            }

            cameraTransform.position = target.position;
            cameraTransform.rotation = target.rotation;
        }

        public void NextCharacter()
        {
            StartCoroutine(SwitchCharacterWithDelay(+1));
        }

        public void PreviousCharacter()
        {
            StartCoroutine(SwitchCharacterWithDelay(-1));
        }

        IEnumerator SwitchCharacterWithDelay(int direction)
        {
            if (isSwitching) yield break;
            isSwitching = true;

            PlayIdle();
            yield return new WaitForSeconds(switchDelay);

            currentIndex += direction;

            if (currentIndex >= characters.Length)
                currentIndex = 0;

            if (currentIndex < 0)
                currentIndex = characters.Length - 1;

            ShowCharacter(currentIndex);

            isSwitching = false;
        }

        // ====================== Animations ======================

        public void PlayWalk1()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Walk 1", true);
        }

        public void PlayWalk2()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Walk 2", true);
        }

        public void PlayRun1()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Run 1", true);
        }

        public void PlayJump()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetTrigger("Jump");
        }

        public void PlaySlowRun()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Slow Run", true);
        }

        public void PlayIdle2()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Idle 2", true);
        }

        public void PlaySadWalk()
        {
            if (currentAnimator == null) return;
            PlayIdle();
            currentAnimator.SetBool("Sad Walk", true);
        }

        public void PlayIdle()
        {
            if (currentAnimator == null) return;

            currentAnimator.SetBool("Walk 1", false);
            currentAnimator.SetBool("Walk 2", false);
            currentAnimator.SetBool("Run 1", false);
            currentAnimator.SetBool("Slow Run", false);
            currentAnimator.SetBool("Sad Walk", false);
            currentAnimator.SetBool("Idle 2", false);
        }
    }
}