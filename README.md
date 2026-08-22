# API Sentinel

Plataforma para monitoramento de APIs e detecção de mudanças de contrato.

> O projeto está no **Marco 2**. A infraestrutura reproduzível, autenticação por cookie,
> catálogo privado, monitores manuais protegidos contra SSRF e histórico estão implementados.

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
`?falhar=true` para responder HTTP 500, `?atrasar=true` para aguardar três segundos e
`?grande=true` para devolver um corpo acima do limite de 1 MB do executor.

Na primeira inicialização, a API cria o banco `ApiSentinel` e aplica automaticamente todas
as migrations pendentes antes de começar a aceitar requisições.

## Autenticação, catálogo e monitoramento manual

Abra o cliente Angular, crie uma conta e use a tela de catálogo para cadastrar APIs e seus
endpoints. Cada registro fica obrigatoriamente vinculado ao usuário autenticado; tentativas de
acessar um registro de outro usuário respondem `404`, sem revelar que ele existe.

A autenticação usa o ASP.NET Core Identity com cookie `HttpOnly`, `Secure` e
`SameSite=Strict`. O modo `Strict` foi mantido porque o proxy de desenvolvimento serve as
chamadas de API pela mesma origem do Angular. As chaves do ASP.NET Data Protection são
persistidas no volume `data_protection_keys`, portanto os cookies não dependem do ciclo de vida
efêmero do container da API.

Na tela da API, abra um endpoint pelo link **Monitores**. A tela de detalhe permite criar mais
de um monitor para a mesma rota, configurar timeout e status esperado, usar **Testar agora** e
consultar as últimas 50 execuções. Ainda não há agendamento: todas as execuções são manuais.

O executor aceita somente HTTP/HTTPS, aplica timeout, até três redirecionamentos e no máximo
1 MB de resposta. Antes da chamada e dentro de `SocketsHttpHandler.ConnectCallback`, o host é
resolvido e seus IPs são validados; a conexão TCP usa diretamente o IP aprovado. Redes não
públicas, loopback, link-local e metadata de nuvem são bloqueados. Somente `mock-api-1` e
`mock-api-2` estão na allowlist exata de desenvolvimento/demo; a allowlist de produção é vazia.

Endpoints disponíveis:

- autenticação: `/auth/register`, `/auth/login`, `/auth/logout` e `/auth/me`;
- catálogo: `/api-services`, `/api-services/{id}/endpoints` e `/endpoints/{id}`;
- monitores: `/endpoints/{id}/monitors`, `/monitors/{id}`, `/monitors/{id}/run` e
  `/monitors/{id}/runs`.

Todos os endpoints do catálogo exigem um cookie de autenticação válido.

## Testes

Para executar a suíte de integração dos Marcos 1 e 2:

```powershell
dotnet test src/ApiSentinel.sln
```

Os testes sobem a aplicação em memória e fazem chamadas HTTP reais. Além do Marco 1, validam
CRUD e autorização dos monitores, bloqueio SSRF de IP privado e metadata de nuvem, allowlist
dos dois mocks, timeout, limite de resposta, sanitização do trecho do corpo e concorrência.

## Validação rápida

Com os containers rodando, execute em outro PowerShell:

```powershell
Invoke-RestMethod http://localhost:8080/health
Invoke-RestMethod http://localhost:5001/produtos
Invoke-RestMethod http://localhost:5002/produtos
curl.exe -i "http://localhost:5001/produtos?falhar=true"
Measure-Command { Invoke-RestMethod "http://localhost:5002/produtos?atrasar=true" }
docker compose ps
dotnet test src/ApiSentinel.sln
```

Para encerrar os serviços:

```powershell
docker compose down
```

Use `docker compose down --volumes` somente quando também quiser apagar os dados locais do
SQL Server e o volume de dependências do cliente.
