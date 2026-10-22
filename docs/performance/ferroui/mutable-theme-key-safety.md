# Resource-cache safety for arbitrary theme keys

ThemeVariant is an immutable wrapper but its public Key is an arbitrary object. ThemeVariant.GetHashCode/Equals forward to that key. Identity of the wrapper alone therefore does not establish that a theme lookup is immutable: custom key hash/equality can change or run callbacks without a resources event.

Requested custom variants now use location caching only when every key in their immutable inheritance chain is a string or actual runtime Type. Null and the built-in Light/Dark/Default objects have a short inlinable path. The original live theme walk is retained otherwise.

Stored opaque theme keys are tracked by the same conservative sticky flag as opaque resource keys, before owner-add callbacks can look up resources. This covers an opaque stored key that begins matching Default after a previous negative lookup, even when the requested theme is null. Introducing an opaque key after caching invalidates earlier locations, and nested probes propagate the no-cache decision. The flag owns no theme objects and is conservatively retained after removal.

Tests cover mutable requested and inherited key hashes, stored equality that changes before/after cache warm-up, and eligibility of built-in and stable custom chains. This is behavioral compatibility with live dictionaries, not a promise that mutating a dictionary key is good application practice. Ordinary application theme keys retain caching and the existing benchmark workload is unchanged.
