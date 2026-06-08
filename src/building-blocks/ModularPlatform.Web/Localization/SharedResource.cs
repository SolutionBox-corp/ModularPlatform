namespace ModularPlatform.Web.Localization;

/// <summary>
/// Marker for the platform's shared resx catalogue. Resource KEYS are error codes
/// (e.g. "credit.insufficient_balance"); values are the localized human messages.
/// Modules add their own keys to SharedResource.{culture}.resx.
/// </summary>
public sealed class SharedResource;
