namespace ModularPlatform.Cqrs;

/// <summary>Void result for commands that return nothing.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value = default;
    public static Task<Unit> Task { get; } = System.Threading.Tasks.Task.FromResult(Value);
}
