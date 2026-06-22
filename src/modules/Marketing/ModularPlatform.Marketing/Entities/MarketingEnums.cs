namespace ModularPlatform.Marketing.Entities;

/// <summary>A free marketing data source the module can pull from.</summary>
internal enum PullSource
{
    Ga4,
    Gsc,
    PostHog,
    Reddit,
    Trends,
}

/// <summary>Lifecycle of a single data pull (accepted → durable worker advances it to a terminal state).</summary>
internal enum PullStatus
{
    Pending,
    Running,
    Completed,
    Failed,
}
