using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;
using System;

public class FluidSolver : MonoBehaviour
{

    public List<Particle> particles;

    public GameObject prefab;

    public GameObject boundaryPrefab;

    private Dictionary<Vector3Int, List<int>> _grid = new Dictionary<Vector3Int, List<int>>();

    private static readonly float cellSize = 1f;

    private float[] _lambda_i;

    private float[] _pCorrection;

    private Vector3[] _delta_q_i;

    private static readonly int SOLVERITERATIONS = 6;

    private static readonly float KERNELRADIUS = 1f;

    private static readonly float h = 1f;

    private static readonly float POW9H = h * h * h * h * h * h * h * h * h;

    private static readonly float KERNELCONSTANT = 315.0f / (64.0f * Mathf.PI * POW9H);

    private static readonly float KERNELGRADIENTCONSTANT = -945.0f / (32.0f * Mathf.PI * POW9H);

    private static readonly float RHO_0 = 2f;

    private static readonly float ONEOVERRHO_0 = 1f / RHO_0;

    private static readonly float POW2H = h * h;

    private static readonly float DELTA_q = 0.1f * h;

    private static readonly float sTERM = POW2H - (DELTA_q * DELTA_q);

    private static readonly float POW3sTERM = sTERM * sTERM * sTERM;

    private static readonly float SKERNELVALUE = KERNELCONSTANT * POW3sTERM;

    private static readonly float VORTICITY_STRENGTH = 0.1f;
    private static readonly float VISCOSITY_COEFF = 0.01f;

    public AquariumBoundary aquariumBoundary;
    private List<Vector3> _allBoundaryPos;

    [SerializeField]
    private float mass = 1f;

    // Sets up the particles and the boundary when the scene starts.
    void Start()
    {
        particles = new List<Particle>();

        _lambda_i = new float[particles.Count];
        _pCorrection = new float[particles.Count];
        _delta_q_i = new Vector3[particles.Count];

        this._allBoundaryPos = aquariumBoundary.GetAllBoundaryPos();
        InitializeBoundaryParticles();
    }

