using System.Runtime.CompilerServices;

// The Billing module's Core types are internal (module boundary law). The module's own test project needs
// to construct internal validators directly for fast unit tests (no DB), so expose internals to it only.
[assembly: InternalsVisibleTo("ModularPlatform.Billing.Tests")]
