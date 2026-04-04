using UnityEngine;
using System.Collections;

namespace MazeChase.VFX
{
    /// <summary>
    /// Singleton MonoBehaviour providing screen-wide visual effects:
    /// full-screen color flash and camera shake. Uses OnGUI for the
    /// flash overlay so it works with the built-in render pipeline
    /// without extra cameras or render textures.
    /// </summary>
    public class ScreenEffects : MonoBehaviour
    {
        public static ScreenEffects Instance { get; private set; }

        // Flash state
        private bool _flashing;
        private Color _flashColor;
        private float _flashDuration;
        private float _flashElapsed;
        private Texture2D _whiteTexture;

        // Shake state
        private bool _shaking;
        private float _shakeIntensity;
        private float _shakeDuration;
        private float _shakeElapsed;
        private Vector3 _originalCameraPos;
        private Transform _cameraTransform;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Create a 1x1 white texture for flash overlay
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }

        private void Update()
        {
            // Update flash
            if (_flashing)
            {
                _flashElapsed += Time.unscaledDeltaTime;
                if (_flashElapsed >= _flashDuration)
                    _flashing = false;
            }

            // Update shake
            if (_shaking)
            {
                if (_cameraTransform == null)
                {
                    var cam = Camera.main;
                    if (cam != null)
                        _cameraTransform = cam.transform;
                }

                _shakeElapsed += Time.unscaledDeltaTime;

                if (_shakeElapsed >= _shakeDuration)
                {
                    _shaking = false;
                    if (_cameraTransform != null)
                        _cameraTransform.position = _originalCameraPos;
                }
                else if (_cameraTransform != null)
                {
                    float decay = 1f - (_shakeElapsed / _shakeDuration);
                    float offsetX = Random.Range(-_shakeIntensity, _shakeIntensity) * decay;
                    float offsetY = Random.Range(-_shakeIntensity, _shakeIntensity) * decay;
                    _cameraTransform.position = _originalCameraPos + new Vector3(offsetX, offsetY, 0f);
                }
            }
        }

        /// <summary>
        /// Draw the flash overlay on top of everything via OnGUI.
        /// </summary>
        private void OnGUI()
        {
            if (!_flashing)
                return;

            float t = Mathf.Clamp01(_flashElapsed / _flashDuration);
            float alpha = Mathf.Lerp(_flashColor.a, 0f, t);
            GUI.color = new Color(_flashColor.r, _flashColor.g, _flashColor.b, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _whiteTexture);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Trigger a full-screen color flash that fades to transparent.
        /// </summary>
        /// <param name="color">Flash color (alpha sets initial opacity).</param>
        /// <param name="duration">Duration of the fade in seconds.</param>
        public void Flash(Color color, float duration = 0.15f)
        {
            _flashColor = color;
            _flashDuration = duration;
            _flashElapsed = 0f;
            _flashing = true;
        }

        /// <summary>
        /// Trigger a brief camera shake effect.
        /// </summary>
        /// <param name="intensity">Maximum offset in world units.</param>
        /// <param name="duration">Duration of the shake in seconds.</param>
        public void Shake(float intensity = 0.1f, float duration = 0.2f)
        {
            if (_cameraTransform == null)
            {
                var cam = Camera.main;
                if (cam != null)
                    _cameraTransform = cam.transform;
            }

            if (_cameraTransform == null)
                return;

            // Only store original pos if not already shaking
            if (!_shaking)
                _originalCameraPos = _cameraTransform.position;

            _shakeIntensity = intensity;
            _shakeDuration = duration;
            _shakeElapsed = 0f;
            _shaking = true;
        }
    }
}
