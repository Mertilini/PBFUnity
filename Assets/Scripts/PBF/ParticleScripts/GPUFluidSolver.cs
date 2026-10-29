using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;

public class GPUFluidSolver : MonoBehaviour
{
    [Header("Aquarium Bounds")]
    public Vector3 aquariumCenter = Vector3.zero;
    public Vector3 aquariumSize = new Vector3(4f, 5f, 3f);
    public AquariumGPU aquarium;

    private Vector3 _boundsMin;
    private Vector3 _boundsMax;

    [Header("Spawning")]
    public Transform spawnReference;
    public Transform fillerReference;
    public Vector3 fillDimensions;
    public int spawnCount = 15;
    [Tooltip("Spawn spawnCount particles as a block when a benchmark run begins, instead of " +
             "waiting for the S key. Leave off in scenes that fill themselves some other way, " +
             "such as with a FluidVolumeSpawner.")]
    public int fillCount = 15;
    public bool spawnOnStart = false;
    public float spawnRadius = 1.2f;

    [Header("Mesh Boundaries")]
    [Tooltip("Objects whose surface is sampled into static boundary particles at startup.")]
    public CreateBoundary[] meshBoundaries;
    [Tooltip("Cubes and cuboids the fluid flows around analytically, costing no particles at all. " +
             "Leave empty to find every BoxBoundary in the scene automatically.")]
    public BoxBoundary[] boxBoundaries;
    [Tooltip("Draw the boundary shell as fluid particles. Debug only - it also feeds the SSFR surface.")]
    public bool renderBoundaryParticles = false;

    [Header("Particle Settings")]
    public float particleRadius = 0.1f;
    public float hMultiplier = 2.0f;

    [Range(0.4f, 0.75f)] public float restSpacingFactor = 0.475f;

    public float RestSpacing => particleRadius * hMultiplier * restSpacingFactor;

    [Header("Fluid Behavior")]
    public float viscosityCoeff = 0.02f;
    public float vorticityStrength = 0.1f;
    [Tooltip("Artificial pressure (tensile instability correction) as a virtual density error at " +
             "contact distance. 0.02 = behave as if touching particles were 2% over-dense. Raise " +
             "it if particles clump into strands, lower it if free surfaces boil or blobs inflate.")]
    public float artificialPressure = 0.02f;
    public int solverInterations = 3;

    [Header("Level of Detail")]
    [Tooltip("Skip the vorticity confinement force for particles deep inside the fluid, where the " +
             "swirls it adds cannot be seen. The density solve and viscosity always run for every particle.")]
    public bool useSimulationLOD = false;

    [Header("Shader")]

    [FormerlySerializedAs("computeShader")]
    public ComputeShader fluidSolver;

    public GameObject prefab;

    public float timeScale = 1.5f;

    [Header("Tap Flow Settings")]
    public Vector3 tapDirection = Vector3.down;
    [Tooltip("Speed newly spawned particles start with. 0 lets a spawned block simply fall under " +
             "gravity, which is the drop test the PBF papers use.")]
    public float tapSpeed = 0f;

    public enum FluidRenderMode
    {
        Particles,
        ScreenSpaceFluid,
    }

    [Header("Rendering")]
    public FluidRenderMode renderMode = FluidRenderMode.Particles;

    [StructLayout(LayoutKind.Sequential)]
    public struct Particle
    {
        public float3 pos;
        public float3 vel;
        public float3 predictedPos;
        public float mass;
        public uint isBoundary;
        public float3 vorticity;

        public static readonly int Stride = 56;
    }

    private const int MAX_CAPACITY = 135526;
    private int _activeCount;

    private int _boundaryCount;

    private int threadGroups;

    private Particle[] _particles;

    private ComputeBuffer _particleBufferIn;
    private ComputeBuffer _particleBufferOut;

    private ComputeBuffer _gridCountsBuffer;
    private ComputeBuffer _gridParticlesBuffer;

    private ComputeBuffer _lambdaBuffer;
    private ComputeBuffer _deltaQBuffer;

    private ComputeBuffer _boxBuffer;
    private int _boxCount;

    [StructLayout(LayoutKind.Sequential)]
    private struct BoxData
    {
        public Matrix4x4 worldToBox;
        public Matrix4x4 boxToWorld;
        public Vector3 halfExtents;
        public float pad0;
        public Vector3 linearVelocity;
        public float pad1;
        public Vector3 angularVelocity;
        public float pad2;

