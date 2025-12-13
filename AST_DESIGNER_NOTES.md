**Relevant Avalonia/XamlX pieces**
- Parse layer: `external/XamlX/src/XamlX/Parsers/XDocumentXamlParser.cs` builds a `XamlDocument` whose root is a `XamlAstObjectNode` with children that are `XamlAstObjectNode` (content), `XamlAstXamlPropertyValueNode` (attrs/prop elements), `XamlAstXmlDirective` (x:/d: directives), and `XamlAstTextNode`. Every node carries `Line/Position`, so you already have a lightweight AST with source locations.
- Transform/type‑resolve layer: `src/Markup/Avalonia.Markup.Xaml.Loader/CompilerExtensions/AvaloniaXamlIlCompiler.cs` wires the full Avalonia transformer chain (XName, design‑properties, bindings, property/type resolvers, content conversion, templates/styles validators). You can call `Parse` + `Transform` (no IL emit) if you want typed nodes/content‑property info/validation.
- Preview instantiation: `AvaloniaRuntimeXamlLoader.Load` → `AvaloniaXamlIlRuntimeCompiler` dynamically compiles and instantiates from a XAML string. Existing previewer (`src/Avalonia.DesignerSupport/Remote/RemoteDesignerEntryPoint.cs`) just reloads the full string on each update.

**Minimal AST‑based WYSIWYG designer**
- Make the raw XamlX AST the canonical model. Keep a side metadata table keyed by `XamlAstObjectNode` for `NodeId`, selection, undo stack, etc.
- Implement a small AST→AXAML writer for the four node types above. It can reformat; for simplicity, serialize markup extensions as element syntax if needed.
- For preview, serialize AST to a *preview string*, inject a designer namespace at the root and an attached `designer:Id="..."` on every object node, then load it via `DesignWindowLoader.LoadDesignerWindow` (in‑process) or a RemoteDesigner‑style host (out‑of‑process).
- UI layout: tree view = projection of `Children.OfType<XamlAstObjectNode>()` (optionally show explicit property elements as secondary nodes); property grid edits `XamlAstXamlPropertyValueNode`s; WYSIWYG surface is the preview plus an adorner layer.

**Bidirectional sync**
- AST → tree/preview: all edits go through `AstCommand`s (add/remove/move/set property). Tree rebinds immediately; preview reloads debounced (same pattern as current previewer). Store selected `NodeId` and re‑select after reload.
- Preview → AST: hit‑test, walk up to nearest `AvaloniaObject` with `Designer.Id` attached property, map to AST node. Gestures translate to property edits:
  - parent `Canvas`: update `Canvas.Left/Top` + `Width/Height`
  - parent `Grid`: update `Grid.Row/Column(/Span)` based on cell hit
  - parent `Panel`/`ItemsControl`: reorder/move child `XamlAstObjectNode`s in parent’s `Children` list.
- Tree → preview: selection uses `NodeId`→runtime object lookup; structural edits reuse the same command path.

**Implementation steps / pitfalls**
- Steps: (1) parse initial XAML (`XDocumentXamlParser.Parse` or `AvaloniaXamlIlCompiler.Parse(IsDesignMode=true)`), assign `NodeId`s; (2) build serializer + preview‑string injector; (3) preview host + Id mapping + selection; (4) core AST edit commands with undo/redo; (5) adorner tools mapping drag/resize/reorder to commands.
- Pitfalls: template‑generated visuals aren’t in AST (only map nodes with injected Ids); namespaces are root‑only per parser (update `XamlDocument.NamespaceAliases` when inserting new types/attached props); markup‑extension round‑trip is tricky (store original text or emit element syntax); full reload flicker/perf (debounce now, diff later); loading user assemblies can run code (prefer out‑of‑process host for safety).
