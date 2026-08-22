var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var produtos = new[]
{
    new Produto(1, "Teclado mecânico", 349.90m),
    new Produto(2, "Mouse sem fio", 159.90m),
    new Produto(3, "Monitor 27 polegadas", 1899.00m)
};

app.MapGet("/produtos", async (bool? falhar, bool? atrasar) =>
{
    if (falhar is true)
    {
        return Results.StatusCode(StatusCodes.Status500InternalServerError);
    }

    if (atrasar is true)
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
    }

    return Results.Ok(produtos);
});

app.Run();

internal sealed record Produto(int Id, string Nome, decimal Preco);
