# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.1.0] - 2026-01-21
Baked texture sample shaders added. New custom lighting sample and advanced gas giant and asteroid ring shaders.

### Added
- New static baked shaders with baked texture samples from the full asset.
- New sample scene with all baked shaders.
- New sample scene with basic shader custom omnidirectional lighting.
- New Gas Giant shader with whirling storms.
- New planet and corona glow shaders that support orthographic camera (add Planet Shader Global Settings to manage automatic switching between perspective and orthographic modes).
- New Asteroid Ring shader with improved transparency and custom lighting.

## [3.0.2] - 2025-12-16
Мinor fixes.

### Added
- Fresnel Intensity property for randomly generated Earth-like planes.

### Changed
- Continent mask shader subgraph - extracted position and cubemap sampling logic outside of subgraph.

### Fixed
- Earth-likes runtime color generation was not applying color gradients correctly.

## [3.0.1] - 2025-12-16
New Simplified Gas Giant shader added and minor fixes.

### Added
- Gas Giant Lite (Simplified) shader - a performance-optimized version of the procedural Gas Giant Lite shader, with reduced features and visual fidelity. Find it under a new menu item.

### Fixed
- Welcome window names and documentation links updated.

## [3.0.0] - 2025-12-11
Major update - the asset has been renamed to Procedural Planet Generation: Lite Sample Pack.

### Added
- Procedural Moon shader.
- Procedural Earth-like shader.
- Legacy 2.1.2 version included as an optional import .unitypackage for backwards compatibility.

### Changed
- Procedural generation script system updated to use modular component-based architecture.
- All shaders renamed to {Shader Name} Lite and moved to new custom menu group.
- All shaders are now part of the Procedural Planet Generation Lite shader group.

## [2.1.2] - 2025-08-18
Improved lighting-related shader effects and fixed minor bugs

### Added
- Added Procedural Asteroid Ring (Lighting Override), an alternative asteroid ring shader with custom lighting controls.
- New Lighting Sample Scene that shows shadow casting and lighting improvements on asteroid ring and gas giant shaders.
- Links to new online documentation.

### Changed
- The Fresnel glow effect of the gas giants is now occluded by shadows.
- Updated FlyCamera and DisableCursor scripts to be compatible with both Input System and legacy Input Manager.

### Fixed
- Shadow settings of sample URP assets allow celestial bodies in sample scenes to cast shadows.

## [2.1.1] - 2025-08-11
Bug fixes

### Added
- ViewDirectionWorldSpaceFullScreenShader subgraph, used in GasGiantGlow and Corona shaders.

### Fixed
- Fixed a visual bug where gas giant and star glow effects would wobble and jitter when viewed from a distance with a small camera near clip plane value.
- Removed FullScreemMultiplePassesRendererFeature.cs from the asset - it was erroneously included commented out prototype code.
- Changed the Readme to include the correct contact email for support.
- Moved DisableRotation functionality from Update to LaterUpdate to reduce a lagging or wobble effect when framerate is very low. (3D Skybox Wobble Fix)
- Cleaned up leftover comments and #TODOs from PostFXGlowPass.cs

### Changed
- Ray-sphere intersection code refactored to return more logically defined results. GasGiantGlow and Corona shaders updated to use the new results.
- Refactored the names of some variables in GasGiantGlow.hlsl to be consistent with the effect.

## [2.1.0] - 2025-07-30
Editor and Runtime Random Generation

### Added
- Random Procedural Generation methods for asteroid rings, gas giants and stars.
- New sample scene showcasing the runtime random generation of celestial bodies.
- Editor UI Buttons for randomization of Procedural Gas Giant, Procedural Star and Procedural Asteroid Ring inspectors.

### Changed
- Creating new procedural celestial bodies through custom menu commands creates a randomized body by default.

### Fixed
- Fixed angular seam bug in the Asteroid Ring shader (again).
- Missing texture warnings for gradient textures on new body creation.

## [2.0.0] - 2025-07-10
Major update - package structure rework and 3D Skybox Sample scene and URP rendering technique.

### Added
- New welcome window with links to sample scenes and documentation.
- New URP Renderer assets for 3D skybox rendering.
- New sample scene showcasing the 3D skybox effect.
- 3 New gas giant prefabs, used in the 3D skybox sample scene.
- Sample URP Volume profile with Bloom effect to be used as an alternative, or in addition to post-processing glow effect.

### Changed
- Asset format reverted to Assets folder package. This will require uninstalling any previous versions of the package.
- GlowPostFXPass renamed to PostFXGlowPass for consistency, reworked to access the depth and stencil attachment textures for 3D Skybox compatibility.
- Reworked Corona and Gas Giant Glow effects for better performance and visual consistence when scaling their size.

### Fixed
- Visual artifacts at glow effect edges would occur when viewed from great distances.

## [1.1.0] - 2024-12-20

### Added
- New Procedural Gas Giant Glow shader effect.

## [1.0.1] - 2024-12-13

### Changed
- Renamed 'PlanetaryRing.hlsl' to 'AsteroidRing.hlsl' to better fit shader purpose.

### Fixed
- Fixed minor visual bug in asteroid ring shader - angular discontinuity visible as a 1 pixel-wide line.

## [1.0.0] - 2024-12-02

### Added
- Initial release