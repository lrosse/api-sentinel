# API Sentinel

Plataforma para monitoramento de APIs e detecção de mudanças de contrato.

> O projeto está no **Marco 0 (setup)**. A infraestrutura e a comunicação entre os
> componentes estão prontas, mas ainda não há funcionalidades de negócio.

## Pré-requisitos

- Docker Desktop com Docker Compose
- Portas `1433`, `4200`, `5001`, `5002` e `8080` disponíveis

Não é necessário ter SQL Server, .NET ou Node.js instalados para executar via Docker.

## Executar localmente

No PowerShell, a partir da raiz do repositório:

```powershell
Copy-Item .env.example .env
docker compose up --build
```

Antes de usar o ambiente fora de uma máquina local, troque a senha de desenvolvimento em
`.env`. Esse arquivo é ignorado pelo Git e não deve ser versionado.

Depois que os serviços estiverem saudáveis, acesse:

| Componente | Endereço |
| --- | --- |
| Cliente Angular | http://localhost:4200 |
| Health check da API | http://localhost:8080/health |
| Dashboard Hangfire | http://localhost:8080/hangfire |
| Mock API 1 | http://localhost:5001/produtos |
| Mock API 2 | http://localhost:5002/produtos |

O Mock API 2 inclui o campo adicional `categoria`. Ambos os mocks aceitam
`?falhar=true` para responder HTTP 500 e `?atrasar=true` para aguardar três segundos.

Na primeira inicialização, a API cria o banco `ApiSentinel` e aplica automaticamente todas
as migrations pendentes antes de começar a aceitar requisições.

## Validação rápida

Com os containers rodando, execute em outro PowerShell:

```powershell
Invoke-RestMethod http://localhost:8080/health
Invoke-RestMethod http://localhost:5001/produtos
Invoke-RestMethod http://localhost:5002/produtos
curl.exe -i "http://localhost:5001/produtos?falhar=true"
Measure-Command { Invoke-RestMethod "http://localhost:5002/produtos?atrasar=true" }
docker compose ps
```

Para encerrar os serviços:

```powershell
docker compose down
```

Use `docker compose down --volumes` somente quando também quiser apagar os dados locais do
SQL Server e o volume de dependências do cliente.
