using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace _Game.Code.UI
{
    public class LoadingScreen : MonoBehaviour
    {
        [SerializeField] private Image _loadingIcon;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private float _loadingIconRotationSpeed = 15;
        
        private Coroutine _fadeOutCoroutine;
        private Coroutine _rotateIconCoroutine;

        public void Show()
        {
            StopFadeOut();
            _canvasGroup.alpha = 1;
            _rotateIconCoroutine = StartCoroutine(RotateIcon());
        }

        public void Hide()
        {
            StopFadeOut();
            _fadeOutCoroutine = StartCoroutine(FadeOut());
        }

        private void StopFadeOut()
        {
            if(_fadeOutCoroutine != null)
                StopCoroutine(_fadeOutCoroutine);
        }

        private IEnumerator FadeOut()
        {
            while (_canvasGroup.alpha > 0)
            {
                _canvasGroup.alpha -= 0.01f;
                yield return null;
            }
            
            _canvasGroup.alpha = 0;
            StopRotateIcon();
        }

        private void StopRotateIcon()
        {
            if(_rotateIconCoroutine != null)
                StopCoroutine(_rotateIconCoroutine);
        }

        private IEnumerator RotateIcon()
        {
            while (true)
            {
                _loadingIcon.transform.Rotate(0, 0, _loadingIconRotationSpeed);
                yield return null;
            }
        }
    }
}