        public const int Stride = 64 + 64 + 12 + 4 + 12 + 4 + 12 + 4;
    }

    private BoxData[] _boxData;
    private Vector3[] _boxPreviousPosition;
    private Quaternion[] _boxPreviousRotation;

    private const int GRID_SIZE = 262144;
    private const int MAX_PER_CELL = 256;
    private const float h = 1f;

    public const int THREADS = 64;

    private bool _useAasInput = true;

    private GraphicsBuffer _positionGraphicsBuffer;
    private MaterialPropertyBlock _mpb;
    private Mesh _particleMesh;

    private float fluidDt;
    private int subSteps = 1;

    // Creates all the buffers, sets the solver values and spawns the boundary particles.
    void Start()
    {
        this._activeCount = 0;

        threadGroups = (MAX_CAPACITY + THREADS - 1) / THREADS;

        Debug.Log($"C# stride: {Particle.Stride}");

        if (aquarium == null)
        {
            Debug.LogWarning("AquariumGPU not assigned.");
        }

        Debug.Log($"Aquarium Bounds - Min: {aquarium.BoundsMin}, Max: {aquarium.BoundsMax}");
        Debug.Log($"Spawn Position: {transform.position}");

        if (transform.position.y < aquarium.BoundsMin.y || transform.position.y > aquarium.BoundsMax.y)
        {
            Debug.LogError($"Spawn position Y={transform.position.y} is OUTSIDE aquarium Y range [{aquarium.BoundsMin.y}, {aquarium.BoundsMax.y}]!");
        }

        _particles = new Particle[MAX_CAPACITY];

        _particleBufferIn = new ComputeBuffer(MAX_CAPACITY, Particle.Stride);
        _particleBufferOut = new ComputeBuffer(MAX_CAPACITY, Particle.Stride);

        _gridCountsBuffer = new ComputeBuffer(GRID_SIZE, sizeof(uint));
        _gridParticlesBuffer = new ComputeBuffer(GRID_SIZE * MAX_PER_CELL, sizeof(uint));
        _lambdaBuffer = new ComputeBuffer(MAX_CAPACITY, sizeof(float));
        _deltaQBuffer = new ComputeBuffer(MAX_CAPACITY, sizeof(float) * 3);

        _particleBufferIn.SetData(_particles);
        _particleBufferOut.SetData(_particles);

        int kernel = fluidSolver.FindKernel("PredictPosition");
        fluidSolver.SetBuffer(kernel, "particlesIn", _particleBufferIn);
        fluidSolver.SetBuffer(kernel, "particlesOut", _particleBufferOut);

        kernel = fluidSolver.FindKernel("ClearGrid");
        fluidSolver.SetBuffer(kernel, "gridCounts", _gridCountsBuffer);

        kernel = fluidSolver.FindKernel("BuildSpatialGrid");
        fluidSolver.SetBuffer(kernel, "gridCounts", _gridCountsBuffer);
        fluidSolver.SetBuffer(kernel, "gridParticles", _gridParticlesBuffer);
        fluidSolver.SetBuffer(kernel, "particlesIn", _particleBufferIn);
        fluidSolver.SetInt("_gridSize", GRID_SIZE);

        SetupGPURendering();

        SetupAllValues();

        SpawnBoundaryParticles();

        SetupBoxBoundaries();
    }

    // Collects every solid box in the scene and sends them to the shader.
    private void SetupBoxBoundaries()
    {
        if (boxBoundaries == null || boxBoundaries.Length == 0)
            boxBoundaries = FindObjectsByType<BoxBoundary>(FindObjectsSortMode.None);

        _boxCount = boxBoundaries != null ? boxBoundaries.Length : 0;

        _boxData = new BoxData[Mathf.Max(1, _boxCount)];
        _boxPreviousPosition = new Vector3[_boxData.Length];
        _boxPreviousRotation = new Quaternion[_boxData.Length];

        for (int i = 0; i < _boxCount; i++)
        {
            if (boxBoundaries[i] == null)
                continue;

            _boxPreviousPosition[i] = boxBoundaries[i].transform.position;
            _boxPreviousRotation[i] = boxBoundaries[i].transform.rotation;
        }

        _boxBuffer = new ComputeBuffer(_boxData.Length, BoxData.Stride);

        UpdateBoxBoundaries(0f);

        fluidSolver.SetInt("_boxCount", _boxCount);

        Debug.Log($"Box boundaries: {_boxCount}.");
    }

