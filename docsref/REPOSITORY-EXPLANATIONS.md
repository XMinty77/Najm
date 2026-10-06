# Repository explanations with Najm

Status: proposed external use case. This note adds no integration or engine feature.

## The connection

Repo Brain gives each selected repository its own assistant, instructions, conversations, and source evidence. A developer can ask how a function works, how data moves through the application, or what a proposed change affects.

Najm could supply the visuals for an optional **Illustrate** mode. Repo Brain would retrieve and explain the repository; a separate adapter would turn selected evidence into an authored Najm scene. The answer would remain readable without the illustration.

For example, a developer asking how a request reaches a database could receive a small diagram, followed by an animation highlighting the relevant functions in order. Each step would have a caption and a source reference. A static call graph would be described as a possible path, rather than presented as a recorded execution.

## A first useful slice

Start with a single call-chain template and PNG output. Add a short MP4 only when the ordering is supported by the supplied evidence. Keep the template small enough that labels remain readable and show omitted branches explicitly.

The current [PNG export](../src/Najm.Skia/Delivery/SkiaExport.cs) accepts a fresh scene factory and a sample time. The [Orrery driver](../samples/Najm.Samples.Orrery/Program.cs) demonstrates stills and video delivery. These are useful starting points for an adapter; they do not provide a repository-analysis API.

The developer would activate Illustrate for a question. Ordinary answers would continue using the existing explanation path. The browser would display the exported media and an adjacent text list of steps with links to the original code. Direct web hosting, live scene controls, and SVG/PDF delivery should be evaluated separately against their implementation status.

## Adapter responsibilities

1. Retrieve evidence from the current bot's repository and record its commit.
2. Build a bounded explanation plan containing labels, relationships, steps, and source references.
3. Check that every referenced file and line belongs to that repository snapshot.
4. Map the plan to a reviewed C# scene template outside Najm.Core.
5. Render with fixed timing and a pinned engine/runtime environment.
6. Return media, captions, alternative text, and source links to the assistant UI.

An artifact manifest should record the repository commit, Najm revision, template version, render settings, and evidence IDs. Repository changes should mark older illustrations as stale; regeneration should remain an explicit request.

The scene owns drawing and choreography. The adapter owns repository knowledge, citations, model requests, caching, and application access. This follows the [host/scene boundary](ARCHITECTURE.md) without adding those application concerns to the engine.

## Execution and data boundaries

C# scene code can execute arbitrary operations. A proposed adapter should therefore accept validated data for reviewed templates, rather than compile arbitrary model output. Rendering still needs a separate process with explicit filesystem, network, time, memory, and output limits. A process boundary alone is not a sandbox.

Najm should receive only the selected labels and relationships needed to draw the scene. It needs no OpenAI key, GitHub credentials, complete checkout, or conversation history. A renderer failure should leave the cited text answer available.

Repo Brain does not yet have the rendering adapter or the required execution sandbox. This note is an integration direction, not a claim that Illustrate is available.

## How to evaluate a prototype

Use a small public fixture repository. Check source-link accuracy, snapshot freshness, readable labels, captions, bounded render time, and repeatability in the pinned environment. Compare the illustration with the fixture's actual call graph and distinguish observed behavior from inferred relationships. Ask a developer whether it helped them explain the code before expanding the template set.
