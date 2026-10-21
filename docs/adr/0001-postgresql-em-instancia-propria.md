# ADR-0001: PostgreSQL em instância própria

**Status**: Accepted
**Date**: 2026-10-21
**Decided by**: time GearFlow

## Context

- O PDF da Fase 4 exige que cada microsserviço tenha "repositório, infraestrutura e banco de dados" próprios,
  com pelo menos um banco relacional e pelo menos um não relacional no conjunto, e que **nenhum serviço acesse
  diretamente o banco de outro**.
- Escolhas já feitas pelo time: OS Service em PostgreSQL, Billing em MongoDB.
- O estoque é dado transacional: reservar vários itens tudo-ou-nada, mexer em contadores (`quantity`,
  `reserved_quantity`) e garantir que duas OS não reservem a última unidade.
- O monólito (Fase 3) usa SQL Server, no RDS provisionado em `gearflow-infra-database`.

## Decision

O Inventory Service usa **PostgreSQL**, em **instância própria** (StatefulSet + PVC + Service `ClusterIP` no
`k8s/postgres/` deste repo), com credenciais que só o pod do Inventory recebe.

"Mesmo motor do OS Service" não significa o mesmo banco: não há database, schema ou tabela compartilhados entre
os dois. O requisito de SQL + NoSQL é atendido pelo conjunto (OS e Inventory em Postgres, Billing em Mongo).

**Why**: o estoque é relacional por natureza, e o Postgres dá transação local e concorrência otimista (`xmin`)
sem precisar de replica set, como o Mongo exigiria. Reaproveitar o motor do OS Service reduz o que a equipe
precisa operar.

## Consequences

### Positive
- Transação local atômica na reserva (estoque + reserva + Outbox no mesmo commit).
- Concorrência resolvida pelo banco: o token `xmin` faz a segunda reserva concorrente falhar e ser repetida.
- Sem custo de RDS: o banco sobe no cluster.

### Negative
- Troca de provider em relação ao monólito: a migration `InitialInventory` (T-SQL) não é reaproveitada e o
  `SqlInventoryReservation` (`OUTPUT INSERTED`) não é portado.
- Mais um StatefulSet no cluster, que já roda 4 bancos e o RabbitMQ.

### Mitigations
- As colunas já eram `snake_case`, então o esquema novo é equivalente ao antigo.
- `requests`/`limits` pequenos no Postgres e conferência da capacidade dos nós antes do deploy.
- NetworkPolicy na porta 5432 só para o pod do Inventory (quando o CNI aplicar).

## Alternatives Considered

1. **SQL Server** (reaproveitaria código e o RDS da Fase 3) — descartado: o OS Service já usa Postgres, e
   manter um terceiro motor só para o Inventory não traz ganho.
2. **MongoDB** — descartado: reservar vários itens tudo-ou-nada exigiria transações multi-documento.
3. **Mesmo servidor Postgres do OS Service com database separado** — descartado: o PDF pede infraestrutura
   própria, e instância própria elimina qualquer ambiguidade na avaliação.

## Related

- ADR-0002 — reserva por OS
- `gearflow-app/docs/rfcs/rfc-001-escolha-do-banco-de-dados.md` — escolha do banco na Fase 3
