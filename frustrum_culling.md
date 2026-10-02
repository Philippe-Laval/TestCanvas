# frustum culling (not drawing what isn't visible).

> I am programming in C# with Blazor server. In a page I am using a html canvas to draw a graph. Actually I have only few nodes and edges in the graph. I want to support more element. I want to use a viewport for not drawing all node not visible. What are the best   practice or ideas ?

Optimizing a canvas in Blazor Server for rendering large graphs is a great move. Because Blazor Server communicates over WebSockets (SignalR), minimizing JSInterop calls and offloading the rendering logic is critical.
Here are the best practices, techniques, and ideas to implement an efficient viewport with frustum culling (not drawing what isn't visible).

## 1. Maintain a Virtual Viewport State
To know what to draw, you must track the camera's state in your C# code. Maintain a structure or class that holds:

* X, Y: The top-left coordinate of the viewport in the "world" space.
* Width, Height: The actual pixel dimensions of the canvas element.
* Zoom: The current zoom level (scale factor).

When a user drags (pans) or scrolls (zooms), update these variables in C# first.

## 2. Implement Frustum Culling (Spatial Filtering)
Before passing any node or edge to the canvas API, check if it falls within the boundaries of your viewport.

* For Nodes:
A node is visible if its world coordinates (plus its radius/size) overlap with the viewport rectangle:
$$\text{Visible} = (X_{\text{node}} + R \ge X_{\text{view}}) \land (X_{\text{node}} - R \le X_{\text{view}} + W_{\text{view}}) \land (Y_{\text{node}} + R \ge Y_{\text{view}}) \land (Y_{\text{node}} - R \le Y_{\text{view}} + H_{\text{view}})$$ 
* For Edges:
Use a line-segment intersection test against the viewport bounding box. Alternatively, a quicker (but slightly less accurate) check is to see if either connected node is visible, or if the bounding box of the edge intersects the viewport.

## 3. Use Spatial Indexing for Mass Elements
If your graph grows to thousands of elements, checking every single node/edge sequentially ($O(N)$ complexity) on every frame will slow down your server.

* Quadtree or Grid: Group your graph elements into a Quadtree or a simple Spatial Hash Grid in C#.
* How it helps: When rendering, you query the Quadtree only for the regions intersecting your viewport. This drops the lookup complexity to $O(\log N)$ or $O(1)$, instantly filtering out thousands of off-screen elements before processing.

## 4. Optimize the Blazor-to-JS Bridge (Critical for Blazor Server)
Every CanvasContext.DrawImage or LineTo call creates a network hop over SignalR. If you have 500 visible elements, calling JSInterop 500 times per frame will cause massive lag.

* Batching Calls: Do not call Canvas APIs directly from C# one by one. Instead, write a small custom JavaScript function (e.g., window.drawGraph(visibleNodes, visibleEdges, viewportState)).
* Single Interop Hook: Serialize your filtered (culled) list of nodes and edges into a lightweight JSON array and send it to your JS function in one single InvokeVoidAsync call. Let JavaScript loop through the local array and do the actual Canvas rendering instantly.

## Comparison of Architecture Approaches

| Strategy | Pros | Cons | Best Used For |
|---|---|---|---|
| Pure C# Filtering + Batched JS | Keeps business logic in C#; easy to maintain. | Network overhead on very high FPS pan/zoom. | Small to Medium graphs (up to ~2,000 elements). |
| Spatial Indexing (Quadtree) | Scales incredibly well; server CPU usage stays low. | Extra complexity to update the tree during layout changes. | Large graphs (2,000+ to 50,000 elements). |
| Client-Side Web Worker / WASM | Completely eliminates SignalR latency during panning/zooming. | High complexity; splitting logic between C# and JS/WASM. | Massive graphs or highly interactive animations. |

## 5. Level of Detail (LOD) Rendering
As the user zooms out, nodes become tiny dots and text becomes unreadable. Drawing complex shapes or text strings for thousands of microscopic nodes wastes processing power.

* Thresholds: Define Zoom levels (e.g., Zoom < 0.3).
* Behavior: When zoomed out past the threshold, stop rendering node labels, text, and complex borders. Render nodes as simple squares or circles, and edges as thin single-pixel lines.
