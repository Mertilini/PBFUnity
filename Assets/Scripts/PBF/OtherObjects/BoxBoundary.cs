using UnityEngine;

public class BoxBoundary : MonoBehaviour
{
    [Tooltip("Size of the box before scaling. A default Unity Cube is 1 x 1 x 1, so leave this " +
             "alone and size the object with its Transform scale.")]
    [SerializeField] private Vector3 localSize = Vector3.one;

    [Header("Gizmo")]
    [SerializeField] private bool drawGizmo = true;
    [SerializeField] private Color gizmoColor = new(0.9f, 0.4f, 0.2f, 0.5f);

    public Vector3 HalfExtents => Vector3.Scale(localSize, transform.lossyScale) * 0.5f;

    public Matrix4x4 BoxToWorld => Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

    // Draws the box in the editor so that its size can be seen.
    private void OnDrawGizmos()
    {
        if (!drawGizmo)
            return;

        Gizmos.color = gizmoColor;
        Gizmos.matrix = BoxToWorld;
        Gizmos.DrawWireCube(Vector3.zero, HalfExtents * 2f);
    }
}
