# ADR-0004: MassTransit 8.5.x com Outbox/Inbox no EF Core

**Status**: Accepted
**Date**: 2026-10-21
**Decided by**: time GearFlow

## Context

- A Saga exige que a resposta ao orquestrador só saia **se** o estoque foi gravado, e que a mesma mensagem entregue
  duas vezes não reserve duas vezes (o RabbitMQ entrega "pelo menos uma vez").
- O README do Billing já planeja MassTransit com Outbox/Inbox. O plano marcava como risco a compatibilidade do
  MassTransit 8.x com EF Core 10 e Npgsql 10.
- Licença: o MassTransit **9.x passou a ser comercial** (`massient.com/license`); a 8.x segue Apache-2.0.
- A transação de um consumer com Outbox roda em isolamento **Serializable** (padrão do MassTransit).

## Decision

1. Usamos **MassTransit 8.5.11** (`MassTransit`, `.RabbitMQ`, `.EntityFrameworkCore`), fixado em `Directory.Packages.props`.
   O pacote da 8.5.11 já tem alvo `net10.0` com `Microsoft.EntityFrameworkCore.Relational` 10. Os quatro serviços devem usar a
   mesma versão, e a subida para a 9.x só com decisão explícita de licença.
2. **Outbox/Inbox no mesmo banco** (`OutboxMessage`, `OutboxState`, `InboxState`, no schema `inventory`):
   - nos **consumers**: `UseEntityFrameworkOutbox` (a resposta entra na transação do consumer; o Inbox deduplica por `MessageId`);
   - nos **endpoints HTTP**: `UseBusOutbox` (ex.: `StockReplenished` do `PATCH …/stock`).
3. A Application não conhece o MassTransit: publica por `IMessagePublisher`, e os consumers (finos) ficam em
   `Infrastructure/Messaging`.
4. **Retry só para falha transitória**: 3 tentativas (1s, 5s, 15s, configurável) e depois a fila `<fila>_error`. Falha
   permanente (`PermanentMessageException`: mensagem malformada, reserva inexistente, liberar o já consumido) é ignorada
   pelo retry e vai **direto** para a `_error`.
5. Conflitos de concorrência (`xmin`, falha de serialização `40001`, deadlock `40P01`, unicidade `23505`) viram
   `ConcurrencyConflictException` no `EfUnitOfWork` e são tratados como transitórios: o retry reexecuta e o handler já
   enxerga o estado novo.

**Why**: o Outbox no EF Core é o que torna "gravar e responder" atômico sem coordenador distribuído, e a 8.5.11 é a última
linha com licença aberta e suporte a .NET 10.

## Consequences

### Positive
- Spike resolvido: MassTransit 8.5.11 + EF Core 10.0.9 + Npgsql 10.0.3 funciona com Outbox/Inbox, provado em teste de
  integração com RabbitMQ e Postgres reais (reserva, repetição, disputa pela última unidade, DLQ).
- A DLQ passa a ser um sinal útil: só tem mensagem que de fato precisa de gente.

### Negative
- A 8.x deixa de receber evolução quando a 9.x for a linha principal; é preciso reavaliar a licença antes de atualizar.
- Sob muita disputa pelo mesmo item, a transação Serializable causa mais retentativas.

### Mitigations
- Volume de uma oficina é baixo; o retry resolve a disputa e o teste "duas OS disputando a última unidade" garante o resultado.
- `RabbitMq__RetryIntervalsMilliseconds` ajusta a política sem recompilar.

## Alternatives Considered

1. **MassTransit 9.x** — licença comercial.
2. **Outbox próprio** (tabela + `BackgroundService`) — mais código para manter e testar; só valeria se a 8.x não fechasse com EF Core 10.
3. **Publicar direto no RabbitMQ depois do commit** — janela em que o estoque é gravado e a resposta se perde.

## Related

- ADR-0002 — reserva por OS, idempotente e com concorrência otimista
- `docs/fluxo-saga.md` (gearflow-budget-service) — fluxo da Saga e política de retry/DLQ
