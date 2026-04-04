using System;
using System.Collections;
using UnityEngine;

namespace MazeChase.Game
{
    /// <summary>
    /// Spawns bonus fruit items at the designated tile when pellet thresholds are met.
    /// Classic behavior: fruit appears after 70 and 170 pellets eaten, stays for ~10 seconds.
    /// </summary>
    public class FruitSpawner : MonoBehaviour
    {
        public static FruitSpawner Instance { get; private set; }

        public event Action<string, int> OnFruitCollected; // fruitName, score

        [SerializeField] private float despawnTime = 10f;

        private MazeRenderer _mazeRenderer;
        private PelletManager _pelletManager;
        private GameObject _activeFruit;
        private Vector2Int _fruitTile;
        private bool _firstFruitSpawned;
        private bool _secondFruitSpawned;
        private string _currentFruitName;
        private int _currentFruitScore;
        private int _currentRound = 1;

        // Fruit types and scores per round
        private static readonly string[] FruitNames = {
            "Cherry", "Strawberry", "Orange", "Apple",
            "Grape", "Galaxian", "Bell", "Key"
        };
        private static readonly int[] FruitScores = {
            100, 300, 500, 700, 1000, 2000, 3000, 5000
        };

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public void Init(MazeRenderer renderer, PelletManager pelletManager)
        {
            _mazeRenderer = renderer;
            _pelletManager = pelletManager;
            _fruitTile = MazeData.GetFruitSpawn();

            if (_pelletManager != null)
                _pelletManager.OnPelletCollected += OnPelletEaten;
        }

        public void SetRound(int round)
        {
            _currentRound = round;
            _firstFruitSpawned = false;
            _secondFruitSpawned = false;
            DespawnFruit();

            int fruitIndex = Mathf.Clamp(round - 1, 0, FruitNames.Length - 1);
            _currentFruitName = FruitNames[fruitIndex];
            _currentFruitScore = FruitScores[fruitIndex];
        }

        private void OnPelletEaten(Vector2Int tile, MazeTile type)
        {
            if (_pelletManager == null) return;
            int eaten = _pelletManager.PelletsEaten;

            if (!_firstFruitSpawned && eaten >= 70)
            {
                _firstFruitSpawned = true;
                SpawnFruit();
            }
            else if (!_secondFruitSpawned && eaten >= 170)
            {
                _secondFruitSpawned = true;
                SpawnFruit();
            }
        }

        private void SpawnFruit()
        {
            if (_activeFruit != null) return;

            _activeFruit = new GameObject($"Fruit_{_currentFruitName}");
            _activeFruit.transform.position = _mazeRenderer != null
                ? _mazeRenderer.TileToWorld(_fruitTile.x, _fruitTile.y)
                : new Vector3(_fruitTile.x * 0.5f, _fruitTile.y * 0.5f, 0f);

            var sr = _activeFruit.AddComponent<SpriteRenderer>();
            sr.sprite = CreateFruitSprite();
            sr.sortingOrder = 5;

            Debug.Log($"[FruitSpawner] Spawned {_currentFruitName} worth {_currentFruitScore} points.");
            StartCoroutine(DespawnAfterDelay());
        }

        private IEnumerator DespawnAfterDelay()
        {
            yield return new WaitForSeconds(despawnTime);
            DespawnFruit();
        }

        private void DespawnFruit()
        {
            if (_activeFruit != null)
            {
                Destroy(_activeFruit);
                _activeFruit = null;
            }
        }

        /// <summary>
        /// Call from collision detection when player reaches the fruit tile.
        /// Returns the score if fruit was collected, 0 otherwise.
        /// </summary>
        public int TryCollectFruit(Vector2Int playerTile)
        {
            if (_activeFruit == null) return 0;
            if (playerTile != _fruitTile) return 0;

            int score = _currentFruitScore;
            string name = _currentFruitName;
            DespawnFruit();
            StopAllCoroutines();

            Debug.Log($"[FruitSpawner] {name} collected! +{score}");
            OnFruitCollected?.Invoke(name, score);
            return score;
        }

        public bool IsFruitActive => _activeFruit != null;
        public Vector2Int FruitTile => _fruitTile;

