# macOS 26 theme implementation log

Base: `b709c58c6b1b8aa3b90866c7c001b7bf82b6353b` in `wieslawsoltes/Avalonia`.
Branch: `feature/macos26-theme`.

## 01 — Preserve the Fluent baseline

Copied the complete Fluent theme into a new `Avalonia.Themes.MacOS` project. All control templates, named template parts, pseudo-class rules, localized resources, density resources, palette infrastructure, and license notices are preserved. This commit intentionally retains the original source namespaces; the next step isolates the new theme API.

## Release gates

This work is not release-certified until compilation, automated tests, package consumption, and actual macOS 26 visual/interaction checks pass. A theme resource dictionary alone does not implement Apple's private Liquid Glass renderer. Native vibrancy and cross-platform opaque fallback must be documented separately.

Screenshots must be captures from the running Avalonia ControlCatalog, not HTML approximations or generated artwork. Do not claim a screenshot exists until its capture artifact has been produced and reviewed.

Planned checkpoints: copied baseline; isolated theme and tokens; macOS control families; light/dark and accessibility modes; ControlCatalog regression matrix.
