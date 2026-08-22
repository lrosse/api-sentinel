namespace ApiSentinel.Modules.ApiCatalog.Domain;

public sealed class ApiService
{
    public Guid Id { get; set; }
    public required string OwnerUserId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
    public required string BaseUrl { get; set; }
    public List<Endpoint> Endpoints { get; set; } = [];
}
