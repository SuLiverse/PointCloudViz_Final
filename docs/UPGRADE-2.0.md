# 2.0 Upgrade Notes

## User-Facing Changes

- Native three-column point cloud workspace with statistics, height histogram, processing controls and operation / measurement tabs.
- Deterministic synthetic district, plus the original course sample.
- RGB, height and intensity color modes; Z-up camera presets; point size, background, grid and visibility controls.
- Cancelable file and processing operations, drag and drop, recent files and unsaved-change confirmation.
- Undo / redo for every active cloud transformation, including empty results and restoring the original cloud.
- Portable project snapshots with processed data, SHA-256 integrity verification, camera and measurement state.
- Binary PLY and XYZ exports, PNG viewport capture and measurement CSV.

## Correctness Repairs

- Removed the model constructor's undocumented 1.2-million-point sampling.
- Replaced collision-prone voxel hashes with complete integer coordinate keys.
- Fixed history eviction order and bounded retained snapshot memory.
- Rejected invalid, non-finite and truncated input instead of displaying incomplete or fabricated points.
- Separated PLY vertex properties from face properties; added binary little / big endian parsing.
- Validated LAS headers and record lengths; decoded formats 0-3 and 6-8 without silently excluding noise points.
- Disambiguated six-column XYZRGB from XYZI and preserved colors on export.
- Cleared GPU geometry for empty clouds and reset history when changing documents.
- Used renderer camera matrices for picking, accepted points at the origin, and rejected clicks on empty space or behind the camera.
- Fixed vertical-plane area calculations and retained user-ordered concave polygon boundaries.
- Avoided intensity overflow before clamping and used stable statistics accumulators.

## Engineering

- Modern-.NET HelixToolkit package, dependency lock files and deterministic builds.
- Automated regression tests, a real native-GPU smoke executable and reproducible verification scripts.
- Windows CI builds with warnings as errors and uploads application / test artifacts.
- Preserved the upstream removal of generated / local-state files and the cleaned contributor history.
- Git ignores build outputs, local tools, IDE caches, test results and logs. Application state and rotated logs live outside the checkout.
- Patched legacy transitive Http, RegularExpressions and Uri package versions; enabled transitive NuGet auditing.

See `README.md` for supported formats and explicit scale / precision limits. This release does not claim registration, semantic segmentation, LAZ decoding, out-of-core streaming or survey-grade coordinate precision.
