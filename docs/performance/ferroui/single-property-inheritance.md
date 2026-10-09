# Single-property inheritance without pooled snapshot work

A reparent between null or terminal ancestors containing the same single inherited property
needs only two effective-value references. `SetInheritanceParent` now recognizes that case
before borrowing a comparison dictionary. Both references are captured before the ancestor
is changed. The original notification and child-propagation routines are then called, with
their local-value pruning, exceptions and reentrant behavior unchanged.

Recognition rejects deeper ancestor chains, multiple inherited values and two different
inherited properties. The unchanged general snapshot path handles those cases. Its pool
ownership remains exception-safe and property-major notification order is preserved. The
recognizer never calls user-overridable code or evaluates property values. Non-inherited
local properties do not invalidate the single-inherited-value proof.

Inherited value propagation also reuses the existing property-owned immutable name-only
INPC arguments and avoids allocating event arguments for an empty child list. Sender/value
arguments retain their original lifetime and order; no callback is removed for a child.

Eleven new cases compare optimized and general traces for null transitions, equal values,
multiple/different properties, ancestor chains, local overrides and reentrant reparenting;
verify that no dictionary is borrowed even inside singleton callbacks; cover throwing
callbacks and repeated inherited INPC sender/name behavior. Existing general pool tests now
warm a two-property source explicitly, so they still exercise the rental/exception path.

This completes the previously uncommitted `ValueStore` work; the bare source blob was not
buildable without its recognizer. Elapsed-time gains must come from the new exact-revision
runs of the unchanged native/browser harnesses, not from the existence of this fast path.
