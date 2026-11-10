# ADR-0003: Baixa de estoque comandada pela Saga (`ConsumeStock`)

**Status**: Accepted (2026-11-10). Proposta em 2026-10-21; confirmada pelo código do `os-service` (ver "Confirmação").
**Date**: 2026-10-21
**Decided by**: aceita pela implementação do orquestrador no `os-service` (commit `65da498`, 2026-11-05). Não houve ata do grupo.

## Context

- No monólito, o consumo do estoque reservado acontecia na finalização da OS (`FinalizeServiceOrderHandler`),
  dentro da mesma transação: é a regra "reserva na aprovação, **consumo na finalização**".
- Em `docs/fluxo-saga.md` (gearflow-budget-service) o estoque pertencia ao Execution Service, e a "baixa
  definitiva" acontecia lá, a cada serviço concluído. Com o Inventory como serviço próprio, essa baixa
  precisa de um dono e de um gatilho.
- A Saga é orquestrada: só o orquestrador (no `os-service`) decide o próximo passo.
- Depois que a execução começa é o "ponto sem volta": só retry, sem compensação.

## Decision

O orquestrador envia o comando **`ConsumeStock`** ao Inventory ao receber `ExecutionFinished`. O Inventory baixa
o reservado usando as linhas da própria reserva (a mensagem não carrega itens, para não divergir da reserva),
publica `LowStockAlert` para cada item que ficou no/abaixo do mínimo e responde `StockConsumed`. A OS só vai
para `Finalized` depois de `StockConsumed`.

`ConsumeStock` é idempotente. Falha (reserva inexistente, liberada, item sumiu) sobe para retry e DLQ, sem
compensação.

**Why**: preserva a regra de negócio original e a regra "só o orquestrador decide", e mantém o ponto sem volta
explícito no fluxo.

## Consequences

### Positive
- Um passo a mais, visível e idempotente, na tabela de estado da Saga.
- O alerta de estoque baixo continua disparando no consumo, como no monólito.

### Negative
- Exige um passo novo (`ConsumeStock`/`StockConsumed`) em `fluxo-saga.md`, no orquestrador e nos contratos.

### Mitigations
- Atualizar `fluxo-saga.md`, o README do Billing e a tabela de contratos junto com este ADR.

## Confirmação (2026-11-10)

Lido em `gearflow-os-service`, branch `dev` (`SagaOrchestrator` e `ServiceOrder`):

- Ao receber `ExecutionFinished`, o OS envia `ConsumeStock` à fila `inventory-consume-stock`, só se a OS tem reserva (`OnExecutionFinished`).
- Ao receber `StockConsumed`, o OS marca a baixa (`ApplyStockConsumed`).
- `Deliver` recusa a entrega com `Os.StockPending` enquanto houver reserva sem baixa. Na implementação do OS, a "finalização" do ADR é a
  entrega da OS (`Completed`).
- Ao receber `StockReplenished`, o OS reenvia `ReserveStock` às OS em `AwaitingParts` (`OnStockReplenished`), como o Inventory assume.

**Divergências que ficam abertas** (são do OS e da documentação, não do Inventory):

- A ordem do OS é reservar **antes** de cobrar (`ReserveStock` → `RequestPayment`). O `fluxo-saga.md` do Billing e a RFC-001 do Operation
  ainda descrevem a ordem anterior.
- O OS não tem timeout: se o Inventory não responder (falha permanente vai para a DLQ, sem resposta), a OS fica esperando.

## Alternatives Considered

1. **O Inventory consome ao receber `ExecutionFinished` diretamente** — quebra "só o orquestrador decide o
   próximo passo" e deixa o fluxo espalhado (coreografia parcial).
2. **O Operation baixa o estoque por REST a cada serviço concluído** — acopla Operation e Inventory e perde a
   atomicidade com a reserva.

## Related

- ADR-0002 — reserva por OS
- `gearflow-budget-service/docs/fluxo-saga.md` — passo 7