    // Reads the key presses for spawning water.
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            Debug.Log("Pressed E");
            SpawnParticleGrid(transform.position, new Vector3(2f, 1.5f, 2f), 0.7f);
        }
    }

    // Runs one simulation step at the fixed timestep.
    private void FixedUpdate()
    {
        if (_lambda_i == null || _lambda_i.Length < particles.Count)
        {
            _lambda_i = new float[particles.Count];
            _pCorrection = new float[particles.Count];
            _delta_q_i = new Vector3[particles.Count];
        }

        PredictPosition();

        BuildSpatialGrid();

        FindNeighbors();

        SolveConstraints();

        UpdateVelocities();

    }

    // Applies gravity and guesses where every particle will be.
    private void PredictPosition()
    {
        foreach (Particle p in particles)
        {
            if (p.GetIfBoundary()) continue;

            Vector3 velocity = p.GetVel();
            Vector3 gravity = new Vector3(0, -9.81f, 0);

            velocity += gravity * Time.fixedDeltaTime;
            Vector3 prevPos = p.GetPos();

            p.SetPredictedPos(prevPos + velocity * Time.fixedDeltaTime);
        }
    }

    // Sorts all particles into grid cells so that neighbors can be found quickly.
    private void BuildSpatialGrid()
    {
        _grid.Clear();

        for (int i = 0; i < particles.Count; i++)
        {
            Vector3Int cell = RoundCellCoords(particles[i].GetPredictedPos());

            if (!_grid.TryGetValue(cell, out List<int> cellList))
            {
                cellList = new List<int>();
                _grid[cell] = cellList;
            }
            cellList.Add(i);
        }
    }

    // Works out which grid cell a position belongs to.
    private Vector3Int RoundCellCoords(Vector3 pos)
    {
        return new Vector3Int(
            Mathf.FloorToInt(pos.x / cellSize),
            Mathf.FloorToInt(pos.y / cellSize),
            Mathf.FloorToInt(pos.z / cellSize)
        );

    }

    // Collects the neighbors of every particle from the 27 surrounding cells.
    private void FindNeighbors()
    {
        foreach (Particle p in particles)
            if (!p.GetIfBoundary()) p.ClearNeigborIndices();

        for (int i = 0; i < particles.Count; i++) {
            if (particles[i].GetIfBoundary()) continue;

            Vector3Int baseCell = RoundCellCoords(particles[i].GetPredictedPos());

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        Vector3Int cell = baseCell + new Vector3Int(x, y, z);

                        if (_grid.TryGetValue(cell, out List<int> cellParticles))
                        {
                            foreach (int j in cellParticles)
                            {
                                if (i == j) continue;

                                Vector3 diff = particles[i].GetPredictedPos() - particles[j].GetPredictedPos();

                                if (diff.sqrMagnitude < KERNELRADIUS * KERNELRADIUS)
                                {
                                    particles[i].AddToNeighbors(j);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    // Repeats the density correction a fixed number of times.
    private void SolveConstraints()
    {
        for (int iter = 0; iter < SOLVERITERATIONS; iter++)
        {
            CalculateLambda();

            CalculatePositionUpdate();

            UpdatePositions();
        }
    }

    // Calculates the density of every particle and how far it has to move.
    private void CalculateLambda()
    {

        if (_lambda_i == null || _lambda_i.Length != particles.Count)
            _lambda_i = new float[particles.Count];

        for (int i = 0; i < particles.Count; i++)
        {
            if (particles[i].GetIfBoundary())
            {
                _lambda_i[i] = 0.0f;
                continue;
            }

            float rho_i = 0.0f;

            float denominator = 0.0f;

            foreach (int neighborIndex in particles[i].GetNeigborsIndices())
            {
                Particle neighbor = particles[neighborIndex];

                Vector3 r_vec = particles[i].GetPredictedPos() - neighbor.GetPredictedPos();

                float r = r_vec.magnitude;

                if (r < 0.0001f) continue;

                float rSqr = r_vec.sqrMagnitude;

                if (rSqr >= POW2H) continue;

                if (r < h)
                {
                    float term = POW2H - (r * r);

                    if (term > 0.0f)
                    {
                        float kernelValue = KERNELCONSTANT * term * term * term;
                        rho_i += neighbor.GetMass() * kernelValue;

                        float gradientW_mag = KERNELGRADIENTCONSTANT * term * term;
                        Vector3 gradientW = gradientW_mag * r_vec;
                        Vector3 gradientC = -(particles[i].GetMass() / RHO_0) * gradientW;

                        denominator += gradientC.sqrMagnitude;
                    }
                }
            }

            float C_i = Mathf.Max(rho_i / RHO_0 - 1f, 0f);

            if (float.IsNaN(denominator) || float.IsInfinity(denominator)) denominator = 0.00001f;

            if (denominator < 1e-4f) denominator = 1e-4f;

            _lambda_i[i] = -C_i / denominator;

            _lambda_i[i] = Mathf.Clamp(_lambda_i[i], -1000f, 0f);

            if (float.IsNaN(_lambda_i[i]) || float.IsInfinity(_lambda_i[i])) _lambda_i[i] = 0f;
        }
    }

    // Works out the position correction from the particle and its neighbors.
    private void CalculatePositionUpdate()
    {
        if (_pCorrection == null || _pCorrection.Length != particles.Count)
            _pCorrection = new float[particles.Count];

        for (int i = 0; i < particles.Count; i++)
        {
            if (particles[i].GetIfBoundary())
            {
                _delta_q_i[i] = Vector3.zero;
                continue;
            }

            Vector3 Delta_q = Vector3.zero;

            List<int> neighbors = particles[i].GetNeigborsIndices();

            for (int j = 0; j < neighbors.Count; j++)
            {
                int neighborIndex = neighbors[j];
                Particle neighbor = particles[neighborIndex];

                Vector3 r_vec = particles[i].GetPredictedPos() - neighbor.GetPredictedPos();

                float r = r_vec.magnitude;

                if (r < 0.0001f) continue;

                if (r < h)
                {
                    float term = POW2H - (r * r);

                    if (term > 0f)
                    {
                        float gradientW_mag = KERNELGRADIENTCONSTANT * term * term;
                        Vector3 gradientW = gradientW_mag * r_vec;

                        float sCorr = CalculateSCorr(particles[i], neighbor);
                        float lambdaSum = neighbor.GetIfBoundary() ? _lambda_i[i] + sCorr : _lambda_i[i] + _lambda_i[neighborIndex] + sCorr;

                        if (!float.IsNaN(lambdaSum) && !float.IsInfinity(lambdaSum))
                        {
                            Delta_q += ONEOVERRHO_0 * lambdaSum * gradientW;
                        }
                    }
                }
            }

            if (float.IsNaN(Delta_q.x) || float.IsNaN(Delta_q.y) || float.IsNaN(Delta_q.z))
                Delta_q = Vector3.zero;

            _delta_q_i[i] = Delta_q;
        }
    }

    // Keeps the particles inside the tank.
    private void HandleCollision()
    {
        foreach (Particle p in particles)
        {
            for (int j = 0; j < p.GetNeigborsIndices().Count; j++)
            {
                int neighborIndex = p.GetNeigborsIndices()[j];

                Particle neighbor = particles[neighborIndex];

                Vector3 diff = p.GetPredictedPos() - neighbor.GetPredictedPos();
                if (diff.sqrMagnitude < 0.0001f) continue;
                Vector3 normal = diff.normalized;

                float distance = Vector3.Dot(diff, normal);

                if (distance < h && distance > -h )
                {
                    float penetrationDepth = h - Math.Abs(distance);

                    p.SetPos(p.GetPredictedPos() + (normal * penetrationDepth) * Time.fixedDeltaTime);

                    if (Vector3.Dot(p.GetVel(), normal) < 0)
                    {
                        p.SetVel(p.GetVel() - normal * Vector3.Dot(p.GetVel(), normal) * Time.fixedDeltaTime);
                    }
                }
            }
        }
    }

    // Calculates the extra repulsion that stops particles from clumping together.
    private float CalculateSCorr(Particle p, Particle neighbor)
    {
        Vector3 r_vec = p.GetPredictedPos() - neighbor.GetPredictedPos();

        float r = r_vec.magnitude;
        float s_corr = 0f;

        if (r < h)
        {
            float term = POW2H - (r * r);

            if (term > 0.0f)
            {
                float kernelValue = KERNELCONSTANT * term * term * term;

                s_corr = -0.1f * Mathf.Pow((kernelValue / SKERNELVALUE), 4);
            }
        }

        return s_corr;
    }

    // Applies the corrections that were worked out before.
    private void UpdatePositions()
    {
        for (int i = 0; i < particles.Count; i++)
        {
            if (particles[i].GetIfBoundary()) continue;

            particles[i].SetPredictedPos(particles[i].GetPredictedPos() + _delta_q_i[i]);
        }
    }

    // Works out the new speed from how far a particle moved, then adds viscosity and vorticity.
    private void UpdateVelocities()
    {
        for (int i = 0; i < particles.Count; i++)
        {
            if (!particles[i].GetIfBoundary())
                particles[i].SetVorticity(CalculateVorticity(i));
        }

        for (int i = 0; i < particles.Count; i++)
        {
            if (particles[i].GetIfBoundary()) continue;

            Vector3 velocityFromPosition = (particles[i].GetPredictedPos() - particles[i].GetPos()) / Time.fixedDeltaTime;

            velocityFromPosition += CalculateXSPHViscosity(i);

            Vector3 correctiveForce = CalculateCorrectiveForce(i);

            velocityFromPosition += correctiveForce * Time.fixedDeltaTime;

            particles[i].SetVel(velocityFromPosition);

            particles[i].SetPos(particles[i].GetPredictedPos());
        }

    }

    // Measures how strongly the fluid turns around a particle.
    private Vector3 CalculateVorticity(int i)
    {
        Vector3 omega_i = Vector3.zero;

        List<int> neighbors = particles[i].GetNeigborsIndices();

        for (int j = 0; j < neighbors.Count; j++)
        {
            int neighborIndex = neighbors[j];
            Particle neighbor = particles[neighborIndex];

            Vector3 v_ij = neighbor.GetVel() - particles[i].GetVel();

            Vector3 r_vec = particles[i].GetPredictedPos() - neighbor.GetPredictedPos();

            float r = r_vec.magnitude;

            if (r < h)
            {
                float term = POW2H - (r * r);

                if (term > 0.0f)
                {
                    float gradientW_mag = KERNELGRADIENTCONSTANT * term * term;
                    Vector3 gradientW = gradientW_mag * r_vec;

                    omega_i += Vector3.Cross(gradientW, v_ij);
                }
            }
        }

        return omega_i;
    }

    // Calculates the force that adds the lost swirling back.
    private Vector3 CalculateCorrectiveForce(int i)
    {
        Vector3 eta = CalculateEta(i);

        if (eta.sqrMagnitude < 0.0001f) return Vector3.zero;

        Vector3 locationVector = eta.normalized;

        return VORTICITY_STRENGTH * (Vector3.Cross(locationVector, particles[i].GetVorticity()));
    }

    // Finds the direction in which the swirling gets stronger.
    private Vector3 CalculateEta(int i)
    {
        Vector3 eta = Vector3.zero;
        float omega_i_mag = particles[i].GetVorticity().magnitude;

        List<int> neighbors = particles[i].GetNeigborsIndices();

        for (int j = 0; j < neighbors.Count; j++)
        {
            int neighborIndex = neighbors[j];
            Particle neighbor = particles[neighborIndex];

            float omega_j_mag = neighbor.GetVorticity().magnitude;

            float diff = omega_j_mag - omega_i_mag;

            Vector3 r_vec = particles[i].GetPredictedPos() - neighbor.GetPredictedPos();

            float r = r_vec.magnitude;

            if (r < h)
            {
                float term = POW2H - (r * r);

                if (term > 0.0f)
                {
                    float gradientW_mag = KERNELGRADIENTCONSTANT * term * term;
                    Vector3 gradientW = gradientW_mag * r_vec;

                    eta += diff * gradientW;
                }
            }
        }

        return eta;
    }

    // Pulls the speed of a particle towards the speed of its neighbors.
    private Vector3 CalculateXSPHViscosity(int i)
    {
        Vector3 viscositySum = Vector3.zero;

        List<int> neighbors = particles[i].GetNeigborsIndices();

        for (int j = 0; neighbors.Count > j; j++)
        {
            int neighborIndex = neighbors[j];
            Particle neighbor = particles[neighborIndex];

            Vector3 v_ij = neighbor.GetVel() - particles[i].GetVel();

            Vector3 r_vec = particles[i].GetPredictedPos() - neighbor.GetPredictedPos();

            float r = r_vec.magnitude;

            if (r < h)
            {
                float term = POW2H - (r * r);

                if (term > 0f)
                {
                    float kernelValue = KERNELCONSTANT * term * term * term;

                    viscositySum += v_ij * kernelValue;
                }
            }
        }

        return VISCOSITY_COEFF * viscositySum;
    }

    // Creates the first particles of the scene.
    private void InitializeParticle()
    {
        Particle dataParticle = new Particle(gameObject.transform.position, Vector3.zero, 1f, false);
        particles.Add(dataParticle);

        GameObject visualObject = Instantiate(prefab, gameObject.transform.position, Quaternion.identity);

        ParticleRenderer view = visualObject.GetComponent<ParticleRenderer>();

    }

    // Creates the static particles of the tank walls.
    private void InitializeBoundaryParticles()
    {
        foreach (var pos in _allBoundaryPos)
        {
            Particle dataParticle = new Particle(pos, Vector3.zero, 0.5f, true);
            particles.Add(dataParticle);

        }
    }

    // Spawns a block of particles in a regular grid.
    public void SpawnParticleGrid(Vector3 center, Vector3 size, float spacing)
    {
        if (spacing <= 0f || spacing > h)
        {
            Debug.LogError($"Invalid spacing: {spacing}. Must be 0 < spacing <= h ({h})");
            return;
        }

        int countX = Mathf.CeilToInt(size.x / spacing);
        int countY = Mathf.CeilToInt(size.y / spacing);
        int countZ = Mathf.CeilToInt(size.z / spacing);

        Vector3 halfSize = size * 0.5f;
        float jitterAmount = spacing * 0.1f;

        int newCount = countX * countY * countZ;
        int startIndex = particles.Count;

        for (int x = 0; x < countX; x++)
        {
            for (int y = 0; y < countY; y++)
            {
                for (int z = 0; z < countZ; z++)
                {

                    Vector3 gridPos = new Vector3(
                        (x + 0.5f) * spacing - halfSize.x,
                        (y + 0.5f) * spacing - halfSize.y,
                        (z + 0.5f) * spacing - halfSize.z
                    );

                    Vector3 jitter = new Vector3(
                        UnityEngine.Random.Range(-jitterAmount, jitterAmount),
                        UnityEngine.Random.Range(-jitterAmount, jitterAmount),
                        UnityEngine.Random.Range(-jitterAmount, jitterAmount)
                    );

                    Vector3 finalPos = center + gridPos + jitter;

                    Particle p = new Particle(finalPos, Vector3.zero, this.mass, false);
                    particles.Add(p);

                    GameObject visual = Instantiate(prefab, finalPos, Quaternion.identity);

                }
            }
        }

        ResizeSolverArrays();

        Debug.Log($"Spawned {newCount} particles. Total: {particles.Count}");
    }

    // Makes the working arrays big enough for the current particle count.
    private void ResizeSolverArrays()
    {
        int count = particles.Count;
        if (_lambda_i == null || _lambda_i.Length < count) _lambda_i = new float[count];
        if (_pCorrection == null || _pCorrection.Length < count) _pCorrection = new float[count];
        if (_delta_q_i == null || _delta_q_i.Length < count) _delta_q_i = new Vector3[count];
    }

    public int GetParticlesCount() { return particles.Count - this._allBoundaryPos.Count; }

}
