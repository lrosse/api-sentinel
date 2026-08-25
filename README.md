# API Sentinel

Plataforma pessoal para monitoramento de APIs, detecção de mudanças de contrato e gestão do
ciclo de incidentes.

> O **MVP está funcionalmente completo**. O produto cobre infraestrutura reproduzível,
> autenticação, catálogo, execução HTTP protegida contra SSRF, agendamento, diff estrutural
> recursivo e incidentes automáticos com recuperação e resolução manual.

## Pré-requisitos

- Docker Desktop com Docker Compose
- Portas `1433`, `4200`, `5001`, `5002` e `8080` disponíveis

Não é necessário ter SQL Server, .NET ou Node.js instalados para executar via Docker.

## Executar localmente

No PowerShell, a partir da raiz do repositório:

```powershell
Copy-Item .env.example .env
docker compose up --build -d
docker compose ps
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

Como ferramenta exclusiva de teste e demonstração, o `mock-api-1` lê `CONTRACT_MODE` ao
iniciar: `v1` devolve o contrato original; `v2` acrescenta `categoria`; `v3` mantém
`categoria`, remove `nome` e muda `id` de número para string. O padrão é `v1`.

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

## Produto completo

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
agendamento pausado e retomado sem apagar o histórico. O campo **Falhas consecutivas para abrir
incidente** define o limiar por monitor e usa `3` como padrão. Recurring jobs nativos do Hangfire
têm granularidade de minuto, então o backend rejeita intervalos menores que 60 segundos.

Cada execução bem-sucedida também cria um `SchemaSnapshot` contendo somente a lista canônica
de paths e tipos — valores da resposta nunca são persistidos nem comparados. O diff é recursivo,
usa a forma do primeiro elemento para arrays e compara sempre com o snapshot imediatamente
anterior. Campo adicionado é compatível; campo removido ou tipo alterado é quebrador. Paths
ignorados e todos os seus descendentes ficam fora tanto do snapshot quanto do diff.

A análise é limitada por padrão a 10 níveis e 500 campos. Uma resposta acima desses limites
continua gerando um snapshot marcado como `TooComplex`, sem derrubar a execução e sem produzir
um diff parcial potencialmente enganoso. A tela do monitor mostra o estado mais recente do
contrato e o histórico detalhado das mudanças por path.

A home autenticada é o dashboard (`/dashboard`). Ela carrega um único resumo agregado por API,
com o último status, horário, latência, sequência atual de falhas, limiar configurado e incidente
ativo de cada monitor.

Uma falha abre um incidente quando a sequência alcança o limiar do monitor. Falhas posteriores
acrescentam evidências ao mesmo incidente aberto, sem duplicá-lo. Uma mudança de contrato
classificada como `Breaking` abre o incidente imediatamente, sem esperar o limiar. A primeira
execução saudável seguinte move automaticamente o estado de `Open` para `Recovered`; somente o
usuário confirma `Resolved` na tela de detalhe. A causa raiz é texto livre opcional e nunca é
inferida ou preenchida pelo sistema.

A seção **Incidentes** lista todos os estados, permite filtrar por status e mostra a linha do
tempo com as execuções e mudanças de contrato relacionadas. O fluxo normal é
`Open → Recovered → Resolved`, embora a UI também permita resolver manualmente um incidente ainda
aberto quando isso for necessário.

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
- contratos: `/monitors/{id}/contract-changes` e
  `/monitors/{id}/schema-snapshot/latest`;
- dashboard: `/dashboard/summary`.
- incidentes: `GET /incidents`, `GET /incidents/{id}` e
  `POST /incidents/{id}/resolve`.

Todos os endpoints de catálogo, monitoramento, dashboard e incidentes exigem um cookie de
autenticação válido e aplicam isolamento por proprietário com resposta `404` para recursos de
outro usuário.

## Testes

Para executar a suíte completa de integração dos Marcos 1 a 5:

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
isolamento do dashboard por usuário. O Marco 4 acrescenta cobertura para adição, remoção e
troca de tipo, path aninhado, arrays de objetos, paths ignorados, snapshots idênticos, limites
de profundidade/campos e a deriva controlada `v1 → v2 → v3`. O Marco 5 valida limiar de falhas,
ausência de duplicação, evidências, recuperação automática, abertura imediata por mudança
quebradora, resolução manual com causa raiz, autorização e o fluxo HTTP completo do cadastro ao
estado `Resolved`.

## Demo reproduzível do MVP do zero

O bootstrap aplica migrations e o seed é idempotente. A partir da raiz, sem remover volumes:

```powershell
Copy-Item .env.example .env -ErrorAction SilentlyContinue
$env:SEED_DEV_DATA = "true"
docker compose down
docker compose up --build -d
Remove-Item Env:SEED_DEV_DATA
docker compose ps
Start-Process http://127.0.0.1:4200
```

Espere todos os serviços aparecerem como `healthy`. Entre com `dev@apisentinel.local` /
`DevSentinel#2026`; o seed já fornece duas APIs, seus endpoints e monitores ativos. O SQL Server
não precisa estar instalado na máquina.

