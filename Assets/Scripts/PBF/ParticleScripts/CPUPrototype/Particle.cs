using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class Particle
{
    private Vector3 _pos;
    private Vector3 _vel;
    private Vector3 _predictedPosition;
    private readonly float mass;
    private Vector3 vorticity;

    private List<int> _neighborIndices;

    private bool _isBoundary;

    // Creates a particle with its starting values.
    public Particle(Vector3 pos, Vector3 vel, float mass, bool isBoundary)
    {
        this._pos = pos;
        this._vel = vel;
        this._predictedPosition = pos;
        this.mass = mass;
        this.vorticity = Vector3.zero;

        this._neighborIndices = new List<int>();
        this._isBoundary = isBoundary;
    }

    public Vector3 GetPos() {  return _pos; }

    public void SetPos(Vector3 pos)
    {

        if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
        {
            Debug.LogWarning("Attempted to set NaN position on particle!");
            return;
        }
        _pos = pos;
    }

    public Vector3 GetVel() { return _vel; }

    public void SetVel(Vector3 vel) { _vel = vel; }

    public void AddToNeighbors(int i) { _neighborIndices.Add(i); }

    public List<int> GetNeigborsIndices() { return _neighborIndices; }

    public int GetNeighborIndex(int i) {  return _neighborIndices[i]; }

    public void ClearNeigborIndices() { _neighborIndices.Clear(); }

    public void SetNeighborIndices(List<int> list) { _neighborIndices = list; }

    public Vector3 GetPredictedPos() { return _predictedPosition; }

    public void SetPredictedPos(Vector3 pos)
    {

        if (float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
        {
            Debug.LogWarning("Attempted to set NaN predicted position on particle!");
            return;
        }
        _predictedPosition = pos;
    }

    public float GetMass() { return mass; }

    public Vector3 GetVorticity() { return vorticity; }

    public void SetVorticity(Vector3 vorticity) { this.vorticity = vorticity; }

    public bool GetIfBoundary() { return _isBoundary; }
}
