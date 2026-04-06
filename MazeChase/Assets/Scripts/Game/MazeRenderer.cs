using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MazeChase.Core;
using MazeChase.VFX;

namespace MazeChase.Game
{
    /// <summary>
    /// Reads MazeData and renders the maze using SpriteRenderer quads.
    /// Creates all tile visuals at runtime without requiring prefabs or imported sprites.
    /// Modern neon aesthetic with glowing walls, dark corridors, and pulsing energizers.
    /// </summary>
    public class MazeRenderer : MonoBehaviour
    {
        [Header("Tile Settings")]
        [SerializeField] private float tileSize = 0.5f;

        [Header("Colors")]
        [SerializeField] private Color wallColor = new Color(0.05f, 0.15f, 0.55f, 1.0f);
        [SerializeField] private Color wallEdgeColor = new Color(0.3f, 0.6f, 1.0f, 1.0f);
        [SerializeField] private Color corridorColor = new Color(0.01f, 0.01f, 0.03f, 1.0f);
        [SerializeField] private Color pelletColor = new Color(1f, 0.95f, 0.8f, 1.0f);
        [SerializeField] private Color energizerColor = new Color(1f, 0.85f, 0.2f, 1.0f);
        [SerializeField] private Color ghostHouseColor = new Color(0.03f, 0.03f, 0.1f, 1.0f);
        [SerializeField] private Color ghostDoorColor = new Color(1f, 0.3f, 0.6f, 1.0f);
        [SerializeField] private Color tunnelColor = new Color(0.01f, 0.01f, 0.03f, 0.3f);

        private Transform _mazeParent;
        private Dictionary<Vector2Int, GameObject> _pelletObjects = new Dictionary<Vector2Int, GameObject>();

        // Cached sprites created at runtime
        private Sprite _wallSprite;
        private Sprite _pelletSprite;
        private Sprite _energizerSprite;
        private Sprite _tunnelSprite;
        private Sprite _ghostHouseSprite;
        private Sprite _ghostDoorSprite;
        private Sprite _corridorSprite;

        // Offset to center the maze at world origin
        private Vector3 _offset;

        // Track wall renderers for flash effect
        private List<SpriteRenderer> _wallRenderers = new List<SpriteRenderer>();

        // Track energizer transforms for pulse animation
        private List<Transform> _energizerTransforms = new List<Transform>();
        private List<Transform> _energizerGlowTransforms = new List<Transform>();
        private Coroutine _energizerPulseCoroutine;

        // Cached glow sprite for wall halos
        private Sprite _glowSprite;

        private void Awake()
        {
            Init();
        }

        /// <summary>
        /// Initialize and render the entire maze.
        /// </summary>
        public void Init()
        {
            // Clean up previous maze if any
            if (_mazeParent != null)
            {
                Destroy(_mazeParent.gameObject);
                _pelletObjects.Clear();
                _wallRenderers.Clear();
                _energizerTransforms.Clear();
                _energizerGlowTransforms.Clear();
            }

            if (_energizerPulseCoroutine != null)
            {
                StopCoroutine(_energizerPulseCoroutine);
                _energizerPulseCoroutine = null;
            }

            // Calculate offset to center the maze at world origin
            _offset = new Vector3(
                -(MazeData.Width * tileSize) / 2f + tileSize / 2f,
                (MazeData.Height * tileSize) / 2f - tileSize / 2f,
                0f
            );

            if (RuntimeExecutionMode.SuppressPresentation)
                return;

            // Create runtime sprites
            CreateAllSprites();

            // Create parent object
            GameObject mazeGo = new GameObject("Maze");
            _mazeParent = mazeGo.transform;

            // Create dark background quad behind the entire maze area
            CreateMazeBackground();

            // Render each tile
            for (int y = 0; y < MazeData.Height; y++)
            {
                for (int x = 0; x < MazeData.Width; x++)
                {
                    MazeTile tile = MazeData.GetTile(x, y);
                    RenderTile(x, y, tile);
                }
            }

            // Start the energizer pulse coroutine
            _energizerPulseCoroutine = StartCoroutine(EnergizerPulseCoroutine());
        }

