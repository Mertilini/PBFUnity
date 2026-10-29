using UnityEngine;

public class ParticleRenderer : MonoBehaviour
{
    private GPUFluidSolver _solver;

    // Stores the solver that this renderer belongs to.
    public void Initialize(GPUFluidSolver solver)
    {
        _solver = solver;
    }

    void Update()
    {
        
    }
}