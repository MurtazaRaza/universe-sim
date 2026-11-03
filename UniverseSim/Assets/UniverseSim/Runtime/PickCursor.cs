using UnityEngine;

namespace UniverseSim.Unity
{
    /// <summary>
    /// The cursor over something a click picks, a body or its label (A-12, docs/ART_DIRECTION.md section 12): a ring
    /// with a dot, white with a dark outline so it reads on any background. Drawn by code, like the label icons, so it
    /// is no artistic asset. The system cursor comes back elsewhere.
    /// </summary>
    public static class PickCursor
    {
        public const int Size = 32;

        private static Texture2D texture;
        private static bool shown;

        /// <summary>The cursor's image (made once).</summary>
        public static Texture2D Texture => texture != null ? texture : texture = Draw();

        /// <summary>Whether the pick cursor is the one in use.</summary>
        public static bool Shown => shown;

        /// <summary>Shows the pick cursor, or the system cursor. Does nothing when it is already so.</summary>
        public static void Show(bool on)
        {
            if (on == shown)
            {
                return;
            }
            shown = on;
            if (on)
            {
                Cursor.SetCursor(Texture, new Vector2(Size / 2f, Size / 2f), CursorMode.Auto);
            }
            else
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            }
        }

        // A ring of radius 9 px (2 px wide) and a dot of radius 2 px, each in white with a 1.5 px dark edge, with
        // antialiased edges; transparent elsewhere.
        private static Texture2D Draw()
        {
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "Pick cursor",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[Size * Size];
            var center = (Size - 1) / 2f;
            for (var y = 0; y < Size; ++y)
            {
                for (var x = 0; x < Size; ++x)
                {
                    var r = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    // Distance from the white shapes: the ring band 8 to 10 px and the dot up to 2 px.
                    var fromWhite = Mathf.Min(Mathf.Max(Mathf.Abs(r - 9f) - 1f, 0f), Mathf.Max(r - 2f, 0f));
                    var white = Mathf.Clamp01(1f - fromWhite);
                    var outline = Mathf.Clamp01(2.5f - fromWhite) * (1f - white);
                    var alpha = white + outline * 0.75f;
                    var shade = alpha > 0f ? white / alpha : 0f;
                    var level = (byte)Mathf.RoundToInt(Mathf.Lerp(20f, 255f, shade));
                    pixels[y * Size + x] = new Color32(level, level, level, (byte)Mathf.RoundToInt(255f * alpha));
                }
            }
            image.SetPixels32(pixels);
            image.Apply(false, false);
            return image;
        }
    }
}