    // Updates position and speed of every box, so that moving boxes can push the water.
    private void UpdateBoxBoundaries(float dt)
    {
        for (int i = 0; i < _boxCount; i++)
        {
            BoxBoundary box = boxBoundaries[i];
            if (box == null)
                continue;

            Vector3 position = box.transform.position;
            Quaternion rotation = box.transform.rotation;

            Vector3 linear = Vector3.zero;
            Vector3 angular = Vector3.zero;

            if (dt > 0f)
            {
                linear = (position - _boxPreviousPosition[i]) / dt;

                Quaternion delta = rotation * Quaternion.Inverse(_boxPreviousRotation[i]);
                delta.ToAngleAxis(out float angleDegrees, out Vector3 axis);

                if (angleDegrees > 180f)
                    angleDegrees -= 360f;

                if (Mathf.Abs(angleDegrees) > 0.0001f && !float.IsNaN(axis.x))
                    angular = axis.normalized * (angleDegrees * Mathf.Deg2Rad / dt);
            }

            _boxPreviousPosition[i] = position;
            _boxPreviousRotation[i] = rotation;

            _boxData[i] = new BoxData
            {
                worldToBox = box.BoxToWorld.inverse,
                boxToWorld = box.BoxToWorld,
                halfExtents = box.HalfExtents,
                linearVelocity = linear,
                angularVelocity = angular,
            };
        }

        _boxBuffer.SetData(_boxData);
    }

    private float WallSkin => RestSpacing * 0.5f;

    // Checks if a position lies inside the tank.
    private bool IsInsideAquarium(Vector3 position)
    {
        if (aquarium == null)
            return true;

        Vector3 min = aquarium.BoundsMin + Vector3.one * WallSkin;
        Vector3 max = aquarium.BoundsMax - Vector3.one * WallSkin;

        return position.x >= min.x && position.x <= max.x
            && position.y >= min.y && position.y <= max.y
            && position.z >= min.z && position.z <= max.z;
    }

    // Spawns the block of water that was set up in the inspector.
    public void SpawnConfiguredBlock()
    {
        if (!spawnOnStart)
            return;

        if (_particles == null)
        {
            Debug.LogWarning("SpawnConfiguredBlock ran before Start - the buffers do not exist yet.");
            return;
        }

        SpawnMultipleParticles(spawnCount);
    }

    public int StepCount { get; private set; }

    public int SubSteps => subSteps;

    public int SolverIterations => solverInterations;

