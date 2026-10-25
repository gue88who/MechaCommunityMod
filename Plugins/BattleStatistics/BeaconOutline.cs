using GameRiver.Client;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace MechaCommunityMod.Plugins.BattleStatistics;

// Render the existing GPU meshes into a private stencil mask. Shift that mask
// in screen pixels and exclude the original silhouette to draw only a border.
internal sealed class BeaconOutline : IDisposable
{
    private static readonly List<BeaconOutline> Outlines = new();
    private static Camera? _camera;
    private static CommandBuffer? _commands;
    private static Material? _mask, _border, _fill;
    private static Mesh? _quad;
    private static RenderTexture? _target;
    private static GameObject? _display;
    private static RawImage? _image;
    private static readonly List<(Renderer Renderer, int Submeshes)> Drawable = new();
    private static readonly List<(Renderer Renderer, int Submeshes)> PreviousDrawable = new();
    private static Matrix4x4 _lastView, _lastProjection;
    private static bool _dirty = true;
    private readonly List<(Renderer Renderer, int Submeshes)> _renderers = new();
    private bool _visible;
    private static float _diagnosticAt;
    internal BeaconOutline(SceneActor actor)
    {
        EnsureMaterials();
        // Use the game's model membership, not all meshes under the actor.
        // Attached glow/shield meshes are descendants too; outlining them turns
        // the silhouette into a circle whenever those effects are active.
        var components = actor.GetModelRenderer()?.mainRenderers;
        var seen = new HashSet<IntPtr>();
        for (var index = 0; components != null && index < components.Count; index++)
        {
            var component = components[index];
            if (component == null) continue;
            var renderer = component.GetRenderer();
            if (renderer == null || !seen.Add(renderer.Pointer)) continue;
            var mesh = renderer.TryCast<SkinnedMeshRenderer>()?.sharedMesh
                ?? renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh != null) _renderers.Add((renderer, mesh.subMeshCount));
        }
        Outlines.Add(this);
        _dirty = true;
        if (_renderers.Count == 0) BattleStatisticsPlugin.Logger.LogWarning("Actor silhouette has no mesh renderers on model " + actor.name);
    }
    private static void EnsureMaterials()
    {
        if (_mask != null && _border != null && _fill != null && _quad != null) return;
        var shader = Shader.Find("UI/Default") ?? throw new InvalidOperationException("Stencil outline shader unavailable.");
        Material? mask = null, border = null, fill = null;
        Mesh? quad = null;
        try
        {
            mask = CreateMaterial(shader, CompareFunction.Always, StencilOp.Replace, 0);
            // Bit 0 is the unshifted silhouette; bit 1 is its expanded border.
            // Model vertices only write stencil, so their colors/alpha cannot tint it.
            mask.SetInt("_StencilWriteMask", 1);
            border = CreateMaterial(shader, CompareFunction.NotEqual, StencilOp.Replace, 0);
            border.SetInt("_Stencil", 3);
            border.SetInt("_StencilReadMask", 1);
            border.SetInt("_StencilWriteMask", 2);
            fill = CreateMaterial(shader, CompareFunction.Equal, StencilOp.Keep, 15);
            fill.SetInt("_Stencil", 2);
            fill.SetInt("_StencilReadMask", 2);
            fill.SetInt("_StencilWriteMask", 0);
            quad = new Mesh { name = "MechaBeaconBorderFill" };
            quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            UnityEngine.Object.DontDestroyOnLoad(quad);
        }
        catch
        {
            if (mask != null) UnityEngine.Object.Destroy(mask);
            if (border != null) UnityEngine.Object.Destroy(border);
            if (fill != null) UnityEngine.Object.Destroy(fill);
            if (quad != null) UnityEngine.Object.Destroy(quad);
            throw;
        }
        if (_mask != null) UnityEngine.Object.Destroy(_mask);
        if (_border != null) UnityEngine.Object.Destroy(_border);
        if (_fill != null) UnityEngine.Object.Destroy(_fill);
        if (_quad != null) UnityEngine.Object.Destroy(_quad);
        _mask = mask; _border = border; _fill = fill; _quad = quad;
    }
    private static Material CreateMaterial(Shader shader, CompareFunction compare, StencilOp operation, int colorMask)
    {
        var material = new Material(shader);
        material.SetInt("_Stencil", 1); material.SetInt("_StencilComp", (int)compare);
        material.SetInt("_StencilOp", (int)operation);
        material.SetInt("_StencilReadMask", 255); material.SetInt("_StencilWriteMask", 255);
        material.SetInt("_ColorMask", colorMask); material.SetColor("_Color", Color.white);
        material.SetTexture("_MainTex", Texture2D.whiteTexture);
        UnityEngine.Object.DontDestroyOnLoad(material);
        return material;
    }
    internal void Refresh(bool visible)
    {
        if (_visible != visible) { _visible = visible; _dirty = true; }
    }
    private static void Draw(CommandBuffer commands, Material material)
    {
        foreach (var (renderer, submeshes) in Drawable)
        {
            for (var submesh = 0; submesh < submeshes; submesh++) commands.DrawRenderer(renderer, material, submesh, 0);
        }
    }
    private static void EnsureDisplay()
    {
        if (_display != null) return;
        _display = new GameObject("MechaCommunityMod_BeaconOutlineDisplay");
        var canvas = _display.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 0;
        var child = new GameObject("SilhouetteBorder");
        var rect = child.AddComponent<RectTransform>(); rect.SetParent(_display.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _image = child.AddComponent<RawImage>();
        _image.raycastTarget = false; _image.color = Color.white;
        _image.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
        _image.texture = _target;
    }
    internal static void Render()
    {
        if (Outlines.Count == 0) { Detach(); return; }
        var camera = Camera.main;
        Drawable.Clear();
        foreach (var outline in Outlines)
            if (outline._visible)
                foreach (var entry in outline._renderers)
                    if (entry.Renderer != null && entry.Renderer.enabled && entry.Renderer.gameObject.activeInHierarchy && entry.Renderer.isVisible)
                        Drawable.Add(entry);
        if (Time.unscaledTime >= _diagnosticAt)
        {
            _diagnosticAt = Time.unscaledTime + 5;
            var selected = 0; var renderers = 0; var drawable = 0;
            foreach (var outline in Outlines)
            {
                if (!outline._visible) continue;
                selected++;
                foreach (var (renderer, _) in outline._renderers)
                {
                    renderers++;
                    if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.isVisible) drawable++;
                }
            }
            BattleStatisticsPlugin.Logger.LogInfo($"Outline render diagnostic: selected={selected}, renderers={renderers}, drawable={drawable}, camera={(camera == null ? "none" : camera.name)}, cameraEnabled={camera != null && camera.isActiveAndEnabled}, pipeline={(GraphicsSettings.currentRenderPipeline == null ? "built-in" : GraphicsSettings.currentRenderPipeline.name)}.");
        }
        if (Outlines.Count == 0 || camera == null)
        { Detach(); return; }
        if (_camera == null || _camera.Pointer != camera.Pointer)
        {
            Detach(); _camera = camera;
            _commands = new CommandBuffer { name = "Mecha Beacon Silhouette Outline" };
            camera.AddCommandBuffer(CameraEvent.AfterEverything, _commands);
            _dirty = true;
            BattleStatisticsPlugin.Logger.LogInfo("Visual helper outline commands attached to " + camera.name);
        }
        using var timing = new SlowOperation("unit outline rendering");
        var commands = _commands!;
        var width = Math.Max(1, camera.pixelWidth); var height = Math.Max(1, camera.pixelHeight);
        if (_target == null || _target.width != width || _target.height != height)
        {
            if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); }
            _target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            _target.Create();
            _dirty = true;
        }
        EnsureDisplay();
        _image!.texture = _target;
        var show = Drawable.Count > 0 && MatchSupport.Allowed && !OverlayUiLayer.Obscured();
        if (_display!.activeSelf != show) _display.SetActive(show);
        var view = camera.worldToCameraMatrix;
        var projection = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
        var changed = Drawable.Count != PreviousDrawable.Count;
        for (var index = 0; !changed && index < Drawable.Count; index++)
            changed = Drawable[index] != PreviousDrawable[index];
        if (!_dirty && !changed && view == _lastView && projection == _lastProjection) return;
        _dirty = false; _lastView = view; _lastProjection = projection;
        PreviousDrawable.Clear(); PreviousDrawable.AddRange(Drawable);
        commands.Clear();
        commands.SetRenderTarget(_target);
        commands.ClearRenderTarget(true, true, Color.clear);
        // UI/Default's depth mode is a global set by native UI rendering. It must
        // be explicit for our private target, then restored for subsequent draws.
        var priorDepth = Shader.GetGlobalFloat("unity_GUIZTestMode");
        commands.SetGlobalFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
        commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, projection);
        Draw(commands, _mask!);
        const float pixels = 3;
        for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0) continue;
                var offset = Matrix4x4.identity;
                offset.m03 = x * pixels * 2 / width; offset.m13 = y * pixels * 2 / height;
                commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, offset * projection);
                Draw(commands, _border!);
            }
        commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        commands.DrawMesh(_quad!, Matrix4x4.identity, _fill!, 0, 0);
        commands.SetViewProjectionMatrices(camera.worldToCameraMatrix, GL.GetGPUProjectionMatrix(camera.projectionMatrix, false));
        commands.SetGlobalFloat("unity_GUIZTestMode", priorDepth);
        commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
    }
    private static void Detach()
    {
        if (_camera != null && _commands != null) _camera.RemoveCommandBuffer(CameraEvent.AfterEverything, _commands);
        _commands?.Dispose(); _commands = null; _camera = null;
        if (_display != null) UnityEngine.Object.Destroy(_display);
        _display = null; _image = null; PreviousDrawable.Clear(); _dirty = true;
        if (_target != null) { _target.Release(); UnityEngine.Object.Destroy(_target); _target = null; }
    }
    public void Dispose()
    {
        Outlines.Remove(this); _renderers.Clear();
        _dirty = true;
        if (Outlines.Count == 0) Detach();
    }
}