        /// <summary>
        /// Creates a dark background quad that covers the entire maze area.
        /// </summary>
        private void CreateMazeBackground()
        {
            GameObject bgGo = new GameObject("MazeBackground");
            bgGo.transform.parent = _mazeParent;

            // Position at center of the maze
            float centerX = (MazeData.Width * tileSize) / 2f - tileSize / 2f + _offset.x;
            float centerY = -(MazeData.Height * tileSize) / 2f + tileSize / 2f + _offset.y;
            bgGo.transform.position = new Vector3(centerX, centerY, 0.1f);

            SpriteRenderer sr = bgGo.AddComponent<SpriteRenderer>();

            // Create a 1x1 white texture and scale it to cover the maze
            Texture2D tex = new Texture2D(4, 4);
            tex.filterMode = FilterMode.Point;
            Color[] pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();

            sr.sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4);
            sr.color = new Color(0.01f, 0.01f, 0.03f, 1.0f);
            sr.sortingOrder = -2;

            // Scale to cover the entire maze with some padding
            float scaleX = MazeData.Width * tileSize + tileSize;
            float scaleY = MazeData.Height * tileSize + tileSize;
            bgGo.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        /// <summary>
        /// Creates all sprite assets used by the maze at runtime.
        /// </summary>
        private void CreateAllSprites()
        {
            // All sprites must render at exactly tileSize world units.
            // PPU = pixelWidth / desiredWorldSize.
            int wallPx = 32;
            float wallScale = 0.95f; // slightly smaller than tile for gap effect
            float wallWorldSize = tileSize * wallScale;
            float wallPPU = wallPx / wallWorldSize;

            int pelletPx = 12;
            float pelletWorldSize = tileSize * 0.30f; // 30% of tile size
            float pelletPPU = pelletPx / pelletWorldSize;

            int energizerPx = 16;
            float energizerWorldSize = tileSize * 0.6f;
            float energizerPPU = energizerPx / energizerWorldSize;

            int doorPx = 32;
            int doorHeight = 6;

            int corridorPx = 16;
            float corridorPPU = corridorPx / tileSize;

            _wallSprite = CreateRoundedRectSprite(wallPx, wallPx, wallPPU, 6);
            _pelletSprite = CreateGlowCircleSprite(pelletPx, pelletPPU);
            _energizerSprite = CreateGlowCircleSprite(energizerPx, energizerPPU);
            _tunnelSprite = CreateSpriteWithPPU(Color.white, corridorPx, corridorPx, corridorPPU);
            _ghostHouseSprite = CreateSpriteWithPPU(Color.white, corridorPx, corridorPx, corridorPPU);
            _ghostDoorSprite = CreateRoundedRectSprite(doorPx, doorHeight, wallPPU, 2);
            _corridorSprite = CreateSpriteWithPPU(Color.white, corridorPx, corridorPx, corridorPPU);
        }

