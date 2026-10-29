# PBFUnity - Position Based Fluids Framework in Unity

Bachelor's thesis by Mert Kaynak, Computer Graphics and Virtual Reality group,
University of Bremen.

Fluid simulation is expensive, and game engines run at a fixed timestep that is too large for
classical Smoothed Particle Hydrodynamics to stay stable. Position Based Fluids avoids this by
correcting particle positions against a density constraint instead of integrating pressure forces.

This thesis presents PBFUnity, an implementation of Position Based Fluids in Unity 6 that runs
entirely on the graphics card. It consists of a solver of nine compute kernels, a screen space
renderer that reconstructs a fluid surface from particle depth, two level of detail systems and a
benchmark harness. No particle data is sent back to the processor while a scene is running.

I measured the framework on six machines with graphics cards from an RTX 3060 up to an RTX 4090,
across ten scenes ranging from an empty tank to 127,526 particles, each run with the level of
detail switched on and off. From the RTX 3080 upward it holds 60 frames per second in nine of the
ten scenes, and on the RTX 4090 in all ten. The level of detail improves the result only where
frame times already lie close to the target.

## Description

> **Caution when using this program.** Please make sure that your system is able to run it,
> otherwise your system could crash or overheat. The program requires Windows and a graphics card
> with Direct3D 12 support. Check the tables below against your own system first.

The Unity program has **two modes**: the **benchmark** and the **testing area**. Both are reachable
from inside the program, by pressing the **return key** to start the benchmark or the **tab key**
to enter the testing area.

While the benchmark is running it is best to do exactly **nothing**, so that the results are not
influenced. The results are saved as CSV files in a **Benchmarks** folder next to the program,
which is created on the first run: one file per scene and a summary of the whole run.

In the testing area you can spawn water particles at different positions and find the limits of
your own system that way. The controls are shown on screen.

### The benchmark scenes

| # | Scene | Particles | Water | Solid objects | Duration |
|---|---|---|---|---|---|
| 1 | EmptyAquarium | 0 | none | none | 5 s |
| 2 | RainyDay | 40,000 | 12,000 to 40,000 (2.8 s) | 16 boxes | 15 s |
| 3 | SimpleSlide | 40,000 | 20,000 to 40,000 (0.5 s) | 2 boxes | 12 s |
| 4 | Tsunami | 50,000 | all from the start | 10 boxes | 8 s |
| 5 | Meteoroids | 55,188 | all from the start | 6 boxes | 8 s |
| 6 | ScalingAquarium | 55,188 | all from the start | none | 15 s |
| 7 | FlowingDam | 70,000 | 7,000 to 70,000 (21.5 s) | 11 boxes | 50 s |
| 8 | BunnyInTheMiddle | 85,189 | 5,000 to 80,000 (19 s) | Bunny mesh, 5,189 boundary particles | 30 s |
| 9 | BunnyDrop | 79,032 | 34,575 to 79,032 (16.5 s) | none | 30 s |
| 10 | ArmadilloSplash | 127,526 | 60,000 to 120,000 (1.5 s) | Armadillo mesh, 7,526 boundary particles | 22 s |

The particle count includes the boundary particles. The water column counts fluid only.

## Results

Level of Detail is enabled for the results and the tested resolution is Full HD (1920x1080).
All systems passed the test except the system with the RTX 3060. In the last scene, the RTX 3080
and RTX 4070 Ti could not keep up with real time and ran the simulation at 0.82 and 0.91 of real
speed.

The full tables and charts are on the
[project page](https://cgvr.cs.uni-bremen.de/theses/finishedtheses/PBFUnity/), together with
videos of the benchmark and the testing area.

## Repository layout

| Path | Contents |
|---|---|
| `Assets/Scripts/PBF/ParticleScripts/FluidSolver.compute` | The solver, nine compute kernels |
| `Assets/Scripts/PBF/ParticleScripts/GPUFluidSolver.cs` | Sets up the buffers and dispatches the kernels |
| `Assets/Scripts/PBF/SSFR/` | The screen space fluid renderer, four shaders |
| `Assets/Scripts/PBF/Benchmark/` | The benchmark harness |
| `Assets/Scripts/PBF/ParticleScripts/CPUPrototype/` | An earlier CPU implementation, kept for comparison. Not used by any benchmark scene. |

## Files

The thesis, the defense presentation and the compiled program are on the
[project page](https://cgvr.cs.uni-bremen.de/theses/finishedtheses/PBFUnity/).

- Full version of the bachelor's thesis (English only)
- Presentation of the thesis defense (German only)
- The compiled program, as `PBFUnity.rar`. Use the included batch files to start it.

## Third-party content

- **Stanford Bunny** and **Armadillo** meshes, used courtesy of the
  [Stanford Computer Graphics Laboratory](http://graphics.stanford.edu/data/3Dscanrep/).
- **`FpsToText.cs`**, an on-screen frame rate counter in the `Cleverous.Shared` namespace,
  by its original author. Not written by me.
- **TextMesh Pro**, which ships with Unity.

## License

This original work is copyright by University of Bremen.

Any software of this work is covered by the **European Union Public Licence v1.2**, included in
this repository as [`LICENSE`](LICENSE). To view a copy of this license, visit
<https://eur-lex.europa.eu/eli/dec_impl/2017/863/oj>.

The thesis, provided as a PDF file on the project page, is licensed under
**Attribution-NonCommercial-NoDerivatives 4.0 International**
(<http://creativecommons.org/licenses/by-nc-nd/4.0/>).

Any other assets, such as 3D models, movies and documents, are covered by the Creative Commons
**Attribution-NonCommercial-ShareAlike 4.0 International License**
(<http://creativecommons.org/licenses/by-nc-sa/4.0/>).

If you use any of the assets or software to produce a publication, then you must give credit and
put a reference in your publication.

If you would like to use this software in proprietary software, you can obtain an exception from
the above license, also known as dual licensing. Please contact zach at cs.uni-bremen dot de.
