using UnityEngine;

[RequireComponent(typeof(ScreenSpaceFluidRenderer))]
[DefaultExecutionOrder(50)]
public class FluidRenderingLOD : MonoBehaviour
{

    [System.Serializable]
    public class QualityLevel
    {
        [Tooltip("Size of the off screen buffers relative to the screen. 0.5 = quarter of the pixels.")]
        [Range(0.25f, 1f)] public float resolutionScale = 1f;
        [Range(0, 8)] public int depthBlurIterations = 2;
        [Range(0, 4)] public int thicknessBlurIterations = 2;
        [Range(1, 16)] public int blurTapRadius = 8;
    }

    [Header("References")]
    [SerializeField] private ScreenSpaceFluidRenderer fluidRenderer;
    [SerializeField] private GPUFluidSolver solver;
    [SerializeField] private Camera targetCamera;

    [Header("On / Off")]
    [Tooltip("Turn off to always render at High. Use that for benchmark runs that should not " +
             "have the quality changing underneath them.")]
    [SerializeField] private bool enableRenderingLOD = true;

    public void SetRenderingLOD(bool on) { enableRenderingLOD = on; }

    [Header("Thresholds (fraction of the screen height the fluid covers)")]
    [Range(0f, 2f)] [SerializeField] private float highDetailScreenSize = 0.6f;
    [Range(0f, 2f)] [SerializeField] private float mediumDetailScreenSize = 0.25f;
    [Tooltip("Extra margin a threshold must be passed by before the quality actually changes.")]
    [Range(0f, 0.2f)] [SerializeField] private float hysteresis = 0.04f;

    [Header("Quality Levels")]
    [SerializeField] private QualityLevel high = new QualityLevel { resolutionScale = 1.0f, depthBlurIterations = 2, thicknessBlurIterations = 2, blurTapRadius = 8 };
    [SerializeField] private QualityLevel medium = new QualityLevel { resolutionScale = 0.7f, depthBlurIterations = 1, thicknessBlurIterations = 1, blurTapRadius = 6 };
    [SerializeField] private QualityLevel low = new QualityLevel { resolutionScale = 0.5f, depthBlurIterations = 1, thicknessBlurIterations = 0, blurTapRadius = 4 };

    private int _currentLevel;

    public int CurrentLevel => _currentLevel;

    public float CurrentScreenSize { get; private set; }

    // Finds the renderer, the solver and the camera if they were not set in the inspector.
    private void OnEnable()
    {
        if (fluidRenderer == null)
            fluidRenderer = GetComponent<ScreenSpaceFluidRenderer>();
        if (solver == null)
            solver = GetComponent<GPUFluidSolver>();
        if (targetCamera == null)
            targetCamera = fluidRenderer != null && fluidRenderer.targetCamera != null
                ? fluidRenderer.targetCamera
                : Camera.main;
    }

    // Measures the fluid on screen once per frame and applies the matching quality level.
    private void LateUpdate()
    {
        if (fluidRenderer == null || targetCamera == null)
            return;

        if (!enableRenderingLOD)
        {
            _currentLevel = 0;
            Apply(high);
            return;
        }

        CurrentScreenSize = MeasureScreenSize();
        _currentLevel = PickLevel(CurrentScreenSize, _currentLevel);

        Apply(_currentLevel == 0 ? high : (_currentLevel == 1 ? medium : low));
    }

    // Works out how much of the screen height the tank covers.
    private float MeasureScreenSize()
    {
        Vector3 center = transform.position;
        float worldSize = 1f;

        if (solver != null && solver.aquarium != null)
        {
            Vector3 min = solver.aquarium.BoundsMin;
            Vector3 max = solver.aquarium.BoundsMax;
            center = (min + max) * 0.5f;
            worldSize = (max - min).magnitude;
        }

        if (targetCamera.orthographic)
            return worldSize / Mathf.Max(0.0001f, targetCamera.orthographicSize * 2f);

        float distance = Vector3.Distance(targetCamera.transform.position, center);
        if (distance < 0.0001f)
            return 999f;

        float halfFov = targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        return worldSize / (2f * distance * Mathf.Tan(halfFov));
    }

    // Chooses the quality level, with a small margin so that it does not flicker.
    private int PickLevel(float screenSize, int currentLevel)
    {
        int wanted = screenSize >= highDetailScreenSize ? 0
                   : screenSize >= mediumDetailScreenSize ? 1
                   : 2;

        if (wanted < currentLevel)
        {

            float threshold = currentLevel == 1 ? highDetailScreenSize : mediumDetailScreenSize;
            if (screenSize > threshold + hysteresis)
                return currentLevel - 1;
        }
        else if (wanted > currentLevel)
        {

            float threshold = currentLevel == 0 ? highDetailScreenSize : mediumDetailScreenSize;
            if (screenSize < threshold - hysteresis)
                return currentLevel + 1;
        }

        return currentLevel;
    }

    // Hands the settings of the chosen level to the renderer.
    private void Apply(QualityLevel level)
    {
        fluidRenderer.resolutionScale = level.resolutionScale;
        fluidRenderer.depthBlurIterations = level.depthBlurIterations;
        fluidRenderer.thicknessBlurIterations = level.thicknessBlurIterations;
        fluidRenderer.blurTapRadius = level.blurTapRadius;
    }
}
