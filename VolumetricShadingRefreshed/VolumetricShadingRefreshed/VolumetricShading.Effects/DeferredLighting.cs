using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using VolumetricShadingRefreshed.VolumetricShading.Patch;

namespace VolumetricShadingRefreshed.VolumetricShading.Effects;

public class DeferredLighting
{
    private static readonly float[] ClearBlack = new[] { 0f, 0f, 0f, 1f };

    private static readonly DrawBuffersEnum[] DrawBuffersDeferred = new[]
    {
        DrawBuffersEnum.ColorAttachment0,
        DrawBuffersEnum.ColorAttachment1,
        DrawBuffersEnum.ColorAttachment2,
        DrawBuffersEnum.ColorAttachment3
    };

    private static readonly DrawBuffersEnum[] DrawBuffersPrimary = new[]
    {
        DrawBuffersEnum.ColorAttachment0,
        DrawBuffersEnum.ColorAttachment1
    };

    private readonly VolumetricShadingMod _mod;

    private readonly ClientPlatformWindows _platform;

    private bool _enabled;

    private FrameBufferRef _frameBuffer;

    private MeshRef _screenQuad;

    private ShaderProgram _shader;

    public DeferredLighting(VolumetricShadingMod mod)
    {
        _mod = mod;
        _platform = _mod.CApi.GetClientPlatformWindows();
        _mod.CApi.Settings.AddWatcher("volumetricshading_deferredLighting",
            new OnSettingsChanged<bool>(OnDeferredLightingChanged));
        _mod.CApi.Settings.AddWatcher("ssaoQuality",
            new OnSettingsChanged<int>(OnSSAOQualityChanged));
        _enabled = ModSettings.DeferredLightingEnabled;
        _mod.CApi.Event.RegisterRenderer(new DeferredLightingPreparer(this), EnumRenderStage.Opaque,
            "vsmod-deferred-lighting-prepare");
        _mod.CApi.Event.RegisterRenderer(new DeferredLightingRenderer(this), EnumRenderStage.Opaque,
            "vsmod-deferred-lighting");
        _mod.ShaderInjector.RegisterBoolProperty("VSMOD_DEFERREDLIGHTING", () => _enabled);
        _mod.CApi.Event.ReloadShader += OnReloadShaders;
        _mod.Events.RebuildFramebuffers += SetupFramebuffers;

        SetupFramebuffers(_platform.FrameBuffers);
    }

    private void OnDeferredLightingChanged(bool enabled)
    {
        _enabled = enabled;
        if (enabled && ClientSettings.SSAOQuality == 0)
        {
            ClientSettings.SSAOQuality = 1;
        }

        SetupFramebuffers(_platform.FrameBuffers);
    }

    private void OnSSAOQualityChanged(int quality)
    {
        if (quality == 0 && _enabled)
        {
            ModSettings.DeferredLightingEnabled = false;
            _enabled = false;
            _platform.RebuildFrameBuffers();
            _mod.CApi.Shader.ReloadShaders();
        }
    }

    private bool OnReloadShaders()
    {
        var success = true;
        var shader = _shader;
        if (shader != null)
        {
            shader.Dispose();
        }

        if (!_enabled)
        {
            _shader = null;
            return true;
        }

        _shader = (ShaderProgram)_mod.RegisterShader("deferredlighting", ref success);
        return success;
    }

