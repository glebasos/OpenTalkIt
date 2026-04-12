using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace OpenTalkIt.Views.Controls;

public class CloudShaderControl : Control
{
    private CompositionCustomVisual? _customVisual;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        if (compositor == null) return;

        _customVisual = compositor.CreateCustomVisual(new CloudShaderVisualHandler());
        ElementComposition.SetElementChildVisual(this, _customVisual);
        _customVisual.SendHandlerMessage(CloudShaderVisualHandler.Start.Instance);
        _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _customVisual?.SendHandlerMessage(CloudShaderVisualHandler.Stop.Instance);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_customVisual != null)
            _customVisual.Size = new Vector(e.NewSize.Width, e.NewSize.Height);
    }
}

internal class CloudShaderVisualHandler : CompositionCustomVisualHandler
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private SKRuntimeEffect? _effect;
    private bool _running;

    public sealed record Start { public static readonly Start Instance = new(); }
    public sealed record Stop  { public static readonly Stop  Instance = new(); }

    public override void OnMessage(object message)
    {
        if (message is Start)
        {
            _running = true;
            RegisterForNextAnimationFrameUpdate();
        }
        else if (message is Stop)
        {
            _running = false;
        }
    }

    public override void OnAnimationFrameUpdate()
    {
        if (!_running) return;
        Invalidate();
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        var bounds = GetRenderBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var skia = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
        if (skia == null) return;

        using var lease = skia.Lease();
        var canvas = lease.SkCanvas;

        _effect ??= LoadEffect();
        if (_effect == null) return;

        var w = (float)bounds.Width;
        var h = (float)bounds.Height;
        var t = (float)_stopwatch.Elapsed.TotalSeconds;

        using var uniforms = new SKRuntimeEffectUniforms(_effect);
        uniforms["iResolution"] = new[] { w, h };
        uniforms["iTime"]       = t;

        using var shader = _effect.ToShader(uniforms);
        using var paint  = new SKPaint();
        paint.Shader = shader;
        canvas.DrawRect(SKRect.Create(w, h), paint);
    }

    private static SKRuntimeEffect? LoadEffect()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://OpenTalkIt/Assets/Shaders/clouds.sksl"));
            using var reader = new StreamReader(stream);
            var sksl = reader.ReadToEnd();
            var effect = SKRuntimeEffect.CreateShader(sksl, out var errors);
            if (effect == null)
                Debug.WriteLine($"[CloudShader] Compile error: {errors}");
            return effect;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CloudShader] Load error: {ex.Message}");
            return null;
        }
    }
}
