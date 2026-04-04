using UnityEngine;
using System.Collections;

namespace MazeChase.VFX
{
    /// <summary>
    /// Static utility class for spawning lightweight particle burst effects
    /// using runtime-generated sprites. No prefabs or imported assets required.
    /// </summary>
    public static class SimpleParticles
    {
        private static Sprite _particleSprite;

        /// <summary>
        /// Returns a cached 4x4 white square sprite for particle rendering.
        /// </summary>
        private static Sprite GetParticleSprite()
        {
            if (_particleSprite != null)
                return _particleSprite;

            var tex = new Texture2D(4, 4);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();

            _particleSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 32f);
            return _particleSprite;
        }

        /// <summary>
        /// Spawns a burst of small square particles that move outward, fade, and self-destruct.
        /// </summary>
        /// <param name="position">World position to spawn at.</param>
        /// <param name="color">Color of the particles.</param>
        /// <param name="count">Number of particles (default 6).</param>
        /// <param name="duration">Lifetime in seconds (default 0.3).</param>
        /// <param name="speed">Outward movement speed (default 2).</param>
        public static void SpawnBurst(Vector3 position, Color color, int count = 6, float duration = 0.3f, float speed = 2f)
        {
            var root = new GameObject("ParticleBurst");
            root.transform.position = position;

            var driver = root.AddComponent<ParticleBurstDriver>();
            driver.Init(color, count, duration, speed, false);
        }

        /// <summary>
        /// Spawns a larger burst effect using the ghost's color, suitable for ghost-eat events.
        /// </summary>
        public static void SpawnGhostEatEffect(Vector3 position, Color ghostColor)
        {
            var root = new GameObject("GhostEatEffect");
            root.transform.position = position;

            var driver = root.AddComponent<ParticleBurstDriver>();
            driver.Init(ghostColor, 10, 0.5f, 3f, false);
        }

        /// <summary>
        /// Spawns yellow particles spiraling outward, used for player death.
        /// </summary>
        public static void SpawnDeathEffect(Vector3 position)
        {
            var root = new GameObject("DeathEffect");
            root.transform.position = position;

            var driver = root.AddComponent<ParticleBurstDriver>();
            driver.Init(new Color(1f, 0.9f, 0.2f, 1f), 12, 0.6f, 2.5f, true);
        }

        /// <summary>
        /// Returns the shared particle sprite for external use (e.g., glow quads).
        /// </summary>
        public static Sprite SharedSprite => GetParticleSprite();

        /// <summary>
        /// Internal MonoBehaviour that drives the particle animation each frame
        /// and destroys the root object when the effect completes.
        /// </summary>
        private class ParticleBurstDriver : MonoBehaviour
        {
            private struct Particle
            {
                public Transform transform;
                public SpriteRenderer renderer;
                public Vector2 direction;
                public float angle; // for spiral
            }

            private Particle[] _particles;
            private float _duration;
            private float _speed;
            private float _elapsed;
            private Color _color;
            private bool _spiral;

            public void Init(Color color, int count, float duration, float speed, bool spiral)
            {
                _color = color;
                _duration = duration;
                _speed = speed;
                _spiral = spiral;

                _particles = new Particle[count];
                Sprite sprite = GetParticleSprite();

                for (int i = 0; i < count; i++)
                {
                    float angle = (360f / count) * i + Random.Range(-15f, 15f);
                    float rad = angle * Mathf.Deg2Rad;

                    var go = new GameObject($"p{i}");
                    go.transform.parent = transform;
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localScale = Vector3.one * 0.08f;

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = sprite;
                    sr.color = color;
                    sr.sortingOrder = 20;

                    _particles[i] = new Particle
                    {
                        transform = go.transform,
                        renderer = sr,
                        direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)),
                        angle = angle
                    };
                }
            }

            private void Update()
            {
                _elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(_elapsed / _duration);

                for (int i = 0; i < _particles.Length; i++)
                {
                    ref var p = ref _particles[i];
                    if (p.transform == null) continue;

                    // Movement
                    Vector2 dir = p.direction;
                    if (_spiral)
                    {
                        float spiralAngle = (p.angle + _elapsed * 360f) * Mathf.Deg2Rad;
                        dir = new Vector2(Mathf.Cos(spiralAngle), Mathf.Sin(spiralAngle));
                    }

                    p.transform.localPosition += (Vector3)(dir * _speed * Time.deltaTime);

                    // Fade and shrink
                    float alpha = Mathf.Lerp(1f, 0f, t);
                    float scale = Mathf.Lerp(0.08f, 0.02f, t);
                    p.renderer.color = new Color(_color.r, _color.g, _color.b, alpha);
                    p.transform.localScale = Vector3.one * scale;
                }

                if (_elapsed >= _duration)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
