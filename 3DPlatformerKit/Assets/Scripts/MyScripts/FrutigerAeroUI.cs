using UnityEngine;

/// <summary>
/// Shared Frutiger Aero drawing helpers for Golf is Hard's runtime menus and HUD.
/// Uses lightweight IMGUI textures so existing menu behavior remains intact.
/// </summary>
public static class FrutigerAeroUI
{
    public static readonly Color Ink = new Color(0.035f, 0.20f, 0.29f, 1f);
    public static readonly Color Blue = new Color(0.08f, 0.57f, 0.86f, 1f);
    public static readonly Color Cyan = new Color(0.18f, 0.78f, 0.96f, 1f);
    public static readonly Color Aqua = new Color(0.22f, 0.89f, 0.92f, 1f);
    public static readonly Color Green = new Color(0.22f, 0.72f, 0.40f, 1f);
    public static readonly Color Lime = new Color(0.72f, 0.91f, 0.30f, 1f);
    public static readonly Color Coral = new Color(0.97f, 0.31f, 0.34f, 1f);
    public static readonly Color Glass = new Color(0.84f, 0.97f, 1f, 0.91f);

    private static Texture2D roundedTexture;
    private static Texture2D pillTexture;
    private static Texture2D circleTexture;
    private static Texture2D skyTexture;
    private static int lastHoveredGlassButtonId = int.MinValue;

    public static void ResetGlassButtonHover()
    {
        lastHoveredGlassButtonId = int.MinValue;
    }

    private static void EnsureTextures()
    {
        if (roundedTexture != null)
            return;

        roundedTexture = CreateRoundedTexture(256, 256, 0.12f, "Aero Rounded Glass");
        pillTexture = CreateRoundedTexture(512, 128, 0.49f, "Aero Capsule");
        circleTexture = CreateCircleTexture(128, "Aero Bubble");
        skyTexture = CreateSkyTexture(2, 256);
    }

