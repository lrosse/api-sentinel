var builder = WebApplication.CreateBuilder(args);
var contractMode = (builder.Configuration["CONTRACT_MODE"] ?? "v1")
    .Trim()
    .ToLowerInvariant();
if (contractMode is not ("v1" or "v2" or "v3"))
{
    throw new InvalidOperationException(
        "CONTRACT_MODE deve ser v1, v2 ou v3 no mock-api-1.");
}

var app = builder.Build();

var produtos = new[]
{
    new Produto(1, "Teclado mecânico", 349.90m),
    new Produto(2, "Mouse sem fio", 159.90m),
    new Produto(3, "Monitor 27 polegadas", 1899.00m)
};

app.MapGet("/produtos", async (bool? falhar, bool? atrasar, bool? grande) =>
{
    if (falhar is true)
    {
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    if (atrasar is true)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
    }

    if (grande is true)
    {
        return Results.Text(new string('x', 1_100_000), "text/plain");
    }

    return contractMode switch
    {
        "v2" => Results.Ok(produtos.Select(produto => new
        {
            produto.Id,
            produto.Nome,
            produto.Preco,
            Categoria = "Periféricos"
        })),
        "v3" => Results.Ok(produtos.Select(produto => new
        {
            Id = produto.Id.ToString(),
            produto.Preco,
            Categoria = "Periféricos"
        })),
        _ => Results.Ok(produtos)
    };
});

app.Run();

internal sealed record Produto(int Id, string Nome, decimal Preco);
