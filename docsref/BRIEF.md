The project is Najm, a procedural engine for creating educational graphics. The main purpose of the engine is to make educational graphics, whether videos, animations, still images, or even live interactive presentations. I tried my hand at existing tools like Manim, MotionCanvas, even game engines like Unity, but none of them had a specific combination of properties which I wanted. I wanted an engine that is:
- Vector-crisp (optimized for vector graphics by default)
- Codes like a video game (scene graph based, has composable nodes that can own children nodes and behaviors that you can attach to nodes)
- Interactive (can take mouse and keyboard input, and either feed it to nodes in a video-game like way, or automatically does reverse paint order hit-testing and reports events to the node that gets the click or has the focus)
- Composes like illustrator or photoshop (native support for masking, blending modes, multiple layers on top of each other, ...etc)
- Can do arbitrary effects, shaders, masks and filters
- Is powerful, expressive, imperative (immediate mode graphics using primitive commands) and has very nice OOP DX
- Has good support for text and math and integrates them well with everything else
- Can compose both 2D and 3D, but 2D is the priority
- For 3D, I want to experiment with 3D vector graphics by reifying them from the 3D camera into 2D primitives that are then drawn vector-crisp with high resolution anti-aliasing using Skia

And that's the specific niche I want to fill with Najm. I think I'll be able to make some really cool educational content with it.

For my first projects, which I'd like to get done over the next couple of weeks, I'd like to make a video about sorting algorithms (2D animation array visualizations, code stepping visualizations) and a presentation about the hydrogen atom. The latter is the more important one, it's due by around September 14th. The hydrogen atom rendering will be done via GLSL ES shaders which I've already prepared (you can inspect them by cloning https://github.com/xminty77/hydrogen). I need Najm to act the interactive presentation tool that I'll use to create rich interactive animated scenes that move around the visualization and tweak its parameters in real time as I'm presenting, plus the additional UI on top (e.g. chapter names).
