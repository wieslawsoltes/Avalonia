**WYSIWYG AST Designer — V1 Plan With Built‑In Phase‑8 Solutions**

This plan keeps v1 minimal in UI/feature surface but **architecturally includes every Phase‑8 solution up front** so later additions are additive (new plugins/features), not refactors.

---

## **V1 Success Criteria**
- Edit `.axaml/.xaml` visually and via tree/property grid with full bidirectional sync.
- Save back to XAML with **structural correctness + formatting/comment/attribute‑order preservation**.
- Support core layout containers (Canvas, Grid, StackPanel, DockPanel, generic Panel) with drag/move/resize/reorder where semantically valid.
- Preview is safe and robust (out‑of‑process ready; in‑process allowed for dev).
- Extensibility points exist for containers, property editors, serialization, and multi‑file previews.

## **V1 Deferred UI (Foundations Included)**
V1 keeps the *UI surface* minimal, but the following areas are **fully accounted for in the model, commands, plugins, and preview plumbing** so adding richer UI later is additive.

- **Blend‑level features** (animations, triggers, visual states, full style authoring).  
  V1 includes first‑class semantic coverage for `Styles`, `ControlTheme`, templates, triggers/animations, serializer strategies, and a command set for editing them; UI starts read‑only with basic property editing.
- **Template‑generated visuals selection/editing.**  
  V1 includes runtime→definition mapping hooks (template overlays, jump‑to‑definition) via `ITemplateVisualMapper`; edits always target the defining template AST.
- **Markup extension semantic editing.**  
  V1 preserves original `{...}` text tokens and supports optional parsed markup‑extension trees with `IMarkupExtensionEditorProvider`, so structured ME editors can be added without changing serialization.

---

## **Core Architectural Decisions (Phase‑8 included in v1)**

### **A. Dual Canonical Model (No‑Refactor Round‑Trip)**
V1 uses **two synchronized representations** from day one:
1. **Semantic AST**: raw XamlX AST from `XDocumentXamlParser.Parse` (`XamlAstObjectNode`, `XamlAstXamlPropertyValueNode`, `XamlAstXmlDirective`, `XamlAstTextNode`).  
   - Used for: tree projection, WYSIWYG semantics, commands, validation.
2. **Lossless XML Token Tree**: a lightweight DOM/token layer capturing:
   - exact whitespace, indentation, newlines,
   - comments/processing instructions,
   - attribute order and original quote style,
   - exact markup‑extension text.

**Mapping rule:** every semantic AST node stores a pointer to its owning XML token span (or `XElement/XAttribute`), enabling lossless rewrite of only the changed fragments.

### **B. Stable Node Identity Without Polluting User XAML**
V1 introduces `NodeId`s that survive edits/sessions without inserting designer attributes into saved files:
- **Runtime Id injection only for preview** (ephemeral `designer:Id="..."`).
- **Persistence via sidecar** (e.g., `.axaml.designer.json` keyed by XPath + heuristics) OR via deterministic hash of structural path + `x:Name` when present.
- Choose sidecar in v1 so production XAML stays clean; identity heuristics are pluggable.

### **C. Plugin Model From Start**
No hard‑coded container logic in core:
- `IContainerBehaviorProvider`:
  - selection bounds, drop targets, drag/reorder rules, snap/gridline support.
  - one provider per container type (Canvas, Grid, StackPanel, DockPanel, ItemsControl, generic Panel).
- `IPropertyEditorProvider`:
  - maps Avalonia property metadata/type → UI editor + parse/serialize strategy.
- `ISerializerStrategy`:
  - per node type or namespace to handle special cases (styles, templates, resources, markup extensions).
- `IIdentityProvider`:
  - pluggable NodeId persistence/matching.

V1 ships only core providers; new ones can be added without changing core types.

### **D. Preview Host Interface (Safe Now, Not Later)**
Define `IPreviewHost` in v1 with two implementations:
- `InProcessPreviewHost` (dev, fast iteration).
- `RemotePreviewHost` using existing `Avalonia.Designer.HostApp` transport patterns.

