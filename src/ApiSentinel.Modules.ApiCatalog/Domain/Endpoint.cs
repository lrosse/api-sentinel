namespace ApiSentinel.Modules.ApiCatalog.Domain;

public sealed class Endpoint
{
    public Guid Id { get; set; }
    public Guid ApiServiceId { get; set; }
    public required string Path { get; set; }
    public EndpointMethod Method { get; set; }
    public required ApiService ApiService { get; set; }
}

public enum EndpointMethod
{
    GET,
    POST,
    PUT,
    PATCH,
    DELETE
}
