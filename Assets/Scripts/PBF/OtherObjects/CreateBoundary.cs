using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class CreateBoundary : MonoBehaviour
{
    [Header("Spacing")]
    [SerializeField] private GPUFluidSolver solver;
    [SerializeField] private float spacingOverride = 0.12f;

    [Header("Boundary Particles")]
    [SerializeField] private float mass = 1f;

    [Header("Sampling")]

    [SerializeField, Range(1f, 16f)] private float oversample = 4f;
    [SerializeField, Range(1, 4)] private int layers = 1;
    [SerializeField] private int seed = 12345;

    [Header("Gizmo Preview")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color gizmoColor = new(1f, 0.6f, 0.1f, 1f);
    [SerializeField] private int maxGizmos = 20000;

    private List<Vector3> _positions;

    private Vector3 _builtPosition;
    private Quaternion _builtRotation;
    private Vector3 _builtScale;
    private float _builtSpacing;

    public float Spacing => solver != null ? solver.RestSpacing : spacingOverride;

    public float Mass => mass;

    // Returns the boundary particle positions and rebuilds them if the object has moved.
    public List<Vector3> GetBoundaryPositions()
    {

        bool needsRebuild = _positions == null
            || _builtPosition != transform.position
            || _builtRotation != transform.rotation
            || _builtScale != transform.lossyScale
            || !Mathf.Approximately(_builtSpacing, Spacing);

        if (needsRebuild)
            Build();

        return _positions;
    }

    // Scatters points over every triangle of the mesh and snaps them onto a grid.
    private void Build()
    {
        _positions = new List<Vector3>();

        Mesh mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null)
        {
            Debug.LogWarning($"{name}: no mesh on the MeshFilter.", this);
            return;
        }

        if (!mesh.isReadable)
        {
            Debug.LogError($"{name}: mesh '{mesh.name}' is not readable.", this);
            return;
        }

        float spacing = Spacing;
        if (spacing <= 0f)
        {
            Debug.LogError($"{name}: spacing must be greater than 0.", this);
            return;
        }

        Matrix4x4 localToWorld = transform.localToWorldMatrix;
        Vector3[] vertices = mesh.vertices;
        int[] triangles = mesh.triangles;

        System.Random rng = new System.Random(seed);

        float cellArea = spacing * spacing;

        HashSet<Vector3Int> cells = new HashSet<Vector3Int>();

        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Vector3 a = localToWorld.MultiplyPoint3x4(vertices[triangles[t]]);
            Vector3 b = localToWorld.MultiplyPoint3x4(vertices[triangles[t + 1]]);
            Vector3 c = localToWorld.MultiplyPoint3x4(vertices[triangles[t + 2]]);

            Vector3 cross = Vector3.Cross(b - a, c - a);
            float area = cross.magnitude * 0.5f;
            if (area <= 1e-12f)
                continue;

            Vector3 inward = -cross.normalized;

            int samples = Mathf.Max(1, Mathf.CeilToInt(area / cellArea * oversample));

            for (int s = 0; s < samples; s++)
            {

                float u = (float)rng.NextDouble();
                float w = (float)rng.NextDouble();
                if (u + w > 1f)
                {
                    u = 1f - u;
                    w = 1f - w;
                }

                Vector3 point = a + u * (b - a) + w * (c - a);

                for (int l = 0; l < layers; l++)
                {
                    Vector3 layered = point + inward * (l * spacing);
                    cells.Add(Vector3Int.RoundToInt(layered / spacing));
                }
            }
        }

        foreach (Vector3Int cell in cells)
            _positions.Add((Vector3)cell * spacing);

        _builtPosition = transform.position;
        _builtRotation = transform.rotation;
        _builtScale = transform.lossyScale;
        _builtSpacing = spacing;
    }
}
