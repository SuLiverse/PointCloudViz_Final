# Verification Record

Verified locally on Windows on 2026-10-07. This is a test receipt, not a claim of universal hardware compatibility.

## Gates

Command: `scripts/verify.ps1 -Smoke`

| Gate | Result |
| --- | --- |
| Locked NuGet restore | Passed |
| Transitive NuGet audit | No reported advisory after legacy package pins |
| Release build with warnings as errors | 0 warnings, 0 errors |
| xUnit regression tests | 65 passed, 0 failed, 0 skipped |
| Native WPF / DirectX smoke assertions | 26 passed |
| Native viewport pixels | Nonblank colored geometry verified |
| Camera and RGB mode | Rendered pixel buffers change |
| Point picking | Uses the real renderer camera matrices |
| Snapshot | Data, checksum, measurements, camera, color and point size verified |
| Failed import and cancellation | Active cloud is preserved |
| Empty filter result | Clears GPU geometry and remains undoable |
| Desktop / compact windows | 1440 x 900 and 1024 x 700 inspected |

The native harness generates `artifacts/smoke/results.json`, viewport PNGs and full-workspace PNGs. These generated results are ignored by Git; representative screenshots are checked into `docs/images/`.

Regression results are written to `PointCloudViz_Final.Tests/TestResults/regression.trx`.

## Reproduce

```powershell
.\scripts\verify.ps1 -Smoke
dotnet publish PointCloudViz_Final -c Release -r win-x64 --self-contained false -o artifacts/publish
```

The GPU smoke program requires an interactive Windows desktop. Hosted CI runs the non-GPU regression suite and publishes a framework-dependent Windows x64 directory. It does not claim to have run the native visual checks.

## Known Coverage Limits

- LAS / PLY binary regressions use small, explicit binary fixtures, not a certification corpus from every vendor.
- The built-in scene has 144,924 points. No billion-point or out-of-core performance claim is made.
- Single-precision coordinates remain a documented accuracy limit.
- The legacy software rendering classes are not the active workstation renderer and are not part of the GPU smoke acceptance path.
- UI controls were exercised at two native desktop sizes; mobile browsers are not a target of this WPF application.
