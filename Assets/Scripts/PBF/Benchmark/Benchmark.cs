using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(50)]
public class Benchmark : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GPUFluidSolver solver;
    [SerializeField] private FluidVolumeSpawner volumeSpawner;

    [Header("Run")]
    [SerializeField] private KeyCode runKey = KeyCode.Return;
    [SerializeField] private bool runOnStart = false;
    [Tooltip("Seconds to discard before measuring, so spawn spikes do not pollute the average.")]
    [SerializeField] private float warmupSeconds = 1f;
    [Tooltip("Seconds to measure for. A fast PC fits more frames into this window than a slow " +
             "one - that is the point, every machine is measured over the same amount of time. " +
             "Long enough that the block has finished collapsing and a laptop has had time to " +
             "throttle, so the average describes sustained load rather than a burst.")]
    [SerializeField] private float measureSeconds = 30f;
    [Tooltip("Seconds the results stay on screen before the next scene is loaded. Kept short - " +
             "this is idle time that measures nothing, and the CSV holds the same numbers.")]
    [SerializeField] private float resultsSeconds = 5f;

    [Tooltip("The frame rate the simulation is supposed to hold. The results report what share of " +
             "frames reached it, which is what a 'guarantees 60+ FPS' claim rests on.")]
    [SerializeField] private int targetFps = 60;

    [Header("Output")]
    [Tooltip("Written next to the Assets folder, into a Benchmarks subfolder.")]
    [SerializeField] private string fileName = "benchmark.csv";

    [Header("Scripts")]
    [SerializeField] private Settings settings;

    private bool _running;
    private bool _finished = false;
    private float _warmupEndTime;

    private int _boundaryAtRunEnd;

    private int ParticleCount => solver != null ? solver.GetActiveCount() : 0;

    private readonly List<float> _elapsed = new List<float>();

    private readonly List<int> _particles = new List<int>();
    private float _runStartTime;

    private int _stepsAtStart;

    private int _particlesAtRunStart;

    private static readonly string[] BenchmarkScenes =
    {

        "EmptyAquarium",

        "RainyDay",
        "SimpleSlide",
        "Tsunami",

        "Meteoroids",
        "ScalingAquarium",
        "FlowingDam",

        "BunnyInTheMiddle",
        "BunnyDrop",
        "ArmadilloSplash",
    };

    private static int _sceneIndex;

    private static readonly List<string> _summaryRows = new List<string>();
    private static float _summaryShareSum;

    private bool _showingResults;
    private float _resultsUntil;

    // Starts a run right away if the scene is set up that way.
    private void Start()
    {
        if (solver == null)
            solver = FindFirstObjectByType<GPUFluidSolver>();

        if (runOnStart || _sceneIndex > 0)
            StartRun();
    }

    // Waits for the key that starts a run, and ends the run when its time is over.
    private void Update()
    {
        if (Input.GetKeyDown(runKey) && !_running && !_showingResults && !_finished)
        {
            StartRun();
        }

        if (_running)
            RecordFrame();

        if (_showingResults && Time.realtimeSinceStartup >= _resultsUntil)
            NextScene();
    }

    // Loads the next scene of the sequence, or shows the summary after the last one.
    private void NextScene()
    {
        _showingResults = false;
        _sceneIndex++;

        if (_sceneIndex < BenchmarkScenes.Length)
        {
            SceneManager.LoadScene(BenchmarkScenes[_sceneIndex]);
            return;
        }

        _sceneIndex = 0;
        _finished = true;

        ShowSummary();

        settings.SetBenchmarkRunning(false);
        settings.SetBenchmarkFinished(true);
    }

    // Builds the final table with one line per scene and saves it to a file.
    private void ShowSummary()
    {

        if (solver != null)
            solver.ClearParticles();

        float average = _summaryRows.Count > 0 ? _summaryShareSum / _summaryRows.Count : 0f;

        string table = string.Format("{0,-18}{1,9}{2,7}{3,8}{4,8}{5,7}\n",
                                     "scene", "particles", "avg", "1% low", "on tgt", "speed")
                     + string.Join("\n", _summaryRows);

        string plain = string.Format(CultureInfo.InvariantCulture,
                         "Benchmark complete\n\n{0}+ FPS on {1:F2}% of frames, "
                       + "averaged over {2} scenes\n\n",
                         targetFps, average, _summaryRows.Count) + table;

        Debug.Log(plain);

        string folder = Path.Combine(Application.dataPath, "../Benchmarks");
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "summary_" +
            System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) +
            ".csv"), SystemInfoHeader(false) + "\n" + plain);

        settings.SetBenchmarkResults(plain.Substring(0, plain.Length - table.Length)
                                   + "<mspace=0.55em>" + table + "</mspace>");
    }

    // Begins measuring: clears the old data and remembers where the simulation stands.
    public void StartRun()
    {

        settings.SetBenchmarkRunning(true);
        settings.SetBenchmarkFinished(false);

        _elapsed.Clear();
        _particles.Clear();

        _particlesAtRunStart = ParticleCount;

        if (_sceneIndex == 0)
        {
            _summaryRows.Clear();
            _summaryShareSum = 0f;
        }

        BenchmarkInstructions();

        _warmupEndTime = Time.realtimeSinceStartup + warmupSeconds;

        _runStartTime = _warmupEndTime;

        _running = true;

        Debug.Log($"Benchmark: scene {_sceneIndex + 1}/{BenchmarkScenes.Length} " +
                  $"({BenchmarkScenes[_sceneIndex]}) started at {ParticleCount} particles.");
    }

    // Stores the time and the particle count of the current frame.
    private void RecordFrame()
    {

        float now = Time.realtimeSinceStartup;

        if (now < _warmupEndTime)
            return;

        if (_elapsed.Count == 0)
        {
            _runStartTime = now;
            _stepsAtStart = solver != null ? solver.StepCount : 0;
        }

        _elapsed.Add(now - _runStartTime);
        _particles.Add(ParticleCount);

        if (now - _runStartTime >= measureSeconds)
            FinishRun();
    }

    // Spawns particles several times with a pause in between.
    private IEnumerator SpawnParticles(int count, float interval, int iterations)
    {
        if (iterations <= 0 || interval < 0)
            yield break;

        solver.spawnCount = count;

        for (int i = 0; i < iterations; i++)
        {
            solver.SpawnConfiguredBlock();

            yield return WaitForSteps(interval);
        }
    }

    // Spawns a mesh shaped volume of water several times with a pause in between.
    private IEnumerator SpawnFluidVolume(float interval, int iterations)
    {
        if (iterations <= 0 || interval < 0)
            yield break;

        for (int i = 0; i < iterations; i++)
        {
            volumeSpawner.Spawn();

            yield return WaitForSteps(interval);
        }
    }

    // Waits for the given number of simulated seconds.
    private IEnumerator WaitForSteps(float seconds)
    {
        if (solver == null)
            yield break;

        int steps = Mathf.Max(1, Mathf.RoundToInt(seconds / Time.fixedDeltaTime * solver.SubSteps));

        int until = solver.StepCount + steps;

        while (solver.StepCount < until)
            yield return null;
    }

    // Moves the point where new particles appear.
    private void MoveSpawnLocation(Vector3 newLocation)
    {
        if (solver.spawnReference != null)
            solver.spawnReference.transform.position = newLocation;
        else
            solver.transform.position = newLocation;
    }

    // Holds the spawn plan for every scene of the benchmark.
    private void BenchmarkInstructions()
    {
        switch (SceneManager.GetActiveScene().name)
        {
            case "EmptyAquarium":
                break;
            case "RainyDay":
                StartCoroutine(SpawnParticles(2000, 0.2f, 20));
                break;
            case "SimpleSlide":
                StartCoroutine(SpawnParticles(20000, 1.5f, 2));
                break;
            case "Tsunami":
                StartCoroutine(SpawnParticles(50000, 1.5f, 1));
                break;
            case "Meteoroids":
                solver.FillWithParticles(solver.aquarium.size, 60000);
                break;
            case "ScalingAquarium":
                solver.FillWithParticles(solver.aquarium.size, 60000);
                break;
            case "FlowingDam":
                StartCoroutine(SpawnParticles(7000, 2.5f, 10));
                break;
            case "BunnyInTheMiddle":
                StartCoroutine(BunnyInTheMiddleHelper(2.5f));
                break;
            case "BunnyDrop":
                solver.FillWithParticles(solver.aquarium.size, 30000);
                StartCoroutine(SpawnFluidVolume(2.5f, 8));
                break;
            case "ArmadilloSplash":
                StartCoroutine(SpawnParticles(60000, 2.5f, 2));
                break;
            default:
                break;
        }
    }

    // Spawn plan for the bunny scene.
    private IEnumerator BunnyInTheMiddleHelper(float seconds)
    {
        StartCoroutine(SpawnParticles(5000, 2.5f, 2));
        yield return WaitForSteps(3*seconds);
        StartCoroutine(SpawnParticles(10000, 2.5f, 3));
        yield return WaitForSteps(4*seconds);
        StartCoroutine(SpawnParticles(20000, 2.5f, 2));
    }

    // Works out all the results of the run and hands them to the user interface.
    private void FinishRun()
    {
        _running = false;

        StopAllCoroutines();

        if (_elapsed.Count == 0)
        {
            string error = "No frames were recorded, so nothing could be measured.";

            Debug.LogWarning("Benchmark: " + error, this);

            settings.SetBenchmarkResults(error);
            settings.SetBenchmarkRunning(false);
            settings.SetBenchmarkFinished(true);

            ShowResultsThenContinue();
            return;
        }

        float duration = _elapsed[_elapsed.Count - 1];
        int frames = _elapsed.Count;

        int steps = (solver != null ? solver.StepCount : 0) - _stepsAtStart;
        float stepsPerFrame = frames > 0 ? (float)steps / frames : 0f;

        float wantedStepsPerSecond = solver != null && Time.fixedDeltaTime > 0f
            ? solver.SubSteps / Time.fixedDeltaTime
            : 0f;

        float simSpeed = wantedStepsPerSecond > 0f && duration > 0f
            ? steps / duration / wantedStepsPerSecond
            : 0f;

        float avgFps = duration > 0f ? frames / duration : 0f;

        List<float> frameMs = FrameTimes();

        List<float> sorted = new List<float>(frameMs);
        sorted.Sort();

        float onePercentLowFps = FpsFromMs(Percentile(sorted, 0.99f));
        float worstFps = FpsFromMs(sorted.Count > 0 ? sorted[sorted.Count - 1] : 0f);

        float targetMs = targetFps > 0 ? 1000f / targetFps : 0f;
        float shareOnTarget = ShareAtOrUnder(frameMs, targetMs) * 100f;

        _boundaryAtRunEnd = solver != null ? solver.GetBoundaryCount() : 0;

        int particlesAtEnd = _particles[_particles.Count - 1];
        int particlesGained = particlesAtEnd - _particlesAtRunStart;

        string results =
            Line() +
            Row("Fluid particles", (particlesAtEnd - _boundaryAtRunEnd).ToString()) +
            Row("Boundary particles", _boundaryAtRunEnd.ToString()) +
            Row("Total particles", particlesAtEnd.ToString()) +
            Row("Spawned during run", particlesGained.ToString()) +
            Line() +
            Row("Measured frames", frames.ToString()) +
            Row("Duration (s)", duration.ToString("F2", CultureInfo.InvariantCulture)) +
            Line() +
            Row("Average FPS", avgFps.ToString("F1", CultureInfo.InvariantCulture)) +
            Row("1% low FPS", onePercentLowFps.ToString("F1", CultureInfo.InvariantCulture)) +
            Row("Worst frame FPS", worstFps.ToString("F1", CultureInfo.InvariantCulture)) +
            Line() +
            Row("Target FPS", targetFps.ToString()) +
            Row("Frames on target %", shareOnTarget.ToString("F2", CultureInfo.InvariantCulture)) +
            Line() +
            Row("Steps/frame", stepsPerFrame.ToString("F2", CultureInfo.InvariantCulture)) +
            Row("Iters/step", (solver != null ? solver.SolverIterations : 0).ToString()) +
            Row("Sim speed (x)", simSpeed.ToString("F2", CultureInfo.InvariantCulture)) +
            Line();

        _summaryRows.Add(string.Format(CultureInfo.InvariantCulture,
            "{0,-18}{1,9}{2,7:F1}{3,8:F1}{4,8:F2}{5,7:F2}",
            SceneManager.GetActiveScene().name, particlesAtEnd, avgFps, onePercentLowFps,
            shareOnTarget, simSpeed));

        _summaryShareSum += shareOnTarget;

        WriteCsv(results);

        string system =
            "Scene: " + SceneManager.GetActiveScene().name + "\n" +
            "GPU: " + SystemInfo.graphicsDeviceName + "\n" +
            "CPU: " + SystemInfo.processorType + "\n" +
            "Resolution: " + Screen.width + " x " + Screen.height + "\n" +
            "LOD: " + (settings != null && settings.LodEnabled ? "on" : "off") + "\n\n";

        Debug.Log("Benchmark results\n" + system + results);

        settings.SetBenchmarkResults(system + "<mspace=0.55em>" + results + "</mspace>");

        settings.SetBenchmarkRunning(false);
        settings.SetBenchmarkFinished(true);

        ShowResultsThenContinue();
    }

    // Keeps the results on screen for a moment and then loads the next scene.
    private void ShowResultsThenContinue()
    {

        if (solver != null)
            solver.ClearParticles();

        _showingResults = true;
        _resultsUntil = Time.realtimeSinceStartup + resultsSeconds;
    }

    // Writes one file with a row per measured frame into the Benchmarks folder.
    private void WriteCsv(string summary)
    {

        fileName = "benchmark_" +
                   SceneManager.GetActiveScene().name + "_" +
                   (settings != null && settings.LodEnabled ? "LODon" : "LODoff") + "_" +
                   System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) +
                   ".csv";

        string folder = Path.Combine(Application.dataPath, "../Benchmarks");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, fileName);

        bool isNewFile = !File.Exists(path);
        StringBuilder csv = new StringBuilder();

        if (isNewFile)
        {
            csv.Append(SystemInfoHeader());
            csv.AppendLine("time_s,frame,particles,boundary,frame_ms");
        }

        List<float> frameMs = FrameTimes();

        for (int i = 0; i < _elapsed.Count; i++)
        {

            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F3},{1},{2},{3},{4:F3}",
                _elapsed[i], i, _particles[i], _boundaryAtRunEnd,
                i < frameMs.Count ? frameMs[i] : 0f));
        }

        csv.AppendLine("# --- summary ---");

        foreach (string line in summary.Split('\n'))
        {
            if (line.Length > 0)
                csv.AppendLine("# " + line);
        }

        File.AppendAllText(path, csv.ToString());
        Debug.Log($"Benchmark: wrote {_elapsed.Count} rows to {Path.GetFullPath(path)}");
    }

    // Collects the hardware and the settings of this machine for the file header.
    private string SystemInfoHeader(bool perScene = true)
    {
        StringBuilder head = new StringBuilder();

        head.AppendLine("# PBF benchmark");

        if (perScene)
            head.AppendLine("# scene: " + SceneManager.GetActiveScene().name);

        head.AppendLine("# date: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                                                                  CultureInfo.InvariantCulture));
        head.AppendLine("# gpu: " + SystemInfo.graphicsDeviceName);
        head.AppendLine("# gpu_memory_mb: " + SystemInfo.graphicsMemorySize);
        head.AppendLine("# graphics_api: " + SystemInfo.graphicsDeviceType);
        head.AppendLine("# cpu: " + SystemInfo.processorType);
        head.AppendLine("# cpu_threads: " + SystemInfo.processorCount);
        head.AppendLine("# system_memory_mb: " + SystemInfo.systemMemorySize);
        head.AppendLine("# os: " + SystemInfo.operatingSystem);
        head.AppendLine("# unity: " + Application.unityVersion);
        head.AppendLine("# development_build: " + Debug.isDebugBuild);
        head.AppendLine("# resolution: " + Screen.width + "x" + Screen.height);
        head.AppendLine("# fullscreen: " + Screen.fullScreenMode);
        head.AppendLine("# vsync: " + QualitySettings.vSyncCount);
        head.AppendLine("# target_fps: " + Application.targetFrameRate);
        head.AppendLine("# quality_level: " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
        head.AppendLine("# fixed_delta_time: " + Time.fixedDeltaTime.ToString("F4",
                                                                             CultureInfo.InvariantCulture));
        if (perScene)
            head.AppendLine("# iters_per_step: " + (solver != null ? solver.SolverIterations : 0));

        head.AppendLine("# threads_per_group: " + GPUFluidSolver.THREADS);
        head.AppendLine("# lod: " + (settings != null && settings.LodEnabled ? "on" : "off"));
        head.AppendLine("# benchmark_target_fps: " + targetFps);
        head.AppendLine("# lost_focus: " + (settings != null && settings.LostFocusDuringRun));

        return head.ToString();
    }

    // Turns the frame timestamps into the time that each frame took.
    private List<float> FrameTimes()
    {
        List<float> times = new List<float>();

        for (int i = 1; i < _elapsed.Count; i++)
            times.Add((_elapsed[i] - _elapsed[i - 1]) * 1000f);

        if (times.Count > 0)
            times.Insert(0, times[0]);

        return times;
    }

    // Converts a frame time in milliseconds into frames per second.
    private static float FpsFromMs(float ms)
    {
        return ms > 0f ? 1000f / ms : 0f;
    }

    // Picks the value at the given position of a sorted list.
    private static float Percentile(List<float> sorted, float share)
    {
        if (sorted.Count == 0)
            return 0f;

        int index = Mathf.Clamp(Mathf.RoundToInt(share * (sorted.Count - 1)), 0, sorted.Count - 1);
        return sorted[index];
    }

    // Counts how many values are at or below the limit, as a share of all of them.
    private static float ShareAtOrUnder(List<float> values, float limit)
    {
        if (values.Count == 0)
            return 0f;

        int count = 0;
        for (int i = 0; i < values.Count; i++)
            if (values[i] <= limit)
                count++;

        return (float)count / values.Count;
    }

    private const int LabelWidth = 20;
    private const int ValueWidth = 8;

    // Formats one line of the result table.
    private static string Row(string label, string value)
    {
        return "| " + label.PadRight(LabelWidth) + " | " + value.PadLeft(ValueWidth) + " |\n";
    }

    // Returns a separator line for the result table.
    private static string Line()
    {
        return "+" + new string('-', LabelWidth + 2) + "+" + new string('-', ValueWidth + 2) + "+\n";
    }
}
