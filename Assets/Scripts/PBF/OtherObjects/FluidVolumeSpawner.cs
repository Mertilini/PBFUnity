using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(50)]
public class FluidVolumeSpawner : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GPUFluidSolver solver;
    [Tooltip("Prefab whose mesh interior is filled with water. Its own transform is applied, " +
             "then this GameObject's transform places/rotates/scales the result in the scene.")]
    [SerializeField] private GameObject prefab;

    [Header("Spawning")]
    [SerializeField] private KeyCode spawnKey = KeyCode.B;
    [SerializeField] private bool spawnOnStart = false;
    [Tooltip("Seconds to wait before the automatic spawn - handy for dropping into settled water.")]
    [SerializeField] private float startDelay = 0f;
    [SerializeField] private Vector3 initialVelocity = Vector3.zero;

    [Header("Gizmo Preview")]
    [SerializeField] private bool drawGizmos = true;
    [Tooltip("Also sample and draw the particles themselves. Off by default - resampling a " +
             "high-poly mesh while dragging the object in the editor is not free.")]
    [SerializeField] private bool previewParticles = false;
    [SerializeField] private Color gizmoColor = new(0.2f, 0.6f, 1f, 1f);
    [SerializeField] private int maxGizmos = 20000;

    private List<Vector3> _preview;

    private Vector3 _builtPosition;
    private Quaternion _builtRotation;
    private Vector3 _builtScale;
    private float _builtSpacing;
    private GameObject _builtPrefab;

    private float Spacing => solver != null ? solver.RestSpacing : 0f;

    // Finds the solver and spawns the water if the scene is set up that way.
    private void Start()
    {
        if (solver == null)
            solver = FindFirstObjectByType<GPUFluidSolver>();

        if (spawnOnStart)
            StartCoroutine(SpawnAfterDelay());
    }

    // Spawns the water when the spawn key is pressed.
    private void Update()
    {
        if (Input.GetKeyDown(spawnKey))
            Spawn();
    }

    // Waits for the start delay and then spawns the water.
    private IEnumerator SpawnAfterDelay()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        Spawn();
    }

    // Fills the mesh with particles and hands them to the solver.
    public void Spawn()
    {
        if (solver == null)
        {
            Debug.LogError($"{name}: no GPUFluidSolver assigned.", this);
            return;
        }

        List<Vector3> positions = Sample();
        if (positions == null || positions.Count == 0)
        {
            Debug.LogWarning($"{name}: the mesh volume sampled to 0 particles. Is the mesh " +
                             "closed, and is it larger than one particle spacing?", this);
            return;
        }

        int spawned = solver.SpawnFluidParticles(positions, initialVelocity);
        Debug.Log($"{name}: spawned {spawned} fluid particles from '{prefab.name}' " +
                  $"(sampled {positions.Count} at spacing {Spacing:F4}).");
    }

    // Finds the grid points inside the mesh by checking where its surface is crossed in each column.
    private List<Vector3> Sample()
    {
        List<Vector3> result = new List<Vector3>();

        if (prefab == null)
        {
            Debug.LogError($"{name}: no prefab assigned.", this);
            return result;
        }

        MeshFilter meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh == null)
        {
            Debug.LogError($"{name}: prefab '{prefab.name}' has no MeshFilter with a mesh.", this);
            return result;
        }

        if (!mesh.isReadable)
        {
            Debug.LogError($"{name}: mesh '{mesh.name}' is not readable. Enable Read/Write in " +
                           "its import settings.", this);
            return result;
        }

        float spacing = Spacing;
        if (spacing <= 0f)
        {
            Debug.LogError($"{name}: spacing must be greater than 0.", this);
            return result;
        }

        Matrix4x4 model = transform.localToWorldMatrix * meshFilter.transform.localToWorldMatrix;

        Vector3[] localVertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        Vector3[] vertices = new Vector3[localVertices.Length];
        Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

        for (int i = 0; i < localVertices.Length; i++)
        {
            Vector3 v = model.MultiplyPoint3x4(localVertices[i]);
            vertices[i] = v;
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        int minCellX = Mathf.CeilToInt(min.x / spacing);
        int maxCellX = Mathf.FloorToInt(max.x / spacing);
        int minCellZ = Mathf.CeilToInt(min.z / spacing);
        int maxCellZ = Mathf.FloorToInt(max.z / spacing);

        int countX = maxCellX - minCellX + 1;
        int countZ = maxCellZ - minCellZ + 1;
        if (countX <= 0 || countZ <= 0)
            return result;

        List<Crossing>[] columns = new List<Crossing>[countX * countZ];

        float jitterX = spacing * 0.0013f;
        float jitterZ = spacing * 0.0007f;

        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Vector3 a = vertices[triangles[t]];
            Vector3 b = vertices[triangles[t + 1]];
            Vector3 c = vertices[triangles[t + 2]];

            float area2 = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
            if (Mathf.Abs(area2) < 1e-12f)
                continue;

            int direction = area2 > 0f ? 1 : -1;

            int startX = Mathf.Max(minCellX, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / spacing));
            int endX = Mathf.Min(maxCellX, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / spacing));
            int startZ = Mathf.Max(minCellZ, Mathf.CeilToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / spacing));
            int endZ = Mathf.Min(maxCellZ, Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / spacing));

            for (int ix = startX; ix <= endX; ix++)
            {
                float px = ix * spacing + jitterX;

                for (int iz = startZ; iz <= endZ; iz++)
                {
                    float pz = iz * spacing + jitterZ;

                    float w0 = (b.x - px) * (c.z - pz) - (b.z - pz) * (c.x - px);
                    float w1 = (c.x - px) * (a.z - pz) - (c.z - pz) * (a.x - px);
                    float w2 = (a.x - px) * (b.z - pz) - (a.z - pz) * (b.x - px);

                    if (area2 > 0f)
                    {
                        if (w0 < 0f || w1 < 0f || w2 < 0f) continue;
                    }
                    else
                    {
                        if (w0 > 0f || w1 > 0f || w2 > 0f) continue;
                    }

                    float y = (w0 * a.y + w1 * b.y + w2 * c.y) / area2;

                    int index = (ix - minCellX) * countZ + (iz - minCellZ);
                    columns[index] ??= new List<Crossing>(8);
                    columns[index].Add(new Crossing(y, direction));
                }
            }
        }

        for (int ix = minCellX; ix <= maxCellX; ix++)
        {
            float x = ix * spacing;

            for (int iz = minCellZ; iz <= maxCellZ; iz++)
            {
                List<Crossing> crossings = columns[(ix - minCellX) * countZ + (iz - minCellZ)];
                if (crossings == null || crossings.Count < 2)
                    continue;

                crossings.Sort(ByHeight);

                float z = iz * spacing;
                int winding = 0;

                for (int i = 0; i < crossings.Count - 1; i++)
                {
                    winding += crossings[i].Direction;
                    if (winding == 0)
                        continue;

                    int startY = Mathf.CeilToInt(crossings[i].Y / spacing);
                    int endY = Mathf.FloorToInt(crossings[i + 1].Y / spacing);

                    for (int iy = startY; iy <= endY; iy++)
                        result.Add(new Vector3(x, iy * spacing, z));
                }
            }
        }

        return result;
    }

    private readonly struct Crossing
    {
        public readonly float Y;
        public readonly int Direction;

        public Crossing(float y, int direction)
        {
            Y = y;
            Direction = direction;
        }
    }

    private static readonly System.Comparison<Crossing> ByHeight = (a, b) => a.Y.CompareTo(b.Y);

    // Draws the sampled points in the editor.
    private void OnDrawGizmos()
    {
        if (!drawGizmos || prefab == null || solver == null)
            return;

        MeshFilter meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
        if (mesh == null)
            return;

        Matrix4x4 model = transform.localToWorldMatrix * meshFilter.transform.localToWorldMatrix;

        Gizmos.color = gizmoColor;
        Gizmos.matrix = model;
        Gizmos.DrawWireCube(mesh.bounds.center, mesh.bounds.size);
        Gizmos.matrix = Matrix4x4.identity;

        if (!previewParticles)
            return;

        bool needsRebuild = _preview == null
            || _builtPrefab != prefab
            || _builtPosition != transform.position
            || _builtRotation != transform.rotation
            || _builtScale != transform.lossyScale
            || !Mathf.Approximately(_builtSpacing, Spacing);

        if (needsRebuild)
        {
            _preview = Sample();
            _builtPrefab = prefab;
            _builtPosition = transform.position;
            _builtRotation = transform.rotation;
            _builtScale = transform.lossyScale;
            _builtSpacing = Spacing;
        }

        int drawn = Mathf.Min(_preview.Count, maxGizmos);
        float size = Spacing * 0.5f;

        for (int i = 0; i < drawn; i++)
            Gizmos.DrawCube(_preview[i], Vector3.one * size);
    }
}
