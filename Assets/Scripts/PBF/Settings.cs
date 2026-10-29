using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Unity.Collections.Unicode;

public class Settings : MonoBehaviour
{
    [SerializeField] private int MaxFPS = -1;
    [SerializeField] private int VSync = 0;

    [Header("Resolution")]
    [SerializeField] private int screenWidth = 1920;
    [SerializeField] private int screenHeight = 1080;
    [SerializeField] private FullScreenMode screenMode = FullScreenMode.FullScreenWindow;

    [SerializeField] private TMP_Text text1;
    [SerializeField] private TMP_Text text2;

    [SerializeField] private GPUFluidSolver GPUFluidSolver;
    [SerializeField] private FluidRenderingLOD fluidLOD;

    public bool LodEnabled { get; private set; }

    private const int TextUpdateInterval = 30;

    private bool isBenchmarkRunning = false;
    private bool isBenchmarkFinished = false;
    private bool areResultsShown = false;
    private string benchmarkResults;

    // Applies the screen and frame rate settings and reads the level of detail flag.
    void Start()
    {
        QualitySettings.vSyncCount = VSync;
        Application.targetFrameRate = MaxFPS;

        Application.runInBackground = true;

        if (!HasCommandLineResolution())
            Screen.SetResolution(screenWidth, screenHeight, screenMode);

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

        ApplyLodSetting();
    }

    public bool LostFocusDuringRun { get; private set; }

    // Remembers if the window lost focus during a run, because that makes the result unreliable.
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus && isBenchmarkRunning)
        {
            LostFocusDuringRun = true;
            Debug.LogWarning("Settings: the window lost focus during a run - the result is suspect.");
        }
    }

    // Turns both level of detail systems on or off, depending on the command line flag.
    private void ApplyLodSetting()
    {
        LodEnabled = HasCommandLineFlag("-lod");

        if (fluidLOD == null)
            fluidLOD = FindFirstObjectByType<FluidRenderingLOD>();

        if (GPUFluidSolver != null)
            GPUFluidSolver.useSimulationLOD = LodEnabled;

        if (fluidLOD != null)
            fluidLOD.SetRenderingLOD(LodEnabled);

        Debug.Log($"Settings: LOD {(LodEnabled ? "on" : "off")}.");
    }

    // Checks if a screen size was given on the command line.
    private static bool HasCommandLineResolution()
    {
        return HasCommandLineFlag("-screen-width") || HasCommandLineFlag("-screen-height");
    }

    // Checks if the given flag was passed on the command line.
    private static bool HasCommandLineFlag(string flag)
    {
        string[] args = System.Environment.GetCommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == flag)
                return true;
        }

        return false;
    }

    // Handles the key presses and keeps the text on screen up to date.
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) QuitApp();

        if (Input.GetKeyUp(KeyCode.Tab))
        {
            if (!SceneManager.GetActiveScene().name.Equals("TestingArea"))
            {
                SceneManager.LoadScene("TestingArea");
            }
            else
            {
                SceneManager.LoadScene("EmptyAquarium");
            }
        }

        if (!isBenchmarkFinished && Time.frameCount % TextUpdateInterval == 0)
            text1.SetText("Water particles: " + (GPUFluidSolver.GetActiveCount() - GPUFluidSolver.GetBoundaryCount()).ToString() +
                "\n" +
                "Boundary particles: " + GPUFluidSolver.GetBoundaryCount().ToString() +
                "\n" +
                "Total particles: " + GPUFluidSolver.GetActiveCount().ToString());

        if (!isBenchmarkRunning && !isBenchmarkFinished)
        {
            if (text2 != null)
                text2.SetText($"Press the return key to start the benchmark. The simulation will run and be recorded. The results will be displayed here and will be saved as .csv file in your Benchmarks folder.");
        }
        else if (isBenchmarkFinished && !areResultsShown && benchmarkResults != null)
        {
            text2.transform.localPosition = new Vector3(-200f, 350, 0);

            text2.textWrappingMode = TextWrappingModes.NoWrap;

            text2.enableAutoSizing = false;
            text2.fontSize = 28f;

            text2.SetText(benchmarkResults);
            areResultsShown = true;
        }

        else if (isBenchmarkFinished && Input.GetKeyDown(KeyCode.Return))
            QuitApp();
    }

    // Tells this script that the benchmark has started or stopped.
    public void SetBenchmarkRunning(bool running)
    {
        isBenchmarkRunning = running;

        if (running)
            text2.SetText("Don't press anything! The benchmark is running...");
    }

    // Tells this script that the benchmark is done.
    public void SetBenchmarkFinished(bool finished)
    {
        isBenchmarkFinished = finished;
    }

    // Stores the result text so that it can be shown on screen.
    public void SetBenchmarkResults(string results)
    {
        benchmarkResults = results;

        areResultsShown = false;
    }

    public void SetAreResultsShown(bool areResultsShown) { this.areResultsShown = areResultsShown; }

    private void QuitApp() { Application.Quit(); }
}