    // Reads the key presses for spawning and clearing water.
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.RightShift))
        {
            ClearParticles();
        }
        if (Input.GetKeyDown(KeyCode.D))
        {
            Transform spawnerTransform = spawnReference != null ? spawnReference : transform;
            SpawnParticle(spawnerTransform.position);
        }
        if (Input.GetKeyDown(KeyCode.S))
        {
            SpawnMultipleParticles(spawnCount);
        }
        if (Input.GetKeyDown(KeyCode.R))
        {
            SetupAllValues();
        }
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SpawnMultipleParticles(1000);
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SpawnMultipleParticles(2000);
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SpawnMultipleParticles(8000);
        }
        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SpawnMultipleParticles(27000);
        }
        if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            SpawnMultipleParticles(64000);
        }
        if (Input.GetKeyDown(KeyCode.Q))
        {
            FillWithParticles(fillDimensions, fillCount);
        }
    }

    // Runs one simulation step at the fixed timestep.
    private void FixedUpdate()
    {

        if (_activeCount - _boundaryCount == 0) return;

        if (_boxCount > 0)
            UpdateBoxBoundaries(fluidDt * subSteps);

        UpdateAquariumBounds(fluidDt * subSteps);

        for (int i = 0; i < subSteps; i++)
        {
            RunSimulationStep();
        }

        StepCount += subSteps;
    }

    private Vector3 _previousAquariumMin;
    private Vector3 _previousAquariumMax;

    // Sends the tank bounds and their speed to the shader.
    private void UpdateAquariumBounds(float dt)
    {
        if (aquarium == null)
            return;

        Vector3 min = aquarium.BoundsMin;
        Vector3 max = aquarium.BoundsMax;

        Vector3 minVelocity = dt > 0f ? (min - _previousAquariumMin) / dt : Vector3.zero;
        Vector3 maxVelocity = dt > 0f ? (max - _previousAquariumMax) / dt : Vector3.zero;

        _previousAquariumMin = min;
        _previousAquariumMax = max;

        fluidSolver.SetVector("_aquariumMin", min);
        fluidSolver.SetVector("_aquariumMax", max);
        fluidSolver.SetVector("_aquariumMinVelocity", minVelocity);
        fluidSolver.SetVector("_aquariumMaxVelocity", maxVelocity);
    }

    // Draws the particles after everything else has been updated.
    void LateUpdate()
    {
        UpdateGPURendering();
    }

    // Calculates all the constants of the solver and sends them to the shader.
    private void SetupAllValues()
    {
        if (particleRadius <= 0f)
        {
            Debug.LogError("Simulation scale must be greater than 0!");
            return;
        }

        float h = particleRadius * hMultiplier;
        float h2 = h * h;
        float h9 = Mathf.Pow(h, 9);
        float pi = Mathf.PI;

        float kernelConstant = 315f / (64f * pi * h9);
        float kernelGradientConstant = -945f / (32f * pi * h9);
        float spikyGradientConstant = -45f / (Mathf.PI * Mathf.Pow(h, 6));

        float restSpacing = restSpacingFactor * h;
        float rho0 = LatticeRestDensity(h, restSpacing, kernelConstant);

        float delta_q = 0.1f * h;
        float sTerm = h2 - (delta_q * delta_q);
        float sKernelValue = kernelConstant * Mathf.Pow(sTerm, 3f);

        int MAX_PER_CELL = 256;

        fluidSolver.SetFloat("_h", h);
        fluidSolver.SetFloat("_h2", h2);
        fluidSolver.SetFloat("_pow2h", h2);
        fluidSolver.SetFloat("_pow9h", h9);
        fluidSolver.SetFloat("_pi", pi);

        fluidSolver.SetFloat("_kernelconstant", kernelConstant);
        fluidSolver.SetFloat("_kernelgradientconstant",kernelGradientConstant);
        fluidSolver.SetFloat("_spikygradientconstant", spikyGradientConstant);

        fluidSolver.SetFloat("_rho0", rho0);
        fluidSolver.SetFloat("_oneOverRho_0", 1f/ rho0);

        fluidSolver.SetFloat("_delta_q", delta_q);
        fluidSolver.SetFloat("_sTerm", sTerm);
        fluidSolver.SetFloat("_sKernelValue", sKernelValue);

        fluidSolver.SetFloat("_viscosityCoeff", viscosityCoeff);
        fluidSolver.SetFloat("_vorticityStrength", vorticityStrength);

        fluidSolver.SetFloat("_wallSkin", restSpacing * 0.5f);

        if (aquarium != null)
        {
            fluidSolver.SetVector("_aquariumMin", aquarium.BoundsMin);
            fluidSolver.SetVector("_aquariumMax", aquarium.BoundsMax);
            fluidSolver.SetFloat("_boundaryRestitution", aquarium.restitution);
            fluidSolver.SetFloat("_boundaryFriction", aquarium.friction);
        }

        float refDenominator = ReferenceLambdaDenominator(h, restSpacing, spikyGradientConstant, rho0);
        fluidSolver.SetFloat("_sCorrK", artificialPressure / refDenominator);

        fluidSolver.SetInt("MAX_PER_CELL", MAX_PER_CELL);

        if (prefab != null)
        {
            Material mat = prefab.GetComponent<Renderer>().sharedMaterial;
            if (mat != null)
            {
                mat.SetFloat("_Size", particleRadius);
            }
        }

        float gravity = 9.81f;

        float maxStableDt = 0.4f * Mathf.Sqrt(h/ gravity);

        subSteps = Mathf.Max(1, Mathf.CeilToInt((Time.fixedDeltaTime * timeScale) / maxStableDt));

        fluidDt = (Time.fixedDeltaTime * timeScale) / subSteps;

        Debug.Log($"[Scale] h={h:F4} | rho0={rho0:F1} (1/V={1f / Mathf.Pow(restSpacing, 3f):F1}) | " +
                  $"timeScale={timeScale} | subSteps={subSteps} | fluidDt={fluidDt:F4}");
    }

    // Works out the rest density for particles sitting on a regular grid.
    private static float LatticeRestDensity(float h, float spacing, float kernelConstant)
    {
        float h2 = h * h;
        float density = kernelConstant * h2 * h2 * h2;

        int range = Mathf.CeilToInt(h / spacing);

        for (int x = -range; x <= range; x++)
        {
            for (int y = -range; y <= range; y++)
            {
                for (int z = -range; z <= range; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;

                    float r2 = (x * x + y * y + z * z) * spacing * spacing;
                    if (r2 >= h2)
                        continue;

                    float term = h2 - r2;
                    density += kernelConstant * term * term * term;
                }
            }
        }

        return density;
    }

    // Works out a reference value, so that the artificial pressure stays the same at any particle size.
    private static float ReferenceLambdaDenominator(float h, float spacing, float spikyGradientConstant, float rho0)
    {
        float h2 = h * h;
        float sum = 0f;

        int range = Mathf.CeilToInt(h / spacing);

        for (int x = -range; x <= range; x++)
        {
            for (int y = -range; y <= range; y++)
            {
                for (int z = -range; z <= range; z++)
                {
                    if (x == 0 && y == 0 && z == 0)
                        continue;

                    float r2 = (x * x + y * y + z * z) * spacing * spacing;
                    if (r2 >= h2)
                        continue;

                    float r = Mathf.Sqrt(r2);
                    if (r < 0.0001f)
                        continue;

                    float term = h - r;
                    float gradient = spikyGradientConstant * term * term;
                    sum += gradient * gradient;
                }
            }
        }

        return Mathf.Max(sum / (rho0 * rho0), 1e-6f);
    }

    // Dispatches the compute kernels of one simulation step in their fixed order.
    private void RunSimulationStep()
    {

        ComputeBuffer readFrom = _useAasInput ? _particleBufferIn : _particleBufferOut;
        ComputeBuffer writeTo = _useAasInput ? _particleBufferOut : _particleBufferIn;

        fluidSolver.SetFloat("_dt", fluidDt);
        fluidSolver.SetInt("_particleCount", _activeCount);

        int kernel1 = fluidSolver.FindKernel("PredictPosition");
        fluidSolver.SetBuffer(kernel1, "particlesIn", readFrom);
        fluidSolver.SetBuffer(kernel1, "particlesOut", writeTo);
        fluidSolver.Dispatch(kernel1, threadGroups, 1, 1);

        int kernel2 = fluidSolver.FindKernel("ClearGrid");
        fluidSolver.Dispatch(kernel2, (GRID_SIZE + THREADS - 1) / THREADS, 1, 1);

        int kernel3 = fluidSolver.FindKernel("BuildSpatialGrid");
        fluidSolver.SetBuffer(kernel3, "particlesIn", writeTo);
        fluidSolver.SetBuffer(kernel3, "gridCounts", _gridCountsBuffer);
        fluidSolver.SetBuffer(kernel3, "gridParticles", _gridParticlesBuffer);
        fluidSolver.Dispatch(kernel3, threadGroups, 1, 1);

        for (int iter = 0; iter < solverInterations; iter++)
        {

            int kernel4 = fluidSolver.FindKernel("CalculateLambda");
            fluidSolver.SetBuffer(kernel4, "particlesIn", writeTo);
            fluidSolver.SetBuffer(kernel4, "gridCounts", _gridCountsBuffer);
            fluidSolver.SetBuffer(kernel4, "gridParticles", _gridParticlesBuffer);
            fluidSolver.SetBuffer(kernel4, "lambda_i", _lambdaBuffer);
            fluidSolver.SetBuffer(kernel4, "boxes", _boxBuffer);
            fluidSolver.Dispatch(kernel4, threadGroups, 1, 1);

            int kernel5 = fluidSolver.FindKernel("CalculatePositionUpdate");
            fluidSolver.SetBuffer(kernel5, "particlesIn", writeTo);
            fluidSolver.SetBuffer(kernel5, "gridCounts", _gridCountsBuffer);
            fluidSolver.SetBuffer(kernel5, "gridParticles", _gridParticlesBuffer);
            fluidSolver.SetBuffer(kernel5, "lambda_i", _lambdaBuffer);
            fluidSolver.SetBuffer(kernel5, "deltaQ_i", _deltaQBuffer);
            fluidSolver.SetBuffer(kernel5, "boxes", _boxBuffer);
            fluidSolver.Dispatch(kernel5, threadGroups, 1, 1);

            int kernel6 = fluidSolver.FindKernel("UpdatePositions");
            fluidSolver.SetBuffer(kernel6, "particlesIn", writeTo);
            fluidSolver.SetBuffer(kernel6, "particlesOut", writeTo);
            fluidSolver.SetBuffer(kernel6, "deltaQ_i", _deltaQBuffer);
            fluidSolver.SetBuffer(kernel6, "boxes", _boxBuffer);
            fluidSolver.Dispatch(kernel6, threadGroups, 1, 1);
        }

        int kernel7 = fluidSolver.FindKernel("UpdateVelocities");
        fluidSolver.SetBuffer(kernel7, "particlesIn", writeTo);
        fluidSolver.SetBuffer(kernel7, "particlesOut", readFrom);
        fluidSolver.SetBuffer(kernel7, "gridCounts", _gridCountsBuffer);
        fluidSolver.SetBuffer(kernel7, "gridParticles", _gridParticlesBuffer);

        fluidSolver.SetBuffer(kernel7, "lambda_i", _lambdaBuffer);
        fluidSolver.SetInt("_useSimulationLOD", useSimulationLOD ? 1 : 0);
        fluidSolver.Dispatch(kernel7, threadGroups, 1, 1);

        int kernel8 = fluidSolver.FindKernel("ApplyAquariumBoundaries");
        fluidSolver.SetBuffer(kernel8, "particlesIn", readFrom);
        fluidSolver.SetBuffer(kernel8, "particlesOut", writeTo);
        fluidSolver.SetBuffer(kernel8, "boxes", _boxBuffer);
        fluidSolver.Dispatch(kernel8, threadGroups, 1, 1);

        _useAasInput = !_useAasInput;
    }

    // Prepares the buffer and the material used to draw the particles.
    private void SetupGPURendering()
    {
        MeshFilter meshFilter = prefab.GetComponent<MeshFilter>();
        _particleMesh = meshFilter.sharedMesh;

        if (_particleMesh == null)
        {
            Debug.LogError("Mesh is NULL! Check prefab has MeshFilter with valid mesh.");
            return;
        }

        _positionGraphicsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MAX_CAPACITY, sizeof(float) * 3);

        _mpb = new MaterialPropertyBlock();
        _mpb.SetBuffer("_ParticlePositions", _positionGraphicsBuffer);

        Material mat = prefab.GetComponent<Renderer>().sharedMaterial;
        mat.SetFloat("_Size", 1f);
        mat.SetColor("_Color", new Color(0.2f, 0.6f, 1.0f, 0.8f));

        if (mat == null)
        {
            Debug.LogError("Material is NULL! Check prefab has MeshRenderer with valid material.");
            return;
        }

        Debug.Log($"Mesh: {_particleMesh.name}, Vertices: {_particleMesh.vertexCount}");
    }

    // Copies the positions into the render buffer and draws all particles at once.
    private void UpdateGPURendering()
    {
        int renderOffset = renderBoundaryParticles ? 0 : _boundaryCount;
        int renderCount = _activeCount - renderOffset;

        if (renderCount <= 0)
            return;

        ComputeBuffer sourceBuffer = _useAasInput ? _particleBufferIn : _particleBufferOut;

        int copyKernel = fluidSolver.FindKernel("CopyPositions");
        fluidSolver.SetBuffer(copyKernel, "particlesIn", sourceBuffer);
        fluidSolver.SetBuffer(copyKernel, "positionBuffer", _positionGraphicsBuffer);
        fluidSolver.SetInt("_particleCount", _activeCount);
        fluidSolver.SetInt("_renderOffset", renderOffset);

        int threadGroups = (renderCount + THREADS - 1) / THREADS;
        fluidSolver.Dispatch(copyKernel, threadGroups, 1, 1);

        if (renderMode != FluidRenderMode.Particles)
            return;

        Renderer renderer = prefab.GetComponent<Renderer>();

        renderer.GetPropertyBlock(_mpb);

        if (_positionGraphicsBuffer == null || !_positionGraphicsBuffer.IsValid())
        {
            Debug.LogError("GraphicsBuffer is null or invalid!");
            return;
        }

        _mpb.SetBuffer("_ParticlePositions", _positionGraphicsBuffer);

        Graphics.DrawMeshInstancedProcedural(_particleMesh, 0, renderer.sharedMaterial, new Bounds(Vector3.zero, Vector3.one * 1000f), renderCount, _mpb);
    }

    public GraphicsBuffer GetPositionGraphicsBuffer() { return _positionGraphicsBuffer; }

    // Creates the static particles that make up the surface of every mesh boundary.
    private void SpawnBoundaryParticles()
    {
        if (meshBoundaries == null || meshBoundaries.Length == 0)
            return;

        int start = _activeCount;

        foreach (CreateBoundary boundary in meshBoundaries)
        {
            if (boundary == null)
                continue;

            List<Vector3> positions = boundary.GetBoundaryPositions();

            for (int i = 0; i < positions.Count; i++)
            {
                if (_activeCount >= MAX_CAPACITY)
                {
                    Debug.LogWarning($"Max capacity ({MAX_CAPACITY}) reached while spawning boundary particles.");
                    break;
                }

                Vector3 pos = positions[i];

                _particles[_activeCount] = new Particle()
                {
                    pos = pos,
                    vel = float3.zero,
                    predictedPos = pos,
                    mass = boundary.Mass,
                    isBoundary = 1,
                    vorticity = float3.zero,
                };

                _activeCount++;
            }
        }

        _boundaryCount = _activeCount - start;

        if (_boundaryCount > 0)
        {
            _particleBufferIn.SetData(_particles, 0, 0, _activeCount);
            _particleBufferOut.SetData(_particles, 0, 0, _activeCount);
        }

        Debug.Log($"Spawned {_boundaryCount} boundary particles.");
    }

    // Adds a single water particle at the given position.
    private void SpawnParticle(Vector3 pos)
    {
        if (_activeCount >= MAX_CAPACITY)
        {
            Debug.Log("_activeCount is exceeding MAX_CAPACITY!");
            return;
        }

        if (!IsInsideAquarium(pos))
        {
            Debug.LogWarning($"Spawn position {pos} is outside the aquarium - nothing was spawned.");
            return;
        }

        Particle particle = new Particle()
        {
            pos = pos,
            vel = float3.zero,
            predictedPos = pos,
            mass = 1f,
            isBoundary = 0,
            vorticity = 0,
        };

        _particles[_activeCount] = particle;

        _particleBufferIn.SetData(_particles, _activeCount, _activeCount, 1);
        _particleBufferOut.SetData(_particles, _activeCount, _activeCount, 1);

        _activeCount++;
    }

    // Spawns a cube shaped block of water around the spawn point.
    private void SpawnMultipleParticles(int count)
    {

        int room = MAX_CAPACITY - _activeCount;
        count = Mathf.Min(count, room);

        if (count <= 0)
        {
            Debug.LogWarning($"Max capacity ({MAX_CAPACITY}) reached - no block was spawned.");
            return;
        }

        float spacing = RestSpacing;

        int edge = Mathf.Max(1, Mathf.RoundToInt(Mathf.Pow(count, 1f / 3f)));

        if (edge * edge * edge > count)
            edge = Mathf.Max(1, edge - 1);

        int perLayer = edge * edge;
        int layers = Mathf.CeilToInt((float)count / perLayer);

        Transform spawner = spawnReference != null ? spawnReference : transform;
        Vector3 centerPos = spawner.position;

        float startX = -(edge - 1) * 0.5f * spacing;
        float startZ = -(edge - 1) * 0.5f * spacing;
        float startY = -(layers - 1) * 0.5f * spacing;

        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            int y = i / perLayer;
            int withinLayer = i % perLayer;
            int x = withinLayer / edge;
            int z = withinLayer % edge;

            Vector3 spawnPos = centerPos + new Vector3(
                startX + x * spacing,
                startY + y * spacing,
                startZ + z * spacing
            );

            if (!IsInsideAquarium(spawnPos))
                continue;

            _particles[_activeCount] = new Particle()
            {
                pos = spawnPos,
                vel = tapDirection.normalized * tapSpeed,
                predictedPos = spawnPos,
                mass = 1f,
                isBoundary = 0,
                vorticity = float3.zero,
            };

            _activeCount++;
            spawned++;
        }

        UploadSpawned(spawned);

        string skipped = spawned < count ? $", {count - spawned} skipped outside the aquarium" : "";

        Debug.Log($"Spawned {spawned} particles: a {edge}x{edge}x{edge} cube " +
                  $"plus {count - edge * edge * edge} on top, at spacing {spacing:F4}{skipped}.");
    }

    // Fills a box of the given size with the given number of particles.
    public void FillWithParticles(Vector3 dimension, int count)
    {
        if (fillerReference == null)
            return;

        if (_particles == null)
        {
            Debug.LogWarning("FillWithParticles called before Start - the buffers do not exist yet.");
            return;
        }

        float spacing = RestSpacing;

        int countX = Mathf.Max(1, Mathf.FloorToInt(dimension.x / spacing));
        int countZ = Mathf.Max(1, Mathf.FloorToInt(dimension.z / spacing));

        int perLayer = countX * countZ;

        int room = MAX_CAPACITY - _activeCount;
        count = Mathf.Min(count, room);

        if (count <= 0)
        {
            Debug.LogWarning($"Max capacity ({MAX_CAPACITY}) reached - couldn't fill area.");
            return;
        }

        int layers = count / perLayer;

        if (layers < 1)
        {
            Debug.LogWarning($"FillWithParticles: {count} particles is less than one " +
                             $"{countX}x{countZ} layer ({perLayer}) - nothing was spawned. " +
                             "Use a smaller footprint or a larger count.");
            return;
        }

        int requested = count;
        count = layers * perLayer;

        Transform spawner = fillerReference;

        Vector3 centerPos = spawner.position;

        float startX = -(countX - 1) * 0.5f * spacing;
        float startZ = -(countZ - 1) * 0.5f * spacing;

        float startY = WallSkin;

        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            int y = i / perLayer;
            int withinLayer = i % perLayer;
            int x = withinLayer / countZ;
            int z = withinLayer % countZ;

            Vector3 spawnPos = centerPos + new Vector3(
                startX + x * spacing,
                startY + y * spacing,
                startZ + z * spacing
            );

            if (!IsInsideAquarium(spawnPos))
                continue;

            _particles[_activeCount] = new Particle()
            {
                pos = spawnPos,
                vel = tapDirection.normalized * tapSpeed,
                predictedPos = spawnPos,
                mass = 1f,
                isBoundary = 0,
                vorticity = float3.zero,
            };

            _activeCount++;
            spawned++;
        }

        UploadSpawned(spawned);

        string skipped = spawned < count ? $", {count - spawned} skipped outside the aquarium" : "";
        string rounded = count < requested
            ? $", rounded down from {requested} so every layer is full"
            : "";

        Debug.Log($"Filled a {countX}x{layers}x{countZ} box = {spawned} particles " +
                  $"at spacing {spacing:F4}{rounded}{skipped}.");
    }

    // Removes all water particles but keeps the boundary particles.
    public void ClearParticles()
    {
        _activeCount = 0;
        _boundaryCount = 0;

        fluidSolver.SetInt("_particleCount", _activeCount);
    }

    // Adds water particles at the given positions and returns how many of them fit.
    public int SpawnFluidParticles(List<Vector3> positions, Vector3 velocity)
    {
        if (_particles == null)
        {
            Debug.LogWarning("SpawnFluidParticles called before Start - the buffers do not exist yet.");
            return 0;
        }

        if (positions == null || positions.Count == 0)
            return 0;

        int spawned = 0;
        int outside = 0;

        for (int i = 0; i < positions.Count; i++)
        {
            if (_activeCount >= MAX_CAPACITY)
            {
                Debug.LogWarning($"Max capacity ({MAX_CAPACITY}) reached - {positions.Count - i} " +
                                 "particles were dropped.");
                break;
            }

            Vector3 pos = positions[i];

            if (!IsInsideAquarium(pos))
            {
                outside++;
                continue;
            }

            _particles[_activeCount] = new Particle()
            {
                pos = pos,
                vel = velocity,
                predictedPos = pos,
                mass = 1f,
                isBoundary = 0,
                vorticity = float3.zero,
            };

            _activeCount++;
            spawned++;
        }

        UploadSpawned(spawned);

        if (outside > 0)
            Debug.LogWarning($"{outside} particles were outside the aquarium and not spawned.");

        return spawned;
    }

    // Sends the newly created particles to both buffers on the graphics card.
    private void UploadSpawned(int spawned)
    {
        if (spawned <= 0)
            return;

        int startIndex = _activeCount - spawned;
        _particleBufferIn.SetData(_particles, startIndex, startIndex, spawned);
        _particleBufferOut.SetData(_particles, startIndex, startIndex, spawned);
    }

    public int GetActiveCount() { return _activeCount; }

    public int GetBoundaryCount() { return _boundaryCount; }

    public int GetRenderCount() { return _activeCount - (renderBoundaryParticles ? 0 : _boundaryCount); }

    public void SetSpawnRef(Transform spawner) { spawnReference = spawner; }

    public void SetSpawnCount(int count) { spawnCount = count; }

    // Frees all the buffers when the scene is closed.
    private void OnDestroy()
    {

        _particleBufferIn?.Release();
        _particleBufferOut?.Release();
        _positionGraphicsBuffer?.Release();
        _gridCountsBuffer?.Release();
        _gridParticlesBuffer?.Release();
        _lambdaBuffer?.Release();
        _deltaQBuffer?.Release();
        _boxBuffer?.Release();
    }
}