        /// <summary>
        /// Renders a single tile at grid position (x, y).
        /// </summary>
        private void RenderTile(int x, int y, MazeTile tile)
        {
            Vector3 worldPos = TileToWorld(x, y);
            Vector2Int gridPos = new Vector2Int(x, y);

            // Render corridor background for non-wall tiles
            if (tile != MazeTile.Wall)
            {
                CreateTileObject("Corridor", worldPos, _corridorSprite, corridorColor, -1, gridPos, false);
            }

            switch (tile)
            {
                case MazeTile.Wall:
                    // 2.5D shadow: dark offset copy behind the wall
                    Vector3 shadowOffset = new Vector3(0.02f, -0.02f, 0f);
                    CreateTileObject("WallShadow", worldPos + shadowOffset, _wallSprite,
                        new Color(0f, 0f, 0.05f, 0.6f), -1, gridPos, false);

                    // Bloom-like glow halo behind wall at 110% scale
                    GameObject glowGo = CreateTileObject("WallGlow", worldPos, _wallSprite,
                        new Color(0.15f, 0.4f, 1.0f, 0.4f), -1, gridPos, false);
                    glowGo.transform.localScale = new Vector3(1.25f, 1.25f, 1f);

                    // Main wall tile
                    CreateTileObject("Wall", worldPos, _wallSprite, wallColor, 0, gridPos, true);
                    break;

                case MazeTile.Pellet:
                    GameObject pelletGo = CreateTileObject("Pellet", worldPos, _pelletSprite, pelletColor, 2, gridPos, false);
                    _pelletObjects[gridPos] = pelletGo;
                    break;

                case MazeTile.Energizer:
                    // Soft glow circle behind energizer at 200% scale
                    GameObject eGlowGo = CreateTileObject("EnergizerGlow", worldPos, _energizerSprite,
                        new Color(1f, 0.8f, 0.2f, 0.15f), 1, gridPos, false);
                    eGlowGo.transform.localScale = new Vector3(2f, 2f, 1f);
                    _energizerGlowTransforms.Add(eGlowGo.transform);

                    // Main energizer
                    GameObject energizerGo = CreateTileObject("Energizer", worldPos, _energizerSprite, energizerColor, 3, gridPos, false);
                    _pelletObjects[gridPos] = energizerGo;
                    _energizerTransforms.Add(energizerGo.transform);
                    break;

                case MazeTile.Tunnel:
                    CreateTileObject("Tunnel", worldPos, _tunnelSprite, tunnelColor, 0, gridPos, false);
                    break;

                case MazeTile.GhostHouse:
                    CreateTileObject("GhostHouse", worldPos, _ghostHouseSprite, ghostHouseColor, 0, gridPos, false);
                    break;

                case MazeTile.GhostDoor:
                    CreateTileObject("GhostDoor", worldPos, _ghostDoorSprite, ghostDoorColor, 4, gridPos, false);
                    break;

                // Empty, PlayerSpawn, FruitSpawn, GhostSpawn: no visual tile (corridor bg already placed)
                default:
                    break;
            }
        }

        /// <summary>
        /// Creates a tile GameObject with a SpriteRenderer.
        /// </summary>
        private GameObject CreateTileObject(string name, Vector3 position, Sprite sprite, Color color, int sortOrder, Vector2Int gridPos, bool isWall)
        {
            GameObject go = new GameObject($"{name}_{gridPos.x}_{gridPos.y}");
            go.transform.parent = _mazeParent;
            go.transform.position = position;

            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = sortOrder;

            if (isWall)
            {
                _wallRenderers.Add(sr);
            }

            return go;
        }

        /// <summary>
        /// Creates a rectangular Texture2D sprite at runtime.
        /// </summary>
        public static Sprite CreateSprite(Color color, int width, int height)
        {
            return CreateSpriteWithPPU(color, width, height, width);
        }