### Fluxo manual exato: falha → recuperação → resolução

1. Em **Catálogo**, cadastre uma API chamada `Demo Incidentes`, com URL base
   `http://mock-api-1:8080`.
2. Adicione o endpoint `GET /produtos?falhar=true`, abra **Monitores** e crie um monitor com
   status esperado `200`, limiar `3` e **Agendamento ativo** desmarcado.
3. Clique em **Testar agora** três vezes. Cada execução deve mostrar `Falha`/HTTP 500; na terceira,
   abra **Incidentes** e confirme um incidente `Aberto` com motivo `3 falhas consecutivas.`.
4. Clique em **Testar agora** uma quarta vez e abra o detalhe do mesmo incidente. A timeline deve
   conter **Evidência adicionada**, sem um segundo incidente.
5. Volte à API `Demo Incidentes`, altere o endpoint para `/produtos`, retorne ao monitor e clique em
   **Testar agora**. A execução deve ter sucesso e o incidente deve mudar automaticamente para
   `Recuperado`.
6. No detalhe do incidente, informe opcionalmente uma causa raiz confirmada e clique em
   **Confirmar resolução**. O estado final deve ser `Resolvido`, com o evento de resolução manual
   na timeline.

## Demonstração manual da deriva de contrato

Suba todo o ambiente com o contrato original, preservando os volumes existentes:

```powershell
docker compose down
$env:CONTRACT_MODE = "v1"
docker compose up --build -d
Remove-Item Env:CONTRACT_MODE
docker compose ps
Start-Process http://127.0.0.1:4200
```

Na UI, entre com seu usuário. Se `SEED_DEV_DATA=true`, use `dev@apisentinel.local` /
`DevSentinel#2026`, abra **Catálogo → Mock API 1 → GET /produtos → Monitores**, crie um monitor
temporário com **Agendamento ativo** desmarcado e clique em **Testar agora**. A primeira
execução cria o baseline e mostra **Sem mudanças**.

Troque somente o mock para `v2` e execute o mesmo monitor novamente pela UI:

```powershell
$env:CONTRACT_MODE = "v2"
docker compose up -d --force-recreate mock-api-1
Remove-Item Env:CONTRACT_MODE
docker compose ps mock-api-1
```

O estado esperado é **Mudança compatível detectada**, com `categoria` como `Adicionado`.
Depois troque para `v3` e clique novamente em **Testar agora**:

```powershell
$env:CONTRACT_MODE = "v3"
docker compose up -d --force-recreate mock-api-1
Remove-Item Env:CONTRACT_MODE
docker compose ps mock-api-1
```

O estado esperado é **Mudança quebradora detectada**, com `nome` como `Removido` e `id` como
`Tipo alterado` (`Number → String`). Essa execução também abre imediatamente um incidente com
origem na mudança quebradora. Para devolver o mock ao contrato original:

```powershell
$env:CONTRACT_MODE = "v1"
docker compose up -d --force-recreate mock-api-1
Remove-Item Env:CONTRACT_MODE
```

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

## Deliberadamente fora do MVP

O escopo final não inclui organizações/RBAC, múltiplos ambientes, importação OpenAPI, Redis,
notificações por e-mail ou webhook, resumo por IA, OpenTelemetry, página pública de status,
SDK/CLI nem cobrança. São possibilidades de v2/v3, não dependências ocultas do produto atual.
Antes de qualquer exposição pública, o dashboard do Hangfire também deve receber autenticação
própria; em desenvolvimento local ele permanece aberto como dívida técnica conhecida.