        private Sprite CreateFruitSprite()
        {
            const int size = 24;
            const float worldSize = 0.35f;
            float ppu = size / worldSize;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;

            // Clear the texture
            Color[] clear = new Color[size * size];
            for (int i = 0; i < clear.Length; i++) clear[i] = Color.clear;
            tex.SetPixels(clear);

            int fruitIndex = Mathf.Clamp(_currentRound - 1, 0, FruitNames.Length - 1);
            switch (fruitIndex)
            {
                case 0: DrawCherry(tex, size); break;
                case 1: DrawStrawberry(tex, size); break;
                case 2: DrawOrange(tex, size); break;
                case 3: DrawApple(tex, size); break;
                case 4: DrawGrape(tex, size); break;
                case 5: DrawGalaxian(tex, size); break;
                case 6: DrawBell(tex, size); break;
                default: DrawKey(tex, size); break;
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
        }

        // -- Fruit drawing helpers --

        private static void FillCircle(Texture2D tex, float cx, float cy, float r, Color c)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - r));
            int maxX = Mathf.Min(tex.width - 1, Mathf.CeilToInt(cx + r));
            int minY = Mathf.Max(0, Mathf.FloorToInt(cy - r));
            int maxY = Mathf.Min(tex.height - 1, Mathf.CeilToInt(cy + r));
            for (int py = minY; py <= maxY; py++)
                for (int px = minX; px <= maxX; px++)
                {
                    float dx = px + 0.5f - cx;
                    float dy = py + 0.5f - cy;
                    if (dx * dx + dy * dy <= r * r)
                        tex.SetPixel(px, py, c);
                }
        }

        private static void FillRect(Texture2D tex, int x0, int y0, int x1, int y1, Color c)
        {
            for (int py = Mathf.Max(0, y0); py <= Mathf.Min(tex.height - 1, y1); py++)
                for (int px = Mathf.Max(0, x0); px <= Mathf.Min(tex.width - 1, x1); px++)
                    tex.SetPixel(px, py, c);
        }

        private static void DrawLine(Texture2D tex, int x0, int y0, int x1, int y1, Color c)
        {
            // Bresenham's line algorithm
            int dx = Mathf.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                if (x0 >= 0 && x0 < tex.width && y0 >= 0 && y0 < tex.height)
                    tex.SetPixel(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Cherry: Two red circles with green stems joining at top.</summary>
        private static void DrawCherry(Texture2D tex, int s)
        {
            Color red = new Color(0.9f, 0.1f, 0.1f);
            Color green = new Color(0.2f, 0.7f, 0.2f);
            Color highlight = new Color(1f, 0.6f, 0.6f);

            // Two cherries side by side, slightly offset
            FillCircle(tex, s * 0.32f, s * 0.30f, s * 0.20f, red);      // left cherry
            FillCircle(tex, s * 0.65f, s * 0.28f, s * 0.20f, red);      // right cherry
            // Highlights
            FillCircle(tex, s * 0.27f, s * 0.36f, s * 0.06f, highlight);
            FillCircle(tex, s * 0.60f, s * 0.34f, s * 0.06f, highlight);
            // Stems converging to a point at top
            DrawLine(tex, (int)(s * 0.32f), (int)(s * 0.48f), (int)(s * 0.50f), (int)(s * 0.85f), green);
            DrawLine(tex, (int)(s * 0.65f), (int)(s * 0.46f), (int)(s * 0.50f), (int)(s * 0.85f), green);
            // Small leaf at the top
            FillCircle(tex, s * 0.55f, s * 0.82f, s * 0.06f, green);
        }

        /// <summary>Strawberry: Red triangle-ish shape with green leafy top and seed dots.</summary>
        private static void DrawStrawberry(Texture2D tex, int s)
        {
            Color red = new Color(0.9f, 0.15f, 0.15f);
            Color green = new Color(0.2f, 0.7f, 0.2f);
            Color seed = new Color(1f, 0.9f, 0.3f);

            // Strawberry body: wider at top, pointed at bottom
            float cx = s * 0.5f;
            for (int py = 0; py < s; py++)
            {
                float t = (py + 0.5f) / s; // 0=bottom, 1=top
                if (t < 0.10f || t > 0.78f) continue;
                // Width narrows toward bottom
                float widthFrac = Mathf.Lerp(0.02f, 0.42f, (t - 0.10f) / 0.68f);
                float halfW = s * widthFrac;
                for (int px = 0; px < s; px++)
                {
                    float dx = px + 0.5f - cx;
                    if (Mathf.Abs(dx) <= halfW)
                        tex.SetPixel(px, py, red);
                }
            }
            // Green leaves at top
            FillCircle(tex, cx - 3, s * 0.78f, 3f, green);
            FillCircle(tex, cx, s * 0.82f, 3f, green);
            FillCircle(tex, cx + 3, s * 0.78f, 3f, green);
            // Seed dots
            FillCircle(tex, s * 0.40f, s * 0.35f, 1f, seed);
            FillCircle(tex, s * 0.60f, s * 0.35f, 1f, seed);
            FillCircle(tex, s * 0.45f, s * 0.50f, 1f, seed);
            FillCircle(tex, s * 0.55f, s * 0.50f, 1f, seed);
            FillCircle(tex, s * 0.50f, s * 0.25f, 1f, seed);
        }

        /// <summary>Orange: Orange circle with green leaf and short stem.</summary>
        private static void DrawOrange(Texture2D tex, int s)
        {
            Color orange = new Color(1f, 0.6f, 0.1f);
            Color green = new Color(0.2f, 0.65f, 0.2f);
            Color highlight = new Color(1f, 0.85f, 0.5f);
            Color brown = new Color(0.4f, 0.25f, 0.1f);

            FillCircle(tex, s * 0.5f, s * 0.42f, s * 0.36f, orange);
            // Highlight
            FillCircle(tex, s * 0.40f, s * 0.52f, s * 0.10f, highlight);
            // Stem
            FillRect(tex, s / 2 - 1, (int)(s * 0.75f), s / 2, (int)(s * 0.85f), brown);
            // Leaf
            FillCircle(tex, s * 0.58f, s * 0.82f, s * 0.08f, green);
        }

        /// <summary>Apple: Green circle with stem and slight indent at top.</summary>
        private static void DrawApple(Texture2D tex, int s)
        {
            Color appleGreen = new Color(0.3f, 0.8f, 0.2f);
            Color highlight = new Color(0.6f, 1f, 0.5f);
            Color brown = new Color(0.4f, 0.25f, 0.1f);
            Color leafGreen = new Color(0.15f, 0.55f, 0.15f);

            // Main body -- slightly wider than tall
            FillCircle(tex, s * 0.5f, s * 0.40f, s * 0.37f, appleGreen);
            // Slight indent at top (clear a small area)
            FillCircle(tex, s * 0.5f, s * 0.80f, s * 0.08f, Color.clear);
            // Highlight
            FillCircle(tex, s * 0.38f, s * 0.50f, s * 0.09f, highlight);
            // Stem
            FillRect(tex, s / 2 - 1, (int)(s * 0.75f), s / 2, (int)(s * 0.90f), brown);
            // Leaf
            FillCircle(tex, s * 0.60f, s * 0.83f, s * 0.07f, leafGreen);
        }

        /// <summary>Grape: Purple cluster of small circles.</summary>
        private static void DrawGrape(Texture2D tex, int s)
        {
            Color purple = new Color(0.55f, 0.15f, 0.7f);
            Color lightPurple = new Color(0.7f, 0.35f, 0.85f);
            Color green = new Color(0.2f, 0.65f, 0.2f);

            float r = s * 0.11f;
            float cx = s * 0.5f;

            // Bottom row (3 grapes)
            FillCircle(tex, cx - r * 1.8f, s * 0.22f, r, purple);
            FillCircle(tex, cx, s * 0.22f, r, purple);
            FillCircle(tex, cx + r * 1.8f, s * 0.22f, r, purple);
            // Middle row (2 grapes)
            FillCircle(tex, cx - r * 0.9f, s * 0.40f, r, purple);
            FillCircle(tex, cx + r * 0.9f, s * 0.40f, r, purple);
            // Top row (1 grape)
            FillCircle(tex, cx, s * 0.56f, r, purple);

            // Highlights on each grape
            FillCircle(tex, cx - r * 1.8f + 1, s * 0.25f, r * 0.35f, lightPurple);
            FillCircle(tex, cx + 1, s * 0.25f, r * 0.35f, lightPurple);
            FillCircle(tex, cx + r * 1.8f + 1, s * 0.25f, r * 0.35f, lightPurple);

            // Stem
            DrawLine(tex, (int)cx, (int)(s * 0.62f), (int)cx, (int)(s * 0.80f), green);
            FillCircle(tex, cx + 2, s * 0.77f, s * 0.05f, green); // small leaf
        }

        /// <summary>Galaxian: Blue and yellow diamond shape (the Galaxian flagship).</summary>
        private static void DrawGalaxian(Texture2D tex, int s)
        {
            Color blue = new Color(0.1f, 0.3f, 0.9f);
            Color yellow = new Color(1f, 0.9f, 0.2f);
            Color red = new Color(0.9f, 0.2f, 0.2f);

            float cx = s * 0.5f;
            float cy = s * 0.5f;
            float halfH = s * 0.42f;
            float halfW = s * 0.32f;

            // Diamond body
            for (int py = 0; py < s; py++)
            {
                float dy = Mathf.Abs(py + 0.5f - cy);
                if (dy > halfH) continue;
                float wFrac = 1f - dy / halfH;
                float hw = halfW * wFrac;
                for (int px = 0; px < s; px++)
                {
                    float dx = Mathf.Abs(px + 0.5f - cx);
                    if (dx <= hw)
                    {
                        // Top half blue, bottom half yellow
                        Color c = (py + 0.5f >= cy) ? blue : yellow;
                        tex.SetPixel(px, py, c);
                    }
                }
            }
            // Red dot center
            FillCircle(tex, cx, cy, s * 0.06f, red);
            // Wing tips (small triangular extensions)
            FillCircle(tex, cx - halfW * 0.7f, cy, s * 0.05f, red);
            FillCircle(tex, cx + halfW * 0.7f, cy, s * 0.05f, red);
        }

        /// <summary>Bell: Gold bell shape (rounded top, flared bottom).</summary>
        private static void DrawBell(Texture2D tex, int s)
        {
            Color gold = new Color(1f, 0.82f, 0.1f);
            Color darkGold = new Color(0.8f, 0.6f, 0.05f);
            Color highlight = new Color(1f, 0.95f, 0.6f);

            float cx = s * 0.5f;

            // Bell body: narrow at top, wide at bottom
            for (int py = 0; py < s; py++)
            {
                float t = (py + 0.5f) / s; // 0=bottom, 1=top
                if (t < 0.08f || t > 0.82f) continue;

                float hw;
                if (t < 0.25f)
                {
                    // Flared bottom rim
                    hw = s * Mathf.Lerp(0.42f, 0.32f, (t - 0.08f) / 0.17f);
                }
                else if (t < 0.75f)
                {
                    // Bell body curves inward then outward
                    float bt = (t - 0.25f) / 0.50f;
                    hw = s * Mathf.Lerp(0.32f, 0.18f, bt * bt);
                }
                else
                {
                    // Rounded top
                    float tt = (t - 0.75f) / 0.07f;
                    hw = s * Mathf.Lerp(0.18f, 0.04f, Mathf.Clamp01(tt));
                }

                for (int px = 0; px < s; px++)
                {
                    float dx = Mathf.Abs(px + 0.5f - cx);
                    if (dx <= hw)
                        tex.SetPixel(px, py, gold);
                }
            }

            // Clapper (small circle at bottom center)
            FillCircle(tex, cx, s * 0.06f, s * 0.05f, darkGold);
            // Highlight streak
            FillCircle(tex, cx - s * 0.08f, s * 0.55f, s * 0.04f, highlight);
        }

        /// <summary>Key: Gray key shape (circular bow at top, rectangular shaft below).</summary>
        private static void DrawKey(Texture2D tex, int s)
        {
            Color gray = new Color(0.75f, 0.75f, 0.8f);
            Color highlight = new Color(0.9f, 0.9f, 0.95f);

            float cx = s * 0.5f;

            // Circular bow (ring at top)
            float bowCY = s * 0.72f;
            float bowR = s * 0.18f;
            float bowInnerR = s * 0.09f;
            for (int py = 0; py < s; py++)
                for (int px = 0; px < s; px++)
                {
                    float dx = px + 0.5f - cx;
                    float dy = py + 0.5f - bowCY;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist <= bowR && dist >= bowInnerR)
                        tex.SetPixel(px, py, gray);
                }

            // Shaft (vertical bar going down from bow)
            int shaftLeft = (int)(cx - s * 0.05f);
            int shaftRight = (int)(cx + s * 0.05f);
            int shaftTop = (int)(bowCY - bowR);
            int shaftBottom = (int)(s * 0.10f);
            FillRect(tex, shaftLeft, shaftBottom, shaftRight, shaftTop, gray);

            // Teeth (two small horizontal bars at bottom of shaft)
            int tooth1Y = (int)(s * 0.20f);
            int tooth2Y = (int)(s * 0.12f);
            FillRect(tex, shaftRight, tooth1Y - 1, shaftRight + (int)(s * 0.12f), tooth1Y + 1, gray);
            FillRect(tex, shaftRight, tooth2Y - 1, shaftRight + (int)(s * 0.08f), tooth2Y + 1, gray);

            // Highlight on bow
            FillCircle(tex, cx - s * 0.06f, bowCY + s * 0.06f, s * 0.04f, highlight);
        }

        private void OnDestroy()
        {
            if (_pelletManager != null)
                _pelletManager.OnPelletCollected -= OnPelletEaten;
        }
    }
}