        private static Sprite CreateSpriteWithPPU(Color color, int width, int height, float ppu)
        {
            Texture2D tex = new Texture2D(width, height);
            tex.filterMode = FilterMode.Point;
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), ppu);
        }

        /// <summary>
        /// Creates a rounded rectangle sprite with a bright edge for a neon glow look.
        /// </summary>
        private Sprite CreateRoundedRectSprite(int width, int height, float ppu, int cornerRadius)
        {
            Texture2D tex = new Texture2D(width, height);
            tex.filterMode = FilterMode.Bilinear;

            for (int py = 0; py < height; py++)
            {
                for (int px = 0; px < width; px++)
                {
                    // Check if pixel is inside the rounded rectangle
                    bool inside = IsInsideRoundedRect(px, py, width, height, cornerRadius);

                    if (!inside)
                    {
                        tex.SetPixel(px, py, Color.clear);
                        continue;
                    }

                    // Calculate distance from edge for glow effect
                    float edgeDist = DistanceFromEdgeRoundedRect(px, py, width, height, cornerRadius);

                    if (edgeDist <= 2.0f)
                    {
                        // Bright edge (neon glow border)
                        float t = edgeDist / 2.0f;
                        Color edgeCol = Color.Lerp(wallEdgeColor, Color.white, 0.5f);
                        tex.SetPixel(px, py, edgeCol);
                    }
                    else
                    {
                        // Interior body color
                        tex.SetPixel(px, py, Color.white);
                    }
                }
            }

            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), ppu);
        }

        private bool IsInsideRoundedRect(int px, int py, int w, int h, int r)
        {
            // Check the four corners
            if (px < r && py < r)
                return (px - r) * (px - r) + (py - r) * (py - r) <= r * r;
            if (px >= w - r && py < r)
                return (px - (w - r - 1)) * (px - (w - r - 1)) + (py - r) * (py - r) <= r * r;
            if (px < r && py >= h - r)
                return (px - r) * (px - r) + (py - (h - r - 1)) * (py - (h - r - 1)) <= r * r;
            if (px >= w - r && py >= h - r)
                return (px - (w - r - 1)) * (px - (w - r - 1)) + (py - (h - r - 1)) * (py - (h - r - 1)) <= r * r;

            // Inside the main body
            return px >= 0 && px < w && py >= 0 && py < h;
        }

        private float DistanceFromEdgeRoundedRect(int px, int py, int w, int h, int r)
        {
            // Approximate distance from the edge of the rounded rectangle
            float dx = Mathf.Min(px, w - 1 - px);
            float dy = Mathf.Min(py, h - 1 - py);

            // In corner regions, compute from the corner circle
            if (px < r && py < r)
            {
                float cx = r;
                float cy = r;
                float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return r - dist;
            }
            if (px >= w - r && py < r)
            {
                float cx = w - r - 1;
                float cy = r;
                float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return r - dist;
            }
            if (px < r && py >= h - r)
            {
                float cx = r;
                float cy = h - r - 1;
                float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return r - dist;
            }
            if (px >= w - r && py >= h - r)
            {
                float cx = w - r - 1;
                float cy = h - r - 1;
                float dist = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                return r - dist;
            }

            return Mathf.Min(dx, dy);
        }

        /// <summary>
        /// Creates a circle sprite with a soft glow edge (anti-aliased with falloff).
        /// </summary>
        private static Sprite CreateGlowCircleSprite(int diameter, float ppu)
        {
            Texture2D tex = new Texture2D(diameter, diameter);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[diameter * diameter];
            float radius = diameter / 2f;
            float innerRadius = radius * 0.6f;

            for (int py = 0; py < diameter; py++)
            {
                for (int px = 0; px < diameter; px++)
                {
                    float dx = px - radius + 0.5f;
                    float dy = py - radius + 0.5f;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    if (dist <= innerRadius)
                    {
                        // Solid bright center
                        pixels[py * diameter + px] = Color.white;
                    }
                    else if (dist <= radius)
                    {
                        // Soft glow falloff
                        float t = (dist - innerRadius) / (radius - innerRadius);
                        float alpha = 1f - t * t;
                        pixels[py * diameter + px] = new Color(1f, 1f, 1f, alpha);
                    }
                    else
                    {
                        pixels[py * diameter + px] = Color.clear;
                    }
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), ppu);
        }

        private static Sprite CreateCircleSpriteWithPPU(Color color, int diameter, float ppu)
        {
            Texture2D tex = new Texture2D(diameter, diameter);
            tex.filterMode = FilterMode.Bilinear;
            Color[] pixels = new Color[diameter * diameter];
            float radius = diameter / 2f;
            float radiusSq = radius * radius;
            for (int py = 0; py < diameter; py++)
            {
                for (int px = 0; px < diameter; px++)
                {
                    float dx = px - radius + 0.5f;
                    float dy = py - radius + 0.5f;
                    pixels[py * diameter + px] = (dx * dx + dy * dy <= radiusSq) ? color : Color.clear;
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, diameter, diameter), new Vector2(0.5f, 0.5f), ppu);
        }

        /// <summary>
        /// Coroutine that continuously pulses energizer sprites up and down in scale.
        /// </summary>
        private IEnumerator EnergizerPulseCoroutine()
        {
            float timer = 0f;
            while (true)
            {
                timer += Time.deltaTime * 3f;
                float scale = 1f + 0.2f * Mathf.Sin(timer * Mathf.PI);
                float glowScale = 2f + 0.4f * Mathf.Sin(timer * Mathf.PI);

                for (int i = _energizerTransforms.Count - 1; i >= 0; i--)
                {
                    if (_energizerTransforms[i] == null)
                    {
                        _energizerTransforms.RemoveAt(i);
                        continue;
                    }
                    _energizerTransforms[i].localScale = new Vector3(scale, scale, 1f);
                }

                // Pulse the energizer glow halos in sync
                for (int i = _energizerGlowTransforms.Count - 1; i >= 0; i--)
                {
                    if (_energizerGlowTransforms[i] == null)
                    {
                        _energizerGlowTransforms.RemoveAt(i);
                        continue;
                    }
                    _energizerGlowTransforms[i].localScale = new Vector3(glowScale, glowScale, 1f);
                }

                yield return null;
            }
        }

        /// <summary>
        /// Converts grid coordinates (x, y) to world position.
        /// The maze is centered so that the middle of the grid is near world origin.
        /// Y is inverted: row 0 is at the top (positive Y), row 30 at the bottom.
        /// </summary>
        public Vector3 TileToWorld(int x, int y)
        {
            return new Vector3(
                x * tileSize + _offset.x,
                -y * tileSize + _offset.y,
                0f
            );
        }

        /// <summary>Convenience overload accepting Vector2Int.</summary>
        public Vector3 TileToWorld(Vector2Int tile) => TileToWorld(tile.x, tile.y);

        /// <summary>
        /// Converts a world position to the nearest grid coordinates.
        /// </summary>
        public Vector2Int WorldToTile(Vector3 worldPos)
        {
            int x = Mathf.RoundToInt((worldPos.x - _offset.x) / tileSize);
            int y = Mathf.RoundToInt((-worldPos.y + _offset.y) / tileSize);
            return new Vector2Int(
                Mathf.Clamp(x, 0, MazeData.Width - 1),
                Mathf.Clamp(y, 0, MazeData.Height - 1)
            );
        }

        /// <summary>
        /// Removes the pellet or energizer visual at grid position (x, y).
        /// Returns true if a pellet was found and removed.
        /// </summary>
        public bool RemovePellet(int x, int y)
        {
            if (RuntimeExecutionMode.SuppressPresentation)
                return true;

            Vector2Int key = new Vector2Int(x, y);
            if (_pelletObjects.TryGetValue(key, out GameObject pelletGo))
            {
                // Remove from energizer list if applicable
                if (pelletGo != null)
                {
                    // Spawn particle burst at the pellet position
                    Vector3 pos = pelletGo.transform.position;
                    bool isEnergizer = _energizerTransforms.Contains(pelletGo.transform);
                    if (isEnergizer)
                    {
                        SimpleParticles.SpawnBurst(pos, energizerColor, 8, 0.4f, 2.5f);
                    }
                    else
                    {
                        SimpleParticles.SpawnBurst(pos, pelletColor, 5, 0.3f, 2f);
                    }

                    _energizerTransforms.Remove(pelletGo.transform);
                }
                Destroy(pelletGo);
                _pelletObjects.Remove(key);
                return true;
            }
            return false;
        }

        /// <summary>Convenience overload accepting Vector2Int.</summary>
        public bool RemovePellet(Vector2Int tile) => RemovePellet(tile.x, tile.y);

        /// <summary>
        /// Destroys and rebuilds the entire maze visual. Used at round start.
        /// </summary>
        public void RebuildMaze()
        {
            Init();
        }

        /// <summary>
        /// Coroutine that flashes the maze walls between white and the original wall color.
        /// Used when a round is cleared (all pellets eaten).
        /// </summary>
        public Coroutine FlashMaze()
        {
            if (RuntimeExecutionMode.SuppressPresentation)
                return null;

            return StartCoroutine(FlashMazeCoroutine());
        }

        private IEnumerator FlashMazeCoroutine()
        {
            int flashCount = 4;
            float flashDuration = 0.15f;

            for (int i = 0; i < flashCount; i++)
            {
                // Flash to bright cyan/white
                SetWallColor(new Color(0.6f, 0.9f, 1.0f));
                yield return new WaitForSeconds(flashDuration);

                // Flash back to original
                SetWallColor(wallColor);
                yield return new WaitForSeconds(flashDuration);
            }
        }

        /// <summary>
        /// Sets all wall renderers to the specified color.
        /// </summary>
        private void SetWallColor(Color color)
        {
            foreach (SpriteRenderer sr in _wallRenderers)
            {
                if (sr != null)
                {
                    sr.color = color;
                }
            }
        }

        /// <summary>
        /// Returns the configured tile size in world units.
        /// </summary>
        public float TileSize => tileSize;

        /// <summary>
        /// Returns the number of remaining pellets/energizers on the board.
        /// </summary>
        public int RemainingPellets => _pelletObjects.Count;
    }
}
