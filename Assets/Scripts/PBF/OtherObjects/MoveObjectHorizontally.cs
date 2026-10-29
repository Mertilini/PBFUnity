using UnityEngine;

[DefaultExecutionOrder(-10)]
public class MoveObjectHorizontally : MonoBehaviour
{
    [SerializeField] private AquariumGPU aquarium;

    [Tooltip("World units per simulated second.")]
    [SerializeField] private float speed = 30f;

    // Moves the object with the arrow keys and resets it if it leaves the tank.
    private void FixedUpdate()
    {
        bool isOutside = transform.position.y < aquarium.BoundsMin.y || transform.position.y > aquarium.BoundsMax.y || transform.position.x < aquarium.BoundsMin.x || transform.position.x > aquarium.BoundsMax.x || transform.position.z < aquarium.BoundsMin.z || transform.position.z > aquarium.BoundsMax.z;
        if (isOutside) transform.position = new Vector3(0, 30f, 0);

        float step = speed * Time.fixedDeltaTime;

        if (Input.GetKey(KeyCode.UpArrow) && !isOutside)
        {
            transform.position = transform.position + new Vector3(0, 0, step);
        }
        else if (Input.GetKey(KeyCode.DownArrow) && !isOutside)
        {
            transform.position = transform.position + new Vector3(0, 0, -step);
        }
        else if ( Input.GetKey(KeyCode.LeftArrow) && !isOutside)
        {
            transform.position = transform.position + new Vector3(-step, 0, 0);
        }
        else if (Input.GetKey(KeyCode.RightArrow) && !isOutside)
        {
            transform.position = transform.position + new Vector3(step, 0, 0);
        }
    }
}
