# ADR-0002: Reserva por OS, idempotente e com concorrência otimista

**Status**: Accepted
**Date**: 2026-10-21
**Decided by**: time GearFlow

## Context

- No monólito, reservar estoque era mexer em dois contadores (`quantity` → `reserved_quantity`) com SQL cru,
  chamado pelo `Workshop` dentro da mesma transação. **Não existia registro de quem reservou o quê.**
- Na Saga (`docs/fluxo-saga.md` do gearflow-budget-service), `ReserveStock` precisa ter compensação
  (`ReleaseStock`) e tolerar mensagem duplicada ou fora de ordem (RabbitMQ entrega "pelo menos uma vez").
- Com só os contadores, liberar uma reserva que nunca foi feita, ou que já foi liberada, corromperia o saldo.
- O `SqlInventoryReservation` era seguro contra concorrência por usar `UPDATE … WHERE quantity >= @q`. Os
  agregados `Part`/`Consumable` não tinham token de concorrência.

## Decision

### 1. Agregado `StockReservation`, um por OS

`StockReservation` guarda o `ServiceOrderId` (índice **único**), as linhas reservadas (item, nome, quantidade) e
o estado `Reserved → Released | Consumed`. `Released` e `Consumed` são finais.

### 2. Idempotência pelo estado da reserva

| Mensagem | Estado atual | Efeito |
|---|---|---|
| `ReserveStock` | sem reserva | reserva tudo ou nada; `StockReserved` ou `StockInsufficient` |
| `ReserveStock` | `Reserved`/`Consumed` | só reenvia `StockReserved`; não reserva de novo |
| `ReserveStock` | `Released` | ignorada (a compensação já rodou) |
| `ReleaseStock` | sem reserva | grava a reserva já `Released` (tombstone) e responde `StockReleased` |
| `ReleaseStock` | `Reserved` | devolve o reservado ao disponível |
| `ReleaseStock` | `Released` | só responde de novo |
| `ReleaseStock` | `Consumed` | conflito: depois da execução não há compensação |
| `ConsumeStock` | `Reserved` | baixa o reservado; `StockConsumed` (+ `LowStockAlert`) |
| `ConsumeStock` | `Consumed` | só responde de novo |
| `ConsumeStock` | `Released` ou sem reserva | erro (sobe para retry/DLQ) |

O tombstone cobre a mensagem fora de ordem: se o `ReleaseStock` chega antes do `ReserveStock`, o `ReserveStock`
atrasado encontra a reserva `Released` e é ignorado.

### 3. Tudo ou nada

`ReserveStock` carrega todos os itens, confere a disponibilidade de **todos** e só então reserva. Se algum falta,
nada é alterado e o `StockInsufficient` lista **todos** os itens faltantes, não só o primeiro.

### 4. Concorrência otimista com `xmin`

`Part`, `Consumable` e `StockReservation` têm o `xmin` do Postgres como token de concorrência (shadow property:
o domínio não o conhece). Duas reservas disputando a última unidade: a segunda a gravar recebe
`DbUpdateConcurrencyException`, traduzida para `ConcurrencyConflictException`. O consumer deixa a exceção subir,
o MassTransit repete a mensagem e a reexecução enxerga o saldo novo (e responde `StockInsufficient`). O mesmo
vale para duas mensagens criando a reserva da mesma OS: a segunda viola o índice único e é repetida.

Dentro de um consumer a transação roda em isolamento **Serializable** (padrão do Outbox do MassTransit), então a disputa
chega ao banco como falha de serialização (`40001`) e não como `DbUpdateConcurrencyException`. O `EfUnitOfWork` trata os
dois casos, além de deadlock (`40P01`) e unicidade (`23505`), como o mesmo `ConcurrencyConflictException`. Isso foi
**descoberto pelos testes de integração** (o log mostrou o `40001` embrulhado numa `InvalidOperationException`); o retry
já resolvia o resultado, mas o erro não era reconhecido. Ver [ADR-0004](0004-masstransit-8-com-outbox-no-ef-core.md).

**Why**: o registro da reserva é o que torna a compensação e a idempotência possíveis; a concorrência otimista
mantém o modelo de domínio testável sem mocks e sem SQL cru.

## Consequences

### Positive
- A Saga compensa com segurança: liberar é idempotente e nunca deixa o saldo negativo.
- A reserva é consultável por OS (`GET /api/inventory/reservations/service-order/{id}`), útil na demo.
- Regras testadas só com domínio e fakes em memória.

### Negative
- Uma tabela a mais (`stock_reservations` + `stock_reservation_lines`) e um índice único.
- Sob muita disputa pelo mesmo item, há retentativas (aceitável: o volume de uma oficina é baixo).

### Mitigations
- Política de retry do MassTransit (1s, 5s, 15s) cobre o conflito; depois disso a mensagem vai para a DLQ.
- Testes de integração sobre um Postgres e um RabbitMQ reais: duas reservas da última unidade (`xmin` e transações
  Serializable) e duas OS disputando a última unidade via mensagens.

## Alternatives Considered

1. **Manter só os contadores e o `UPDATE` com guarda** — descartado: não dá para compensar nem deduplicar.
2. **Lock pessimista (`SELECT … FOR UPDATE`)** — descartado: mais acoplado ao banco e segura locks durante o processamento.
3. **Fila de espera de reservas dentro do Inventory** — descartado por ora: o Inventory fica sem estado de espera
   e o orquestrador reenvia `ReserveStock` ao receber `StockReplenished`.

## Related

- ADR-0001 — PostgreSQL em instância própria
- ADR-0003 — baixa de estoque comandada pela Saga
- `gearflow-budget-service/docs/fluxo-saga.md` — fluxo completo da Saga
