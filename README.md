# API Sentinel

Plataforma para monitoramento de APIs e detecção de mudanças de contrato.

> O projeto está no **Marco 3**. A infraestrutura reproduzível, autenticação por cookie,
> catálogo privado, execução manual protegida contra SSRF, agendamento via Hangfire, histórico
> e dashboard operacional estão implementados.

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

| Componente          | Endereço                       |
| ------------------- | ------------------------------ |
| Cliente Angular     | http://localhost:4200          |
| Health check da API | http://localhost:8080/health   |
| Dashboard Hangfire  | http://localhost:8080/hangfire |
| Mock API 1          | http://localhost:5001/produtos |
| Mock API 2          | http://localhost:5002/produtos |

O Mock API 2 inclui o campo adicional `categoria`. Ambos os mocks aceitam
`?falhar=true` para responder HTTP 500, `?atrasar=true` para aguardar três segundos e
`?grande=true` para devolver um corpo acima do limite de 1 MB do executor.

Na primeira inicialização, a API cria o banco `ApiSentinel` e aplica automaticamente todas
as migrations pendentes antes de começar a aceitar requisições.

### Seed opcional de desenvolvimento

Para criar dados locais de demonstração automaticamente, defina a flag no `.env` antes de
subir os containers:

```dotenv
SEED_DEV_DATA=true
```

Ou habilite apenas para uma execução no PowerShell:

```powershell
$env:SEED_DEV_DATA = "true"
docker compose up --build -d
Remove-Item Env:SEED_DEV_DATA
```

O seed é idempotente e cria, quando ausentes:

- usuário local `dev@apisentinel.local`, senha `DevSentinel#2026`;
- `Mock API 1` em `http://mock-api-1:8080`;
- `Mock API 2` em `http://mock-api-2:8080`;
- endpoint `GET /produtos` e um monitor ativo de 60 segundos para cada mock.

Essas credenciais são exclusivamente para desenvolvimento local. O código exige ao mesmo
tempo `SeedData:Enabled=true` e ambiente `Development`; se a flag for ligada em qualquer outro
ambiente, a aplicação recusa a inicialização.

## Autenticação, catálogo e monitoramento

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
consultar as últimas 50 execuções. Cada monitor também possui um intervalo e pode ter o
agendamento pausado e retomado sem apagar o histórico. Recurring jobs nativos do Hangfire têm
granularidade de minuto, então o backend rejeita intervalos menores que 60 segundos.

A home autenticada é o dashboard (`/dashboard`). Ela carrega um único resumo agregado por API,
com o último status, horário, latência e a sequência atual de falhas de cada monitor. A sequência
é apenas visual neste marco; ainda não cria incidentes.

O executor aceita somente HTTP/HTTPS, aplica timeout, até três redirecionamentos e no máximo
1 MB de resposta. Antes da chamada e dentro de `SocketsHttpHandler.ConnectCallback`, o host é
resolvido e seus IPs são validados; a conexão TCP usa diretamente o IP aprovado. Redes não
públicas, loopback, link-local e metadata de nuvem são bloqueados. Somente `mock-api-1` e
`mock-api-2` estão na allowlist exata de desenvolvimento/demo; a allowlist de produção é vazia.

Endpoints disponíveis:

- autenticação: `/auth/register`, `/auth/login`, `/auth/logout` e `/auth/me`;
- catálogo: `/api-services`, `/api-services/{id}/endpoints` e `/endpoints/{id}`;
- monitores: `/endpoints/{id}/monitors`, `/monitors/{id}`, `/monitors/{id}/run` e
  `/monitors/{id}/runs`;
- dashboard: `/dashboard/summary`.

Todos os endpoints do catálogo exigem um cookie de autenticação válido.

## Testes

Para executar a suíte de integração dos Marcos 1, 2 e 3:

```powershell
dotnet test src/ApiSentinel.sln
```

O build do cliente verifica automaticamente se todos os prefixos públicos detectados no
backend possuem regra em `client/proxy.conf.json`. A verificação também pode ser executada
isoladamente:

```powershell
cd client
npm run check:proxy
```

Os testes sobem a aplicação em memória e fazem chamadas HTTP reais. Além do Marco 1, validam
CRUD e autorização dos monitores, bloqueio SSRF de IP privado e metadata de nuvem, allowlist
dos dois mocks, timeout, limite de resposta, sanitização do trecho do corpo, agendamento
automático, pausa/retomada, remoção de recurring jobs, concorrência manual × agendada e
isolamento do dashboard por usuário.

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
Start-Process http://127.0.0.1:4200/dashboard
Start-Process http://127.0.0.1:8080/hangfire/recurring
```

Para encerrar os serviços:

```powershell
docker compose down
```

Para validar um novo build preservando usuários, APIs, monitores e histórico:

```powershell
docker compose down
docker compose up --build -d
```

Use `docker compose down --volumes` somente quando um teste de ambiente totalmente limpo for
pedido explicitamente; esse comando apaga os dados locais do SQL Server.