UI talks only to `IPreviewHost`; switching to remote later is config, not refactor.

### **E. Multi‑File / Group Preview Ready**
V1 model supports a **document set**:
- main view file + referenced styles/resources dictionaries.
- preview pipeline can build a group preview via `AvaloniaRuntimeXamlLoader.LoadGroup`.

Tree surface includes “Resources” and “Styles” nodes even if editing UI is minimal.

### **F. Styles/Triggers/Animations As First‑Class Nodes**
V1 treats styling and interaction constructs as semantic data, not opaque text:
- Semantic AST + token mapping covers `Styles`, `ControlTheme`, `DataTemplate`, `ControlTemplate`, triggers, setters, transitions, and animations.
- Core command set includes style/trigger/animation edits (even if UI is basic), routed through plugins.
- Preview host exposes a “state harness” API to toggle pseudo‑classes/visual states for trigger/animation preview.

### **G. Template‑Generated Visuals Mapping**
V1 supports selecting runtime visuals created from templates and mapping them back to definitions:
- Preview injection adds `designer:Id` inside template bodies; multiple runtime instances share the same definition id.
- `ITemplateVisualMapper` resolves a clicked runtime visual to the defining template AST node; UI can show a template overlay and jump‑to‑definition.

### **H. Markup Extension Semantic Model**
V1 preserves and optionally parses markup extensions:
- Token tree stores original ME text and span; semantic nodes reference it.
- When edited, MEs are parsed into a small ME tree stored in metadata and re‑emitted via `ISerializerStrategy`.
- Unchanged MEs round‑trip verbatim, avoiding formatting drift.

---

## **Phased Build Plan**

### **Phase 0 — Project Skeleton**
- Create new tool project (e.g., `src/tools/Avalonia.AstDesigner`).
- Reference Avalonia, `external/XamlX`, and minimal Roslyn/JSON libs for sidecar.
- Define core abstractions:
  - `DesignerDocumentSet`
  - `SemanticAstRoot`
  - `XmlTokenRoot`
  - `NodeId`
  - plugin interfaces (B–H above).

**Deliverable:** compilable designer app shell with empty panes.

---

### **Phase 1 — Parse + Dual Model Construction**
1. **Parse semantic AST**:
   - `XDocumentXamlParser.Parse(xaml)` for raw nodes + line info.
2. **Parse XML token tree**:
   - Use `XDocument.Load(..., LoadOptions.PreserveWhitespace | SetLineInfo)` or a custom tokenizer if needed to preserve attribute order/comments precisely.
3. **Build bidirectional map**:
   - Walk XML tree and AST simultaneously; bind each AST object/property/directive/text node to its source token span.
   - Store mapping in metadata table keyed by AST node reference.

**Acceptance:**
- Loading a view yields both trees and a complete map.
- Nodes created from markup extensions remember their original text token and can store an optional parsed ME tree.

---

### **Phase 2 — Node Identity + Persistence**
1. Assign NodeIds to every `XamlAstObjectNode` on load.
2. Implement `IIdentityProvider` v1:
   - read/write sidecar per document,
   - match ids on reload using (a) stored XPath/line range, (b) fallback structural hash.
3. Keep ids stable through:
   - property edits,
   - reorder/move,
   - delete/undo.

**Acceptance:**
- Close/reopen keeps selection identity stable after minor edits.

---

### **Phase 3 — Command System + Undo/Redo (Semantic + Lossless Rewrite)**
1. Define `AstCommand` with:
   - semantic edit (AST mutation),
   - corresponding token edit (localized XML rewrite),
   - reversible diff for undo.
2. Implement core commands:
   - `SetPropertyCommand`
   - `AddChildCommand`
   - `RemoveChildCommand`
   - `MoveChildCommand`
   - `ChangeTypeCommand`
   - `AddNamespaceCommand`
3. Coalesce rapid changes (drag/resize) into compound commands.

**Acceptance:**
- Any edit updates AST + tokens; undo restores both.

---

