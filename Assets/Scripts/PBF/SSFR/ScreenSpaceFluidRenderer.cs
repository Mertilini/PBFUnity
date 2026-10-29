using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(GPUFluidSolver))]
[DefaultExecutionOrder(100)]
public class ScreenSpaceFluidRenderer : MonoBehaviour
{
    [Header("References")]
    public GPUFluidSolver solver;
    [Tooltip("Camera to composite the fluid into. Defaults to Camera.main.")]
    public Camera targetCamera;

    [Header("Quality")]
    [Range(0.25f, 1f)]
    [Tooltip("Off-screen buffer resolution relative to the screen. 0.5 = quarter the pixels, much faster blur.")]
    public float resolutionScale = 1f;
    [Range(0, 8)] public int depthBlurIterations = 2;
    [Range(0, 4)] public int thicknessBlurIterations = 2;
    [Range(1, 16)] public int blurTapRadius = 8;
    [Tooltip("Bilateral blur edge threshold in world units: depth differences larger than this " +
             "are treated as silhouette edges and not smoothed across.")]
    public float blurDepthFalloff = 0.5f;

    [Header("Fluid Look")]
    [Tooltip("Rendered sphere radius as a multiple of the solver's particleRadius. Slightly " +
             "above 1 helps neighboring particles fuse into a closed surface.")]
    public float sphereRadiusScale = 1.25f;
    [Tooltip("Scales the accumulated thickness before absorption - lower = clearer water.")]
    public float thicknessScale = 0.25f;
    [Tooltip("Per-channel absorption. Absorbing mostly red/green makes the water look blue.")]
    public Color absorptionColor = new Color(0.45f, 0.15f, 0.05f);
    public float absorptionStrength = 1.0f;
    public Color reflectionColor = new Color(0.7f, 0.85f, 1.0f);
    [Range(0f, 1f)]
    [Tooltip("How strongly the surface reflects toward reflectionColor at grazing angles. Lower = less mirror-like.")]
    public float reflectionStrength = 0.6f;
    public float refractionStrength = 0.05f;
    public float specularPower = 250f;
    public float specularIntensity = 0.6f;

    private RenderTexture _depthRT;
    private RenderTexture _depthPingRT;
    private RenderTexture _thicknessRT;
    private RenderTexture _thicknessPingRT;

    private Material _depthMat;
    private Material _thicknessMat;
    private Material _blurMat;
    private Material _compositeMat;

    private CommandBuffer _cmd;

    // Creates the materials and the command buffer.
    private void OnEnable()
    {
        if (solver == null)
            solver = GetComponent<GPUFluidSolver>();
        if (targetCamera == null)
            targetCamera = Camera.main;

        _depthMat = CreateMaterial("PBF/SSFR/FluidDepth");
        _thicknessMat = CreateMaterial("PBF/SSFR/FluidThickness");
        _blurMat = CreateMaterial("PBF/SSFR/FluidBlur");
        _compositeMat = CreateMaterial("PBF/SSFR/FluidComposite");

        if (_compositeMat != null)
            _compositeMat.renderQueue = (int)RenderQueue.Transparent;

        _cmd = new CommandBuffer { name = "SSFR Offscreen Passes" };

        if (targetCamera != null)
        {
            UniversalAdditionalCameraData camData = targetCamera.GetUniversalAdditionalCameraData();
            camData.requiresColorOption = CameraOverrideOption.On;
            camData.requiresDepthOption = CameraOverrideOption.On;
        }
    }

    // Frees the textures, the materials and the command buffer.
    private void OnDisable()
    {
        if (_cmd != null)
        {
            _cmd.Release();
            _cmd = null;
        }

        ReleaseRT(ref _depthRT);
        ReleaseRT(ref _depthPingRT);
        ReleaseRT(ref _thicknessRT);
        ReleaseRT(ref _thicknessPingRT);

        DestroyMat(ref _depthMat);
        DestroyMat(ref _thicknessMat);
        DestroyMat(ref _blurMat);
        DestroyMat(ref _compositeMat);
    }

    // Runs the offscreen passes and queues the final image for this frame.
    private void LateUpdate()
    {
        bool active =
            solver != null &&
            solver.renderMode == GPUFluidSolver.FluidRenderMode.ScreenSpaceFluid &&
            solver.GetRenderCount() > 0 &&
            solver.GetPositionGraphicsBuffer() != null &&
            targetCamera != null &&
            _cmd != null &&
            _depthMat != null && _thicknessMat != null && _blurMat != null && _compositeMat != null;

        if (!active)
            return;

        EnsureRenderTextures();

        int count = solver.GetRenderCount();
        GraphicsBuffer positions = solver.GetPositionGraphicsBuffer();
        float radius = solver.particleRadius * sphereRadiusScale;

        _depthMat.SetBuffer("_ParticlePositions", positions);
        _depthMat.SetFloat("_SphereRadius", radius);
        _thicknessMat.SetBuffer("_ParticlePositions", positions);
        _thicknessMat.SetFloat("_SphereRadius", radius);
        _thicknessMat.SetFloat("_ThicknessScale", thicknessScale);

        RecordAndExecuteOffscreenPasses(count);
        DrawComposite();
    }

