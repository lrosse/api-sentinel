var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var produtos = new[]
{
    new Produto(1, "Teclado mecânico", 349.90m, "Periféricos"),
    new Produto(2, "Mouse sem fio", 159.90m, "Periféricos"),
    new Produto(3, "Monitor 27 polegadas", 1899.00m, "Monitores")
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

    return Results.Ok(produtos);
});

app.Run();

internal sealed record Produto(int Id, string Nome, decimal Preco, string Categoria);
