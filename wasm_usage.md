# WASM usage for Blazor Canvas Rendering

> I will use WASM. The graph uses dynamic layout algorithm that updates the nodes coordinates on demand. I am using custom JSInterop wrapper. Did you have a good third-party Blazor canvas library ?

Switching to Blazor WebAssembly (WASM) completely changes the optimization strategy for the better. Because C# executes directly in the browser's Mono/WASM runtime, the network latency from SignalR disappears. However, standard C# HTML Canvas wrappers can still suffer from overhead because every line, circle, or text draw command still has to cross the .NET-to-JavaScript boundary via JSInterop. [1, 2] 
If you are running a dynamic layout algorithm (like force-directed layouts) that constantly shifts coordinates on demand, you need maximum rendering throughput.

## Recommended Third-Party Options & Strategies

## 1. SkiaSharp for Blazor (Highest Performance C# Option)
Instead of wrapping the browser's HTML5 Canvas, you can use SkiaSharp (SkiaSharp.Views.Blazor). [3] 

* How it works: Skia is a Google-backed, high-performance 2D graphics engine compiled directly to native WebAssembly binary dependencies.
* Why it's great for your use case: It completely bypasses the browser's JavaScript Canvas API. Your dynamic layout algorithm and your rendering engine both sit inside the WebAssembly layer. Drawing 10,000 nodes or edges is incredibly fast because there are zero JSInterop hops during the render loop.
* Consideration: It compiles down to WebAssembly, which slightly increases your initial app download size. [3] 


## 2. nkast.Wasm.Canvas (Modern Canvas 2D / WebGL Wrapper)
If you want to stick strictly to the standard HTML5 Canvas element but need a modern library optimized for WASM, [nkast.Wasm](https://github.com/nkast/Wasm) is a highly active and optimized open-source alternative. [4] 

* Why it's better than old wrappers: Many older libraries (like Blazor.Extensions.Canvas or Excubo.Blazor.Canvas) are mostly abandoned or lack specific high-frequency execution optimizations. nkast.Wasm provides low-level direct bindings for Window, Canvas 2D, and WebGL2 optimized specifically for client-side WASM performance. [2, 4, 5, 6, 7] 

## The Ultimate Architecture for Your Custom Wrapper
Since you already have a custom JSInterop wrapper and are handling a layout algorithm that pushes updates on demand, the most scalable architecture is to let C# manage the math and let JavaScript handle the rendering loop. [2] 
Instead of looking for a C# canvas library that forces you to write context.MoveTo() in C#, structure it like this:

[ C# / WASM State ]  ---(Single JSInterop array push)--->  [ Custom JavaScript File ]
 - Runs Layout Engine                                        - Receives Flat Arrays
 - Performs Viewport Culling                                 - Loops locally over Canvas 2D Context
 - Translates Zoom/Pan coordinates                           - Draws instantly at 60 FPS


   1. Keep the Layout Engine in C#: Let your dynamic force-directed algorithm compute positions in C# using quick, typed .NET arrays or structures.
   2. Cull in C#: Do the viewport boundaries check (frustum culling) inside your C# layout update step.
   3. The Interop Payload: Flatten your visible graph data into simple arrays (e.g., a flat float[] of [X1, Y1, Radius1, X2, Y2, Radius2...] or lightweight JSON). [8] 
   4. Blit to JS: Pass that array to your custom JavaScript file via a single JSInProcessRuntime call (since you are on WASM, you can use synchronous in-process interop for zero-latency memory sharing). Let a tight JavaScript loop clear the canvas and draw everything natively.


* [1] [https://www.youtube.com](https://www.youtube.com/watch?v=WmUWAKZrqOw&t=161)
* [2] [https://www.reddit.com](https://www.reddit.com/r/Blazor/comments/idikdk/what_is_the_flow_for_c_code_to_manipulate_the_dom/)
* [3] [https://learn.microsoft.com](https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-native-dependencies?view=aspnetcore-10.0)
* [4] [https://github.com](https://github.com/nkast/Wasm)
* [5] [https://github.com](https://github.com/excubo-ag/Blazor.Canvas)
* [6] [https://github.com](https://github.com/BlazorExtensions/Canvas)
* [7] [https://github.com](https://github.com/excubo-ag/Blazor.Diagrams/issues/50)
* [8] [https://oneuptime.com](https://oneuptime.com/blog/post/2026-01-30-custom-jsinterop-blazor/view)