### **Phase 4 — Lossless Serializer / Writer**
1. **Primary save path:** rewrite XML tokens in place:
   - only changed nodes are re‑emitted,
   - indentation/comments/attribute order preserved from token tree.
2. **Fallback full writer:** AST→AXAML for nodes without token backing (new nodes).
3. Namespace management:
   - `AddNamespaceCommand` updates root token + AST aliases.
4. Markup extensions:
   - preserve original text token if untouched,
   - if edited structurally, re‑emit with a strategy (text `{}` vs element syntax) via `ISerializerStrategy`.

**Acceptance:**
- Save produces minimal diffs and preserves formatting/comments.

---

### **Phase 5 — Preview Pipeline + Selection Mapping**
1. Implement preview serializer:
   - take semantic AST + token root,
   - produce preview XAML string,
   - inject ephemeral `xmlns:designer=...` and `designer:Id` on each object node.
2. Build `IPreviewHost` v1:
   - in‑process implementation first,
   - remote stub with same API.
3. After reload, build `Id → AvaloniaObject` map by walking visual tree for `Designer.Id` (including visuals from templates).
4. Implement preview “state harness” toggles for pseudo‑classes/visual states (foundation for triggers/animations).
5. Debounce reload (150–300ms).

**Acceptance:**
- Editing AST reloads preview correctly; selection re‑applies by NodeId, including template‑instantiated visuals.

---

### **Phase 6 — Tree View + Properties (Plugin‑Driven)**
1. Tree projection:
   - object hierarchy from AST,
   - top‑level groups: “Visual Tree”, “Resources”, “Styles”.
2. Drag/drop:
   - routed through `IContainerBehaviorProvider` based on parent type.
3. Property grid:
   - uses runtime object metadata when available,
   - editors supplied by `IPropertyEditorProvider`.
4. Property edit path:
   - emits `SetPropertyCommand` (never direct mutation).

**Acceptance:**
- Tree reorder works for StackPanel/DockPanel/ItemsControl via behavior plugins.
- Property edits are undoable and round‑trip losslessly.

---

### **Phase 7 — WYSIWYG Adorners + Gestures (Core Behaviors Only)**
1. Hit‑test selection:
   - select nearest runtime object with `Designer.Id`,
   - update tree selection.
2. Adorner layer:
   - bounds, resize handles, container guides.
3. Gesture mapping via behaviors:
   - Canvas: edit `Canvas.Left/Top`, `Width/Height`.
   - Grid: edit `Grid.Row/Column(/Span)` based on cell hit.
   - StackPanel/DockPanel: reorder only.
   - Generic Panel: reorder; no freeform move.
4. Template overlay support:
   - when a selected visual is template‑generated, use `ITemplateVisualMapper` to jump to the defining AST node and edit there.
5. Inline content edit:
   - double‑click `TextBlock.Text`, `Button.Content`, etc. mapped by property providers.

**Acceptance:**
- Basic drag/move/resize/reorder for supported containers.

---

### **Phase 8 — Validation, Perf, Remote Safety (Already Architected)**
1. Diagnostics:
   - optionally run `AvaloniaXamlIlCompiler.Parse + Transform(IsDesignMode=true)` on preview string for errors/warnings.
2. Perf:
   - incremental tree UI updates,
   - command coalescing,
   - avoid full preview reload on non‑visual edits when possible.
3. Remote preview:
   - flip config to `RemotePreviewHost` for safe sandboxed loading.
4. Extensibility hardening:
   - document plugin discovery/loading rules,
   - versioned sidecar schema.

**Acceptance:**
- Invalid XAML surfaces errors without crashing; switching to remote host requires no UI changes.

---

## **What’s “Phase‑8‑Ready” in v1**
- Lossless round‑trip is v1 core (dual model + token rewrite).
- Plugin model is v1 core (container behaviors, property editors, serializers, identity).
- Multi‑file/group preview plumbing is v1 core (document sets + LoadGroup path).
- Remote preview is v1 core interface (implementation can mature later).

This ensures future work (styles/resources editors, new container rules, richer serialization) is incremental, not structural refactor.