    public static GUIStyle CreateStyle(int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleCenter, bool bold = true)
    {
        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            alignment = alignment,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            wordWrap = true,
            richText = false
        };
        style.normal.textColor = color;
        style.hover.textColor = color;
        style.active.textColor = color;
        return style;
    }

    public static void DrawSkyBackdrop()
    {
        EnsureTextures();
        DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), skyTexture, Color.white);

        float w = Screen.width;
        float h = Screen.height;

        // Soft sunny sky and a pair of rolling green hills.
        DrawCircle(new Rect(-w * 0.16f, h * 0.49f, w * 0.82f, h * 0.88f), new Color(0.69f, 0.94f, 0.54f, 1f));
        DrawCircle(new Rect(w * 0.36f, h * 0.46f, w * 0.88f, h * 0.91f), new Color(0.48f, 0.86f, 0.44f, 1f));
        DrawPill(new Rect(0f, h * 0.78f, w, h * 0.23f), new Color(0.22f, 0.78f, 0.83f, 0.22f));

        DrawCircle(new Rect(w * 0.79f, h * 0.07f, 96f, 96f), new Color(1f, 0.92f, 0.54f, 0.2f));
        DrawCircle(new Rect(w * 0.805f, h * 0.085f, 66f, 66f), new Color(1f, 0.96f, 0.71f, 1f));
        DrawCircle(new Rect(w * 0.82f, h * 0.10f, 20f, 20f), new Color(1f, 1f, 1f, 0.84f));

        DrawCloud(w * 0.09f, h * 0.17f, 1f);
        DrawCloud(w * 0.58f, h * 0.12f, 0.72f);
        DrawCloud(w * 0.9f, h * 0.32f, 0.62f);
        DrawBubble(w * 0.72f, h * 0.43f, 38f);
        DrawBubble(w * 0.16f, h * 0.41f, 22f);
        DrawBubble(w * 0.91f, h * 0.55f, 28f);
    }

    public static void DrawPauseBackdrop()
    {
        EnsureTextures();
        DrawSolid(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0.025f, 0.20f, 0.34f, 0.47f));
        DrawCircle(new Rect(-100f, Screen.height * 0.42f, 310f, 310f), new Color(0.22f, 0.86f, 0.94f, 0.16f));
        DrawCircle(new Rect(Screen.width - 250f, -135f, 330f, 330f), new Color(0.32f, 0.91f, 1f, 0.16f));
        DrawCircle(new Rect(Screen.width - 230f, Screen.height - 175f, 260f, 260f), new Color(0.54f, 0.92f, 0.48f, 0.15f));
        DrawBubble(Screen.width * 0.20f, Screen.height * 0.22f, 26f);
        DrawBubble(Screen.width * 0.82f, Screen.height * 0.72f, 38f);
    }

    public static void DrawGlassPanel(Rect rect, Color accent, Color glass, float shadowAlpha = 0.2f)
    {
        EnsureTextures();

        DrawRounded(new Rect(rect.x + 1f, rect.y + 7f, rect.width, rect.height),
            new Color(0.025f, 0.16f, 0.28f, shadowAlpha));
        DrawRounded(rect, new Color(accent.r, accent.g, accent.b, 0.96f));

        Rect inside = Inset(rect, 3f);
        DrawRounded(inside, glass);

        float shineHeight = Mathf.Clamp(rect.height * 0.14f, 10f, 34f);
        Rect shine = new Rect(inside.x + 7f, inside.y + 5f, inside.width - 14f, shineHeight);
        DrawPill(shine, new Color(1f, 1f, 1f, 0.62f));

        Rect lowerEdge = new Rect(inside.x + 11f, inside.yMax - 8f, inside.width - 22f, 3f);
        DrawPill(lowerEdge, new Color(accent.r, accent.g, accent.b, 0.34f));
    }

    public static bool DrawGlassButton(
        Rect rect,
        string label,
        GUIStyle style,
        Color accent,
        bool primary = false,
        AudioClip hoverSound = null,
        AudioSource hoverAudioSource = null,
        float hoverSoundVolume = 0.45f)
    {
        EnsureTextures();

        bool hovered = rect.Contains(Event.current.mousePosition);
        EventType eventType = Event.current.type;
        bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
        int buttonId = (label != null ? label.GetHashCode() : 0) ^ rect.GetHashCode();
        if (hovered)
        {
            if (lastHoveredGlassButtonId != buttonId)
            {
                lastHoveredGlassButtonId = buttonId;
                bool isPointerMove = eventType != EventType.MouseDown && eventType != EventType.MouseUp;
                if (!clicked && isPointerMove && hoverSound != null && hoverAudioSource != null)
                    hoverAudioSource.PlayOneShot(hoverSound, Mathf.Clamp01(hoverSoundVolume));
            }
        }
        else if (lastHoveredGlassButtonId == buttonId)
        {
            lastHoveredGlassButtonId = int.MinValue;
        }

        float lift = hovered ? 2f : 0f;
        Rect drawRect = new Rect(rect.x, rect.y - lift, rect.width, rect.height);

        DrawPill(new Rect(drawRect.x + 1f, drawRect.y + 5f, drawRect.width, drawRect.height),
            new Color(0.02f, 0.16f, 0.28f, 0.23f));
        Color border = hovered ? Color.Lerp(accent, Color.white, 0.2f) : accent;
        DrawPill(drawRect, new Color(border.r, border.g, border.b, 0.98f));
        Rect inside = Inset(drawRect, 3f);
        DrawPill(inside, primary ? new Color(0.78f, 0.98f, 1f, 0.97f) : new Color(0.92f, 0.99f, 1f, 0.94f));
        DrawPill(new Rect(inside.x + 7f, inside.y + 5f, inside.width - 14f, Mathf.Max(8f, inside.height * 0.34f)),
            new Color(1f, 1f, 1f, hovered ? 0.78f : 0.64f));
        DrawPill(new Rect(inside.x + 12f, inside.yMax - 8f, inside.width - 24f, 3f),
            new Color(accent.r, accent.g, accent.b, 0.38f));

        if (style != null)
            GUI.Label(drawRect, label, style);

        return clicked;
    }

    public static void DrawProgressTrack(Rect rect, float progress, Color fill, bool intense = false)
    {
        EnsureTextures();
        DrawPill(rect, new Color(0.05f, 0.29f, 0.43f, 0.55f));
        Rect inset = Inset(rect, 3f);
        DrawPill(inset, new Color(0.10f, 0.43f, 0.58f, 0.28f));

        float fillWidth = inset.width * Mathf.Clamp01(progress);
        if (fillWidth <= 0f)
            return;

        Color actualFill = intense ? Color.Lerp(fill, new Color(1f, 0.29f, 0.29f, 1f), 0.25f) : fill;
        Rect fillRect = new Rect(inset.x, inset.y, fillWidth, inset.height);
        DrawPill(fillRect, actualFill);

        float shineWidth = Mathf.Min(fillRect.width - 4f, Mathf.Max(0f, 68f));
        if (shineWidth > 4f)
        {
            float travel = Mathf.Max(1f, fillRect.width - shineWidth - 8f);
            float shineX = fillRect.x + 4f + Mathf.Repeat(Time.unscaledTime * 46f, travel);
            DrawPill(new Rect(shineX, fillRect.y + 3f, shineWidth, Mathf.Max(3f, fillRect.height * 0.28f)),
                new Color(1f, 1f, 1f, 0.6f));
        }
    }

    public static void DrawRounded(Rect rect, Color color)
    {
        EnsureTextures();
        DrawTexture(rect, roundedTexture, color);
    }

    public static void DrawPill(Rect rect, Color color)
    {
        EnsureTextures();
        DrawTexture(rect, pillTexture, color);
    }

    public static void DrawCircle(Rect rect, Color color)
    {
        EnsureTextures();
        DrawTexture(rect, circleTexture, color);
    }

    public static void DrawGolfBall(Vector2 center, float size)
    {
        DrawCircle(new Rect(center.x - size * 0.5f + 2f, center.y - size * 0.5f + 4f, size, size),
            new Color(0.04f, 0.30f, 0.43f, 0.2f));
        DrawCircle(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size),
            new Color(1f, 1f, 0.94f, 1f));
        DrawCircle(new Rect(center.x - size * 0.22f, center.y - size * 0.25f, size * 0.48f, size * 0.24f),
            new Color(1f, 1f, 1f, 0.74f));
        float dimple = size * 0.085f;
        DrawCircle(new Rect(center.x - size * 0.19f, center.y + size * 0.06f, dimple, dimple),
            new Color(0.25f, 0.62f, 0.74f, 0.3f));
        DrawCircle(new Rect(center.x + size * 0.06f, center.y + size * 0.13f, dimple, dimple),
            new Color(0.25f, 0.62f, 0.74f, 0.3f));
        DrawCircle(new Rect(center.x + size * 0.15f, center.y - size * 0.08f, dimple, dimple),
            new Color(0.25f, 0.62f, 0.74f, 0.3f));
    }

    public static void DrawSolid(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    private static void DrawCloud(float x, float y, float scale)
    {
        float unit = 44f * scale;
        Color cloud = new Color(1f, 1f, 1f, 0.68f);
        DrawPill(new Rect(x, y + unit * 0.42f, unit * 2.25f, unit * 0.62f), cloud);
        DrawCircle(new Rect(x + unit * 0.18f, y + unit * 0.12f, unit * 0.94f, unit * 0.94f), cloud);
        DrawCircle(new Rect(x + unit * 0.78f, y, unit * 1.08f, unit * 1.08f), cloud);
        DrawCircle(new Rect(x + unit * 1.42f, y + unit * 0.2f, unit * 0.84f, unit * 0.84f), cloud);
    }

    private static void DrawBubble(float x, float y, float size)
    {
        DrawCircle(new Rect(x, y, size, size), new Color(0.16f, 0.72f, 0.9f, 0.55f));
        DrawCircle(new Rect(x + 2f, y + 2f, size - 4f, size - 4f), new Color(0.84f, 0.98f, 1f, 0.48f));
        DrawCircle(new Rect(x + size * 0.2f, y + size * 0.17f, size * 0.24f, size * 0.14f),
            new Color(1f, 1f, 1f, 0.88f));
    }

    private static void DrawTexture(Rect rect, Texture2D texture, Color tint)
    {
        if (texture == null)
            return;

        Color old = GUI.color;
        GUI.color = tint;
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        GUI.color = old;
    }

    private static Rect Inset(Rect rect, float amount)
    {
        return new Rect(rect.x + amount, rect.y + amount,
            Mathf.Max(1f, rect.width - amount * 2f), Mathf.Max(1f, rect.height - amount * 2f));
    }

    private static Texture2D CreateSkyTexture(int width, int height)
    {
        Texture2D texture = CreateTexture(width, height, "Aero Sky Gradient");
        Color[] pixels = new Color[width * height];
        Color horizon = new Color(0.91f, 0.99f, 1f, 1f);
        Color zenith = new Color(0.20f, 0.70f, 0.94f, 1f);
        for (int y = 0; y < height; y++)
        {
            float t = y / (float)(height - 1);
            Color color = Color.Lerp(horizon, zenith, t);
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = color;
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateRoundedTexture(int width, int height, float radiusFraction, string textureName)
    {
        Texture2D texture = CreateTexture(width, height, textureName);
        const int samples = 4;
        float radius = Mathf.Min(width, height) * radiusFraction;
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int covered = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                float px = x + (sx + 0.5f) / samples;
                float py = y + (sy + 0.5f) / samples;
                float dx = Mathf.Abs(px - width * 0.5f) - (width * 0.5f - radius);
                float dy = Mathf.Abs(py - height * 0.5f) - (height * 0.5f - radius);
                float outsideX = Mathf.Max(0f, dx);
                float outsideY = Mathf.Max(0f, dy);
                float distance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY)
                    + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;
                if (distance <= 0f)
                    covered++;
            }
            pixels[y * width + x] = new Color(1f, 1f, 1f, covered / (float)(samples * samples));
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateCircleTexture(int size, string textureName)
    {
        Texture2D texture = CreateTexture(size, size, textureName);
        const int samples = 4;
        float radius = size * 0.5f;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int covered = 0;
            for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
            {
                float px = x + (sx + 0.5f) / samples - radius;
                float py = y + (sy + 0.5f) / samples - radius;
                if (px * px + py * py <= radius * radius)
                    covered++;
            }
            pixels[y * size + x] = new Color(1f, 1f, 1f, covered / (float)(samples * samples));
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private static Texture2D CreateTexture(int width, int height, string textureName)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = textureName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
    }
}

