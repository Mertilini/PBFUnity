using UnityEngine;
using UnityEngine.InputSystem.Processors;

[DefaultExecutionOrder(-10)]
public class AquariumGPU : MonoBehaviour
{
    [Header("Aquarium Dimensions")]
    public Vector3 size = new Vector3(4f, 5f, 3f);
    public Color gizmoColor = new Color(0.2f, 0.6f, 1.0f, 0.5f);

    [Header("Boundary Physics")]
    [Range(0f, 1f)] public float restitution = 0.0f;
    [Range(0f, 1f)] public float friction = 1f;

    [Header("Scaling")]
    public bool isScaling = false;
    public int scalingDistance = 200;
    private int scalingDecreaser = 0;
    private int scalingIncreaser;

    private Vector3 boundsMin;
    private Vector3 boundsMax;

    public Vector3 BoundsMin => boundsMin;
    public Vector3 BoundsMax => boundsMax;

    // Works out the tank bounds once when the scene starts.
    private void Awake()
    {
        UpdateBounds();
        scalingIncreaser = scalingDistance;
    }

    // Recalculates the bounds if the tank was moved or resized.
    void Update()
    {
        if (transform.hasChanged)
        {
            UpdateBounds();
            transform.hasChanged = false;
        }
    }

    // Lets the tank slowly grow and shrink if scaling is switched on.
    private void FixedUpdate()
    {
        if (!isScaling)
            return;

        scaleSidesSmoothly(scalingDistance);

        UpdateBounds();
    }

    // Calculates the lowest and the highest corner of the tank.
    void UpdateBounds()
    {
        Vector3 volumeCenter = transform.position + new Vector3(0, size.y * 0.5f, 0);

        boundsMin = volumeCenter - size * 0.5f;
        boundsMax = volumeCenter + size * 0.5f;
    }

    // Draws the tank and its floor in the editor.
    void OnDrawGizmos()
    {
        UpdateBounds();

        Gizmos.color = gizmoColor;
        Gizmos.DrawWireCube(transform.position + new Vector3(0, size.y * 0.5f, 0), size);

        Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.8f);
        Gizmos.DrawWireCube(transform.position, new Vector3(size.x, 0.1f, size.z));
    }

    // Moves one side of the tank inwards and out again, a small step at a time.
    private void scaleSidesSmoothly(int distance)
    {
        if (scalingDecreaser <= distance)
        {
            size = size + new Vector3(-0.2f * (scalingDecreaser / (float)distance), 0, 0);
            scalingDecreaser++;
        }
        else if (scalingIncreaser <= distance && scalingIncreaser >= 0)
        {
            size = size + new Vector3(0.2f * ((distance - scalingIncreaser) / (float)distance), 0, 0);
            scalingIncreaser--;
        }
        else
        {
            scalingIncreaser = scalingDistance;
            scalingDecreaser = 0;
        }
    }
}
