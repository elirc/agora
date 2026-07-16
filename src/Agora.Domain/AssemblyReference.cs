namespace Agora.Domain;

/// <summary>
/// Marker for assembly-level reflection (architecture tests, scanning).
/// </summary>
// WHY: architecture tests need a compile-safe handle to this assembly; a marker type beats
// Assembly.Load("magic string") because renames break the build, not the tests.
public static class AssemblyReference;
