# Como testar o Inventory Service

Guia para validar o serviço **sem depender do `os-service`, do Billing ou do Operation**. Todos os passos abaixo foram executados
na máquina de desenvolvimento (Windows, PowerShell) e os resultados mostrados são os que apareceram de verdade.

---

## 0. Preciso esperar os outros terminarem?

**Não.** A Saga fala com o Inventory só por mensagens do RabbitMQ, então basta alguém no papel do orquestrador. Esse papel é do **simulador**
(`tools/GearFlow.Inventory.SagaSimulator`), que usa o MassTransit e os mesmos `GearFlow.Contracts`: ele fala exatamente o protocolo real.

| | Dá para testar agora? | Como |
|---|---|---|
| Regras de estoque (reservar, liberar, consumir, tudo ou nada, idempotência) | ✅ Sim | [Seção 2](#2-caminho-a-testes-automatizados) e [Seção 3](#3-caminho-b-teste-manual-com-o-simulador) |
| API REST (CRUD de peças e insumos, erros 400/404/409, JWT) | ✅ Sim | [Seção 5](#5-testar-pela-api-rest) |
| Mensageria (Outbox, Inbox, retry, DLQ, e-mails) | ✅ Sim | Seções 2, 3 e 6 |
| Resiliência (API fora do ar, RabbitMQ cai e volta) | ✅ Sim | [Seção 6](#6-testar-falhas-e-resiliência) |
| **Fluxo completo de uma OS** passando por OS → Billing → Operation → Inventory | ❌ Só quando os outros ficarem prontos | [Seção 7](#7-o-que-depende-dos-outros) |
| Deploy no Kubernetes, Kong, New Relic | ❌ Fase 8 (ainda não feita) | — |

---

## 1. Pré-requisitos

- **.NET 10 SDK** (`dotnet --version`) e **Docker Desktop ligado** (`docker ps` não pode dar erro).
- Portas livres no seu computador: **5433** (Postgres), **5672** e **15672** (RabbitMQ), **1025** e **8025** (Mailpit), **5081** (API).
- Todos os comandos rodam na **raiz do repositório** `gearflow-inventory-service`, no PowerShell.

---

## 2. Caminho A: testes automatizados

Sem montar nada à mão. Os testes de integração sobem Postgres, RabbitMQ e Mailpit sozinhos (Testcontainers).

```powershell
# 249 testes no total
dotnet test GearFlow.Inventory.slnx -c Release

# ou separados
dotnet test tests/GearFlow.Inventory.UnitTests -c Release          # 207 testes, ~5 s, sem Docker
dotnet test tests/GearFlow.Inventory.IntegrationTests -c Release   # 42 testes, ~35 s, precisa do Docker
```

Na primeira vez a integração demora mais porque baixa as imagens (`postgres:17`, `rabbitmq:4-management`, `axllent/mailpit`).

| Suíte | O que prova |
|---|---|
| Unitários | Regras do domínio, handlers da Saga (tudo ou nada, idempotência, fora de ordem), validators, consumers, arquitetura (camadas, endpoints documentados e protegidos) |
| Integração, `Persistence` | Migrations, seed, **duas reservas da última unidade não commitam as duas**, índice único por OS, reserva e Outbox no mesmo commit |
| Integração, `Messaging` | A API completa com RabbitMQ real: reserva, falta de estoque, liberação, consumo, repetição (Inbox), `Release` antes do `Reserve`, DLQ, `PATCH` via Outbox |
| Integração, `Rest` | O contrato REST do frontend: CRUD, `400/404/409` com `code`, OpenAPI com 13 operações, `401` |
| Integração, `Email` | Envio SMTP real contra um Mailpit |

---

## 3. Caminho B: teste manual com o simulador

Para **ver** a Saga acontecendo: as respostas chegando, o e-mail no Mailpit, a mensagem na DLQ. Leva uns 10 minutos.

### Passo 1. Subir as dependências

```powershell
docker compose up -d
docker compose ps        # postgres, rabbitmq e mailpit devem estar "healthy"
```

### Passo 2. Subir a API (outro terminal)

```powershell
dotnet run --project src/GearFlow.Inventory.Api
```

Em Development ela aplica as migrations e semeia o catálogo de demonstração (10 peças e 7 insumos). Confirme:

```powershell
Invoke-RestMethod http://localhost:5081/health/ready
# status: Healthy  (checks: masstransit-bus e Postgres)
```

### Passo 3. Gerar um token para a API REST

A API só aceita JWT, e o Inventory não emite token (o login é de outro serviço). O script abaixo gera um token **de desenvolvimento**, assinado com a
chave do `appsettings.Development.json`:

```powershell
$token = .\scripts\New-DevToken.ps1
$h = @{ Authorization = "Bearer $token" }
Invoke-RestMethod http://localhost:5081/api/inventory/parts -Headers $h    # 10 peças
```

O token vale **60 minutos** (`-Minutes` muda). Sem token a API responde `401`.

### Passo 4. Rodar todos os cenários

```powershell
dotnet run --project tools/GearFlow.Inventory.SagaSimulator -- scenario all
```

Resultado esperado (resumido):

```
=== Cenário 'happy'
  [  OK  ] StockReserved com as 2 linhas
  [  OK  ] StockConsumed
=== Cenário 'shortage'
  [  OK  ] StockInsufficient
  [  OK  ] lista só as palhetas como faltantes (o Filtro tem saldo)
  [  OK  ] nada foi reservado (tudo ou nada): nenhum StockReserved
=== Cenário 'compensation'   [  OK  ] StockReserved   [  OK  ] StockReleased
=== Cenário 'lowstock'       [  OK  ] StockReserved   [  OK  ] StockConsumed   [  OK  ] LowStockAlert da água desmineralizada
=== Cenário 'outoforder'     [  OK  ] StockReleased   [  OK  ] o ReserveStock atrasado foi ignorado
=== Cenário 'poison'         [  OK  ] nenhuma resposta (falha permanente não responde)
RESULTADO: os 6 cenários passaram.
```

O código de saída é `0` se tudo passou e `1` se algo falhou (serve para script).

### Passo 5. Conferir nos lugares certos

| Onde | Endereço | O que olhar |
|---|---|---|
| **API (Scalar)** | http://localhost:5081/scalar/v1 | Documentação interativa. Clique em **Authorize** e cole o token |
| **RabbitMQ** | http://localhost:15672 (`guest` / `guest`), aba **Queues** | As 5 filas `inventory-*` com 1 consumidor cada; a `inventory-reserve-stock_error` com a mensagem do cenário `poison` |
| **Mailpit** | http://localhost:8025 | 2 e-mails em `estoque@gearflow.com`: "Estoque insuficiente para a OS …" (cenário `shortage`) e "Estoque baixo: Água desmineralizada (L)" (cenário `lowstock`) |
| **Postgres** | `docker exec -i gearflow-inventory-service-postgres-1 psql -U gearflow -d gearflow_inventory` | Ver abaixo |

Consultas úteis no Postgres (no PowerShell, mande o SQL pela entrada padrão para não brigar com as aspas):

```powershell
'select status, count(*) from inventory.stock_reservations group by status;' | docker exec -i gearflow-inventory-service-postgres-1 psql -U gearflow -d gearflow_inventory
'select count(*) as outbox_pendentes from inventory."OutboxMessage";'        | docker exec -i gearflow-inventory-service-postgres-1 psql -U gearflow -d gearflow_inventory   # 0 = tudo entregue
```

Depois de rodar `scenario all` uma vez, o esperado é: 2 reservas `Consumed` (`happy` e `lowstock`) e 2 `Released` (`compensation` e a de `outoforder`,
que nasce já liberada).

---

## 4. Os cenários, um a um

Cada um roda sozinho: `dotnet run --project tools/GearFlow.Inventory.SagaSimulator -- scenario <nome>`.

| Cenário | O que o simulador faz | O que prova |
|---|---|---|
| `happy` | Reserva Filtro de óleo ×2 e Óleo 5W30 ×4,5 L, depois consome | Passos 6 e 7b da Saga. O disponível cai na reserva; o consumo baixa só o reservado |
| `shortage` | Pede Jogo de palhetas ×5 (há 4) junto com Filtro ×1 (há de sobra) | **Tudo ou nada**: o Filtro também não é reservado. `StockInsufficient` lista só o que falta. Sai e-mail ao estoquista |
| `compensation` | Reserva e depois manda `ReleaseStock` | Compensação: o saldo volta ao original |
| `lowstock` | Reserva e consome Água desmineralizada ×0,5 L | `LowStockAlert` e e-mail "Estoque baixo". Cada execução gasta 0,5 L |
| `outoforder` | Manda `ReleaseStock` **antes** do `ReserveStock` | Mensagem fora de ordem: o `ReserveStock` atrasado é ignorado |
| `poison` | Manda `ReserveStock` sem itens | Falha permanente: **sem resposta** e direto para `inventory-reserve-stock_error` |

> **Os cenários gastam o estoque de demonstração** (o `happy` consome 2 filtros e 4,5 L de óleo; o `lowstock`, 0,5 L de água). Para voltar ao
> começo: `docker compose down -v` e `docker compose up -d` de novo (o seed recria o catálogo; ele só roda com o banco vazio).

---

## 5. Testar pela API REST

Abra http://localhost:5081/scalar/v1, clique em **Authorize** e cole o token do passo 3 (sem o `Bearer `). Ou use o PowerShell:

```powershell
$h = @{ Authorization = "Bearer $(.\scripts\New-DevToken.ps1)" }
$api = 'http://localhost:5081/api/inventory'
```

| O que tentar | Comando | Resultado esperado |
|---|---|---|
| Listar peças e insumos | `Invoke-RestMethod "$api/parts" -Headers $h` | 10 peças (a "Jogo de palhetas" com `belowMinimum = True`) |
| Criar uma peça | `Invoke-RestMethod "$api/parts" -Method Post -Headers $h -ContentType 'application/json' -Body '{"name":"Vela","description":"d","partNumber":"V-1","manufacturer":"NGK","priceCents":4500,"quantity":10}'` | Devolve a peça com `id` |
| Dados inválidos | mesmo `POST` com `"name":""` | `400` com `code = Part.NameRequired` |
| Repor estoque | `Invoke-RestMethod "$api/parts/<id>/stock" -Method Patch -Headers $h -ContentType 'application/json' -Body '{"quantity":10}'` | Soma ao disponível e publica `StockReplenished` (veja com o `listen`, abaixo) |
| Peça inexistente | `GET $api/parts/<guid qualquer>` | `404` com `code = Part.NotFound` |
| Apagar item **reservado** | reserve com o simulador e depois `DELETE $api/parts/<id>` | `409` com `code = Part.HasActiveReservations` |
| Ver a reserva de uma OS | `GET $api/reservations/service-order/<id da OS>` | `status` (`Reserved`, `Released` ou `Consumed`) e as linhas |
| Sem token | qualquer rota sem `Authorization` | `401` |

Para ver as mensagens que a API publica, deixe o `listen` aberto em outro terminal e faça o `PATCH` de reposição:

```powershell
dotnet run --project tools/GearFlow.Inventory.SagaSimulator -- listen
# 12:35:33  <- StockReplenished  {"messageId":"...","itemType":"Part","itemId":"a1000000-0000-0000-0000-000000000001"}
```

### Comandos avulsos do simulador

Para montar o seu próprio fluxo (os IDs do catálogo estão no [README](../README.md#catálogo-de-demonstração-ids-fixos)):

```powershell
$sim = 'tools/GearFlow.Inventory.SagaSimulator'
$os = [guid]::NewGuid()
$filtro = 'a1000000-0000-0000-0000-000000000001'; $oleo = 'b2000000-0000-0000-0000-000000000001'

dotnet run --project $sim -- reserve --order $os --part "${filtro}:2" --consumable "${oleo}:1.5"   # -> StockReserved
Invoke-RestMethod "http://localhost:5081/api/inventory/reservations/service-order/$os" -Headers $h  # status = Reserved
dotnet run --project $sim -- consume --order $os                                                    # -> StockConsumed
dotnet run --project $sim -- release --order $os --reason "teste"                                   # reserva consumida: sem resposta, vai para a DLQ
```

Veja todas as opções com `dotnet run --project tools/GearFlow.Inventory.SagaSimulator -- help`.

---

## 6. Testar falhas e resiliência

Os itens que o vídeo da Fase 4 pede ("derrubar um serviço no meio do fluxo"), do lado do Inventory.

### 6.1 A API está fora do ar quando a mensagem chega

```powershell
# 1) pare a API (Ctrl+C no terminal dela)
# 2) mande a reserva: o simulador vai dizer "SEM RESPOSTA", é esperado
dotnet run --project tools/GearFlow.Inventory.SagaSimulator -- reserve --order $os --part "a1000000-0000-0000-0000-000000000002:1" --wait 2
# 3) suba a API de novo
dotnet run --project src/GearFlow.Inventory.Api
```

**Esperado:** a mensagem esperou na fila (que é durável) e foi processada assim que a API voltou: a reserva passa a existir
(`GET .../reservations/service-order/$os` → `Reserved`) e o `StockReserved` é publicado (deixe o `listen` aberto para ver). **Nada se perde.**

### 6.2 O RabbitMQ cai e volta

```powershell
docker stop gearflow-inventory-service-rabbitmq-1
Invoke-RestMethod http://localhost:5081/health/ready    # status: Degraded  (HTTP 200)
# a API REST continua funcionando; um PATCH de estoque responde OK e a mensagem fica retida na tabela do Outbox
docker start gearflow-inventory-service-rabbitmq-1
```

**Esperado (medido):** durante a queda o `/health/ready` fica **`Degraded`** (HTTP 200, a REST continua servindo); a publicação do `PATCH` fica na tabela
`OutboxMessage`. Cerca de **26 s depois** do `docker start` o serviço volta a `Healthy` sozinho (sem reiniciar a API) e entrega o que estava retido
(`outbox_pendentes` volta a 0).

### 6.3 O banco cai

```powershell
docker stop gearflow-inventory-service-postgres-1
try { Invoke-WebRequest http://localhost:5081/health/ready -UseBasicParsing } catch { $_.Exception.Response.StatusCode.value__ }   # 503
(Invoke-WebRequest http://localhost:5081/health/live -UseBasicParsing).StatusCode                                                  # 200
docker start gearflow-inventory-service-postgres-1
```

**Esperado (medido):** com o banco parado o `/health/ready` devolve `503` e o `/health/live` continua `200` (o processo está vivo; no Kubernetes isso tira
o pod do tráfego sem reiniciá-lo). Quando o Postgres volta, o `/health/ready` volta a `200` em poucos segundos (~2 s medidos), sem reiniciar a API, e a
Saga continua funcionando (rode `scenario compensation` para confirmar).

### 6.4 Mensagem inválida (DLQ)

O cenário `poison` (ou `reserve` sem itens). **Esperado:** nenhuma resposta, e a mensagem aparece em `inventory-reserve-stock_error` no RabbitMQ, **sem
passar pelas tentativas de retry** (falha permanente vai direto para a DLQ). Falha transitória (banco fora, conflito de concorrência) é repetida 3 vezes
(1 s, 5 s, 15 s) antes de ir para a DLQ.

### 6.5 Duas OS disputando a última unidade, e mensagem repetida

Esses dois dependem de timing e de forçar a mesma `MessageId`, então ficam nos **testes de integração** (`Messaging`), onde são determinísticos:
`Two_service_orders_competing_for_the_last_unit_...` e `The_same_message_delivered_twice_is_processed_once_by_the_inbox`.

---

## 7. O que depende dos outros

O que **só** dá para validar quando o `os-service` (orquestrador), o Billing e o Operation existirem:

1. **O fluxo ponta a ponta de uma OS**: abrir OS → diagnóstico → orçamento → pagamento → `ReserveStock` → execução → `ConsumeStock` → entrega, e o cenário "pagou, faltou estoque" com reembolso.
2. **Que os dois lados falam a mesma língua.** Quando o `os-service` ficar pronto, confira:
   - os tipos de mensagem têm o **mesmo namespace e nome** (`GearFlow.Contracts:ReserveStock` etc.): compare com `src/GearFlow.Contracts/InventoryMessages.cs`;
   - os campos batem (`CorrelationId` = `ServiceOrderId`, `Items[]` com `ItemType` como **texto**, quantidades decimais);
   - os dois usam o **mesmo RabbitMQ** (host, virtual host, credenciais);
   - o orquestrador tem **timeout**: falha permanente do Inventory (mensagem malformada, `ConsumeStock` sem reserva) não gera resposta, só vai para a DLQ;
   - o orquestrador reenvia `ReserveStock` ao receber `StockReplenished` (é o que o Inventory assume).
3. **A decisão sobre quem manda baixar o estoque** ([ADR-0003](adr/0003-baixa-de-estoque-comandada-pela-saga.md), ainda "Proposed"): o código já suporta `ConsumeStock` enviado pela Saga.
4. **De onde o Operation lê os preços** das peças (depende de decisão; o endpoint em lote ainda não existe).

**Dica para o dia da integração:** troque o simulador pelo `os-service` real e rode de novo os mesmos conferires das seções 3 e 6. Se o simulador passa e o
`os-service` não, o problema está no contrato ou no orquestrador, não no Inventory.

---

## 8. Problemas comuns

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| Simulador: `Não consegui conectar no RabbitMQ` | Broker não está de pé | `docker compose up -d` e `docker compose ps` |
| Simulador: `SEM RESPOSTA` | (1) API parada, ou (2) a mensagem foi rejeitada como falha permanente e está na `*_error` | Veja se a API está rodando; confira as filas `*_error` em http://localhost:15672 |
| `401` na API | Token expirou (60 min), ou a chave/issuer/audience não são os do ambiente | Gere outro com `.\scripts\New-DevToken.ps1` |
| `docker compose up` falha com porta ocupada | Outro programa usa 5433, 5672, 15672, 1025 ou 8025 | Pare o outro programa. **Postgres local na 5432 não atrapalha** (o projeto usa a 5433) |
| Testes de integração falham logo de cara | Docker Desktop desligado | Ligue e confira com `docker ps` |
| Testes de integração lentos na 1ª vez | Baixando as imagens | Esperado; as próximas execuções levam ~35 s |
| Cobertura: `FileLoadException ... Application Control policy ... 0x800711C7` e a suíte inteira falha | O **Smart App Control** do Windows barra os DLLs que o coverlet reescreve | Rode os testes **sem** cobertura (passam). O CI no Linux não tem esse bloqueio. Para medir localmente, use o `dotnet-coverage`. Não desligue o Smart App Control |
| Acentos trocados ao ler e-mails ou JSON no Windows PowerShell 5.1 | Codificação do console | É só exibição. Use a interface do Mailpit, ou o PowerShell 7 |
| `scenario happy` ou `lowstock` passam a falhar depois de várias execuções | O estoque de demonstração acabou | `docker compose down -v`, `docker compose up -d` e suba a API de novo |
| `psql` com aspas dando erro no PowerShell | O PowerShell estraga as aspas duplas em argumentos | Mande o SQL pela entrada padrão (exemplo na seção 3) |

---

## 9. Limpar tudo

```powershell
# pare a API (Ctrl+C) e remova containers, rede e o volume do Postgres
docker compose down -v
```
