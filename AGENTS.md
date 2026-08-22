# AGENTS.md — API Sentinel

Este arquivo existe para que qualquer sessão do Codex (nova ou continuada) tenha o contexto
essencial do projeto sem depender do histórico de chat. Leia isto antes de propor mudanças de
arquitetura, stack ou ordem de implementação.

## O que é o projeto

Plataforma pessoal de monitoramento e detecção de mudança de contrato em APIs. Projeto de
portfólio, desenvolvido por uma pessoa só, com ajuda de IA (Claude para planejamento/prompts,
Codex para execução).

## Stack — decisões fechadas, não reabrir sem motivo forte

- Backend: ASP.NET Core 8, C#.
- Frontend: Angular (standalone components).
- Banco: **SQL Server**, rodando via Docker (não instalado localmente na máquina de dev).
- ORM: EF Core, provider SQL Server.
- Jobs: Hangfire (dashboard em `/hangfire`, ainda sem autenticação própria — dívida técnica
  conhecida, não é bug).
- Orquestração local: Docker Compose.
- Arquitetura: monolito modular, organizado por **vertical slice** (módulos:
  `ApiSentinel.Modules.Identity`, `.ApiCatalog`, `.Monitoring`, `.Incidents`), não por camada
  horizontal.
- **Autenticação: cookie HttpOnly via ASP.NET Core Identity — NÃO usar JWT.** Frontend e
  backend são o mesmo produto, não há necessidade de token portátil.

## Modelo de dados — decisões fechadas

- `ApiService.OwnerUserId` é **obrigatório** (referencia o usuário do Identity). Não criar
  FK de organização opcional/especulativa — se organizações forem implementadas no futuro,
  será uma migration consciente, não uma coluna nulável esperando uso.
- `Monitor` é uma entidade separada de `Endpoint` (dois monitores podem observar o mesmo
  endpoint com regras diferentes).
- Incidente tem estados `Open → Recovered (automático) → Resolved (confirmação manual)`, não
  um binário aberto/fechado.
- Incidente separa `TriggerReason` (automático) de `RootCause` (opcional, manual) — o sistema
  não "sabe" a causa raiz sozinho.
- Comparação de contrato é **recursiva** (por path, ex: `cliente.endereco.cidade`), não
  apenas de primeiro nível. Compara só estrutura (chaves/tipos), nunca valores.

## Segurança — não negociável

- SSRF: proteção entra desde o primeiro executor HTTP (não deixar para o final). Validar
  IP no momento da conexão (`SocketsHttpHandler.ConnectCallback`), não apenas resolver DNS
  antes e confiar em nova resolução do HttpClient.
- Credenciais de endpoints monitorados: nunca retornadas pela API; criptografadas com
  ASP.NET Data Protection API, com key ring persistido em volume (não efêmero no container).
- Dashboard do Hangfire: hoje sem autenticação (aceito por enquanto); precisa ser protegido
  antes de qualquer exposição pública do projeto.

## Escopo do MVP — não adicionar sem avisar

Fora do MVP por decisão explícita: organizações/RBAC, múltiplos ambientes, importação
OpenAPI, Redis, notificações/webhook, resumo por IA, OpenTelemetry, página pública de status,
SDK/CLI, cobrança. Esses itens são v2/v3, não trazer de volta para o MVP sem essa decisão ser
revisada explicitamente com o usuário.

## Roadmap (marcos)

0. Infraestrutura reproduzível (Docker Compose, bootstrap automático do banco) — **concluído
   e commitado**.
1. Autenticação por cookie + catálogo mínimo (`ApiService`, `Endpoint`) — **concluído**.
2. Executor HTTP manual protegido contra SSRF + histórico — **concluído**.
3. Agendamento (Hangfire) + dashboard mínimo.
4. Diff estrutural recursivo + detecção de mudança de contrato.
5. Incidentes (Open/Recovered/Resolved) + testes E2E + demo reproduzível.

## Particularidades do ambiente local (não é bug do projeto)

- A máquina de dev teve um problema de rede (EOF ao puxar imagens de
  `mcr.microsoft.com`), resolvido ajustando o modo de rede do WSL2. Por causa disso, o Docker
  Desktop está configurado em modo `mirrored` no `.wslconfig`. Um efeito colateral conhecido:
  `localhost` pode dar timeout intermitente vindo do PowerShell do Windows; `127.0.0.1`
  funciona normalmente. Isso é do ambiente local, não do projeto.

## Ao propor algo novo

Se uma tarefa parecer exigir reabrir uma decisão listada acima (trocar stack, adiar
autenticação, adicionar algo fora do MVP, simplificar segurança), **pare e avise
explicitamente antes de implementar**, em vez de decidir sozinho. O usuário e o Claude (fora
desta sessão) já fecharam essas decisões após revisão cruzada — mudanças aqui exigem
confirmação explícita, não suposição de que "faz mais sentido assim".