    // Draws depth and thickness of all particles and blurs both of them.
    private void RecordAndExecuteOffscreenPasses(int count)
    {
        _cmd.Clear();

        Matrix4x4 view = targetCamera.worldToCameraMatrix;
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(targetCamera.projectionMatrix, false);
        _cmd.SetViewProjectionMatrices(view, proj);

        _cmd.SetRenderTarget(_depthRT);
        _cmd.ClearRenderTarget(false, true, new Color(1.0e8f, 0f, 0f, 0f));
        _cmd.DrawProcedural(Matrix4x4.identity, _depthMat, 0, MeshTopology.Triangles, 6, count);

        _cmd.SetRenderTarget(_thicknessRT);
        _cmd.ClearRenderTarget(false, true, Color.clear);
        _cmd.DrawProcedural(Matrix4x4.identity, _thicknessMat, 0, MeshTopology.Triangles, 6, count);

        _cmd.SetGlobalFloat("_BlurRadius", blurTapRadius);
        _cmd.SetGlobalFloat("_RangeFalloff", blurDepthFalloff);

        for (int i = 0; i < depthBlurIterations; i++)
        {
            _cmd.SetGlobalVector("_BlurDir", new Vector4(1, 0, 0, 0));
            _cmd.Blit(_depthRT, _depthPingRT, _blurMat, 0);
            _cmd.SetGlobalVector("_BlurDir", new Vector4(0, 1, 0, 0));
            _cmd.Blit(_depthPingRT, _depthRT, _blurMat, 0);
        }

        for (int i = 0; i < thicknessBlurIterations; i++)
        {
            _cmd.SetGlobalVector("_BlurDir", new Vector4(1, 0, 0, 0));
            _cmd.Blit(_thicknessRT, _thicknessPingRT, _blurMat, 1);
            _cmd.SetGlobalVector("_BlurDir", new Vector4(0, 1, 0, 0));
            _cmd.Blit(_thicknessPingRT, _thicknessRT, _blurMat, 1);
        }

        Graphics.ExecuteCommandBuffer(_cmd);
    }

    // Draws the finished water surface over the scene.
    private void DrawComposite()
    {
        Matrix4x4 proj = targetCamera.projectionMatrix;
        _compositeMat.SetTexture("_FluidDepthTex", _depthRT);
        _compositeMat.SetTexture("_FluidThicknessTex", _thicknessRT);
        _compositeMat.SetFloat("_P00", proj.m00);
        _compositeMat.SetFloat("_P11", proj.m11);
        _compositeMat.SetMatrix("_CamToWorld", targetCamera.cameraToWorldMatrix);
        _compositeMat.SetColor("_AbsorptionColor", absorptionColor);
        _compositeMat.SetFloat("_AbsorptionStrength", absorptionStrength);
        _compositeMat.SetColor("_ReflectionColor", reflectionColor);
        _compositeMat.SetFloat("_ReflectionStrength", reflectionStrength);
        _compositeMat.SetFloat("_RefractionStrength", refractionStrength);
        _compositeMat.SetFloat("_SpecularPower", specularPower);
        _compositeMat.SetFloat("_SpecularIntensity", specularIntensity);

        Bounds bounds = new Bounds(targetCamera.transform.position, Vector3.one * 10000f);

        Graphics.DrawProcedural(_compositeMat, bounds, MeshTopology.Triangles, 3, 1,
            targetCamera, null, ShadowCastingMode.Off, false, 0);
    }

    // Creates the offscreen textures, or recreates them if the window size changed.
    private void EnsureRenderTextures()
    {
        int w = Mathf.Max(16, Mathf.RoundToInt(targetCamera.pixelWidth * resolutionScale));
        int h = Mathf.Max(16, Mathf.RoundToInt(targetCamera.pixelHeight * resolutionScale));

        EnsureRT(ref _depthRT, w, h, RenderTextureFormat.RFloat, 0, "SSFR Fluid Depth");
        EnsureRT(ref _depthPingRT, w, h, RenderTextureFormat.RFloat, 0, "SSFR Fluid Depth Ping");
        EnsureRT(ref _thicknessRT, w, h, RenderTextureFormat.RHalf, 0, "SSFR Fluid Thickness");
        EnsureRT(ref _thicknessPingRT, w, h, RenderTextureFormat.RHalf, 0, "SSFR Fluid Thickness Ping");
    }

    // Creates one render texture, or replaces it if it has the wrong size.
    private static void EnsureRT(ref RenderTexture rt, int w, int h, RenderTextureFormat format, int depthBits, string name)
    {
        if (rt != null && (rt.width != w || rt.height != h))
        {
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        if (rt == null)
        {
            rt = new RenderTexture(w, h, depthBits, format)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
        }
    }

    // Loads a shader by its name and makes a material from it.
    private static Material CreateMaterial(string shaderName)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError($"ScreenSpaceFluidRenderer: shader '{shaderName}' not found. " +
                           "Is the SSFR folder with its .shader files present in the project?");
            return null;
        }
        return new Material(shader);
    }

    // Frees one render texture.
    private static void ReleaseRT(ref RenderTexture rt)
    {
        if (rt != null)
        {
            rt.Release();
            Destroy(rt);
            rt = null;
        }
    }

    // Frees one material.
    private static void DestroyMat(ref Material mat)
    {
        if (mat != null)
        {
            Destroy(mat);
            mat = null;
        }
    }
}
