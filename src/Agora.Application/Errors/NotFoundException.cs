namespace Agora.Application.Errors;

/// <summary>
/// The requested resource does not exist — or the caller is not allowed to know
/// whether it exists. The Api maps this to 404 problem+json.
/// </summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
    public object Key { get; } = key;
}