    private void SetupFramebuffers(List<FrameBufferRef> mainBuffers)
    {
        if (_frameBuffer != null)
        {
            _platform.DisposeFrameBuffer(_frameBuffer);
            _frameBuffer = null;
        }

        if (ClientSettings.SSAOQuality <= 0 || !_enabled)
        {
            return;
        }

        var fbPrimary = mainBuffers[0];

        var fbWidth = fbPrimary.Width;
        var fbHeight = fbPrimary.Height;

        if (fbWidth == 0 || fbHeight == 0)
        {
            return;
        }

        var frameBufferRef = new FrameBufferRef
        {
            FboId = GL.GenFramebuffer(),
            Width = fbWidth,
            Height = fbHeight,
            ColorTextureIds = ArrayUtil.CreateFilled(2, _ => GL.GenTexture())
        };

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, frameBufferRef.FboId);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            TextureTarget.Texture2D, fbPrimary.DepthTextureId, 0);
        frameBufferRef.SetupColorTexture(0);
        frameBufferRef.SetupColorTexture(1);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment2,
            TextureTarget.Texture2D, fbPrimary.ColorTextureIds[2], 0);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment3,
            TextureTarget.Texture2D, fbPrimary.ColorTextureIds[3], 0);
        GL.DrawBuffers(DrawBuffersDeferred.Length, DrawBuffersDeferred);
        Framebuffers.CheckStatus();
        _frameBuffer = frameBufferRef;
        _screenQuad = _platform.GetScreenQuad();
    }

    public void OnBeginRender()
    {
        if (_frameBuffer == null)
        {
            return;
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _frameBuffer.FboId);
        // Ensures that the viewport matches the deferred FBO size :)
        GL.Viewport(0, 0, _frameBuffer.Width, _frameBuffer.Height);
        GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.ColorBufferBit);
    }

    public void OnEndRender()
    {
        if (_frameBuffer == null || _shader == null)
        {
            return;
        }

        _platform.LoadFrameBuffer(EnumFrameBuffer.Primary);

        var fbPrimary = _platform.FrameBuffers[0];
        // Ensure viewport matches the primary FBO size to hopefully avoid lower-left rendering.
        GL.Viewport(0, 0, fbPrimary.Width, fbPrimary.Height);

        GL.ClearBuffer(ClearBuffer.Color, 0, ClearBlack);
        GL.ClearBuffer(ClearBuffer.Color, 1, ClearBlack);
        var render = _mod.CApi.Render;
        var uniforms = render.ShaderUniforms;
        var myUniforms = _mod.Uniforms;
        var fb = _frameBuffer;
        _platform.GlDisableDepthTest();
        _platform.GlToggleBlend(false);
        GL.DrawBuffers(DrawBuffersPrimary.Length, DrawBuffersPrimary);
        var s = _shader;
        s.Use();
        s.BindTexture2D("gDepth", fbPrimary.DepthTextureId);
        s.BindTexture2D("gNormal", fbPrimary.ColorTextureIds[2]);
        s.BindTexture2D("inColor", fb.ColorTextureIds[0]);
        s.BindTexture2D("inGlow", fb.ColorTextureIds[1]);
        s.UniformMatrix("invProjectionMatrix", myUniforms.InvProjectionMatrix);
        s.UniformMatrix("invModelViewMatrix", myUniforms.InvModelViewMatrix);
        s.Uniform("dayLight", myUniforms.DayLight);
        s.Uniform("sunPosition", uniforms.SunPosition3D);
        if (ShaderProgramBase.shadowmapQuality > 0)
        {
            s.Uniform("shadowRangeFar", uniforms.ShadowRangeFar);
            s.Uniform("shadowRangeNear", uniforms.ShadowRangeNear);
            s.UniformMatrix("toShadowMapSpaceMatrixFar", uniforms.ToShadowMapSpaceMatrixFar);
            s.UniformMatrix("toShadowMapSpaceMatrixNear", uniforms.ToShadowMapSpaceMatrixNear);
        }

        s.Uniform("fogDensityIn", render.FogDensity);
        s.Uniform("fogMinIn", render.FogMin);
        s.Uniform("rgbaFog", render.FogColor);
        s.Uniform("flatFogDensity", uniforms.FlagFogDensity);
        s.Uniform("flatFogStart", uniforms.FlatFogStartYPos - uniforms.PlayerPos.Y);
        s.Uniform("viewDistance", ClientSettings.ViewDistance);
        s.Uniform("viewDistanceLod0", ClientSettings.ViewDistance * ClientSettings.LodBias);
        _platform.RenderFullscreenTriangle(_screenQuad);
        s.Stop();
        _platform.CheckGlError("Error while calculating deferred lighting");
        GL.DrawBuffers(DrawBuffersPrimary.Length, DrawBuffersPrimary);
        _platform.GlEnableDepthTest();
    }

    public void Dispose()
    {
        var shader = _shader;
        if (shader != null)
        {
            shader.Dispose();
        }

        _shader = null;
        if (_frameBuffer != null)
        {
            _platform.DisposeFrameBuffer(_frameBuffer);
            _frameBuffer = null;
        }
    }
}
