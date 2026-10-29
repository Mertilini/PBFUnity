using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;

public class AquariumBoundary : MonoBehaviour
{
    private List<Vector3> _allBoundaryPos;

    // Builds the boundary particle positions for the floor and the four walls.
    public List<Vector3> GetAllBoundaryPos()
    {
        Vector3 startPos = gameObject.transform.position;

        int x = 10;
        int y = 8;
        int z = 10;

        float density = 0.33f;

        _allBoundaryPos = new List<Vector3>();

        CreateFloor(x, z, startPos, density);
        CreateWalls(x, y, z, startPos, density);

        return _allBoundaryPos;
    }

    // Lays out a grid of points on the floor.
    private void CreateFloor(int x, int z, Vector3 startPos, float density)
    {
        float h = gameObject.transform.position.y;
        for (float i = 0; i < x; i = i + density)
        {
            for (float j = 0; j < z; j = j + density)
            {
                _allBoundaryPos.Add(new Vector3(startPos.x + i, h, startPos.z + j));
            }
        }
    }

    // Lays out points on all four walls.
    private void CreateWalls(int x, int y, int z, Vector3 startPos, float density)
    {
        float h = gameObject.transform.position.y;

        for (float i = 0; i < x; i = i + density)
        {
            for (float j = density; j <= y; j = j + density)
            {
                _allBoundaryPos.Add(new Vector3(startPos.x + i, h + j, startPos.z));
            }
        }

        for (float i = 0; i < z; i = i + density)
        {
            for (float j = density; j <= y; j = j + density)
            {
                _allBoundaryPos.Add(new Vector3(startPos.x, h + j, startPos.z + i));
            }
        }

        for (float i = 0; i < x; i = i + density)
        {
            for (float j = density; j <= y; j = j + density)
            {
                _allBoundaryPos.Add(new Vector3(startPos.x + i, h + j, startPos.z + z));
            }
        }

        for (float i = 0; i < z; i = i + density)
        {
            for (float j = density; j <= y; j = j + density)
            {
                _allBoundaryPos.Add(new Vector3(startPos.x + x, h + j, startPos.z + i));
            }
        }
    }

    // Draws the tank as a wireframe box in the editor.
    private void OnDrawGizmos()
    {
        Vector3 pos = transform.position;
        int x = 10, y = 8, z = 10;

        Gizmos.color = Color.cyan;

        Gizmos.DrawLine(new Vector3(pos.x, pos.y, pos.z), new Vector3(pos.x + x, pos.y, pos.z));
        Gizmos.DrawLine(new Vector3(pos.x + x, pos.y, pos.z), new Vector3(pos.x + x, pos.y, pos.z + z));
        Gizmos.DrawLine(new Vector3(pos.x + x, pos.y, pos.z + z), new Vector3(pos.x, pos.y, pos.z + z));
        Gizmos.DrawLine(new Vector3(pos.x, pos.y, pos.z + z), new Vector3(pos.x, pos.y, pos.z));

        Gizmos.DrawLine(new Vector3(pos.x, pos.y, pos.z), new Vector3(pos.x, pos.y + y, pos.z));
        Gizmos.DrawLine(new Vector3(pos.x + x, pos.y, pos.z), new Vector3(pos.x + x, pos.y + y, pos.z));
        Gizmos.DrawLine(new Vector3(pos.x, pos.y, pos.z + z), new Vector3(pos.x, pos.y + y, pos.z + z));
        Gizmos.DrawLine(new Vector3(pos.x + x, pos.y, pos.z + z), new Vector3(pos.x + x, pos.y + y, pos.z + z));

        Gizmos.DrawLine(new Vector3(pos.x, pos.y + y, pos.z), new Vector3(pos.x + x, pos.y + y, pos.z));
        Gizmos.DrawLine(new Vector3(pos.x + x, pos.y + y, pos.z), new Vector3(pos.x + x, pos.y + y, pos.z + z));
        Gizmos.DrawLine(new Vector3(pos.x + x, pos.y + y, pos.z + z), new Vector3(pos.x, pos.y + y, pos.z + z));
        Gizmos.DrawLine(new Vector3(pos.x, pos.y + y, pos.z + z), new Vector3(pos.x, pos.y + y, pos.z));
    }
}
