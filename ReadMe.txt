
Unity 2 PNG 
    Author: Matthew Mazan (Masterio)
    Permitted: Free to personal or commercial use.
    Forbidden: Re-Sell/Re-Share this package.
    Compatibility: Unity 2021.3.0+, URP

Patch notes:

[v3.0]
	+ Added Unity 7 compatibility alongside backwards compatibility with Unity 2021.3.0+.
	+ Added full support for the new Unity Input System (alongside the Legacy Input Manager).
	+ Added an interactive Animation Preview Player directly inside the Inspector and as a dockable Pop-Out window (with Zoom, Pan, FPS control, Play/Pause, Auto-Play, background switcher, and an Alpha=0 debug highlighter).
	+ Added Resolution Override presets (256x256, 512x512, 1024x1024, 2048x2048) without modifying the source RenderTexture asset.
	+ Added fractional Duration support (0.1s–10.0s) and increased the maximum transition frames limit to 60.
	+ Added real-time RAM usage estimation and an automatic Out-Of-Memory safety guard.
	+ Added Quadratic Alpha Smooth to cleanly fade out faint outer glow fringes.
	+ Added Multi-Object Editing support in the Inspector and collapsible UI sections (FILES & OUTPUT, PREVIEW).
	+ Simplified loop transition options by removing legacy transition modes and keeping only the universal Seamless transition.
	+ Fixed pixelated edge seams and stepping glitches during loop transitions (when '+RGB Alpha Test' is enabled) by replacing hard threshold cutoffs with smooth Porter-Duff compositing and pure Premultiplied Alpha crossfading.
	+ Improved '+RGB Alpha Test' using orthogonal R/G/B background transmittance combined with outer Bloom glow preservation (fixes semi-transparent grey/dark objects while keeping soft bloom edges).
	+ Optimized memory usage by keeping only loop transition frames in RAM and exporting regular frames immediately.
	+ Enforced 32-bit float (R32G32B32A32_SFloat) RenderTexture validation to prevent color banding and precision glitches.
	+ Code cleanup and UI refinements.

[v2.0]
	+ Implemented Unity Job System (utilizes multi-threaded CPU processing to speed up the entire capture pipeline).
	+ Fixed a glitch in '+RGB Alpha Test' where grey pixels were incorrectly treated as semi-transparent.
	+ Fixed HDR color capturing in re-capture mode.
	+ Adjusted alpha distribution factors for loop transitions.
	+ Added CustomNormalized transition.
	+ Removed AlphaBlend transition.
	+ Removed AlphaBlend_Max transition.
	+ Updated version numbering from v1.10 to v2.0.
	+ Code cleanup.
