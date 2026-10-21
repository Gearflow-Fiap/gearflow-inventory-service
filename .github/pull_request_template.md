## O que muda

<!-- Resumo em 1 ou 2 frases. Qual passo da Saga, endpoint ou regra de estoque é afetado? -->

## Checklist

- [ ] `dotnet test GearFlow.Inventory.slnx -c Release` passa e a cobertura continua ≥ 80%
- [ ] Mudou contrato de mensagem (`GearFlow.Contracts`)? O `os-service` foi avisado e os nomes/campos batem
- [ ] Mudou o modelo do banco? Há migration nova (`dotnet ef migrations add ...`)
- [ ] Handler novo ou alterado: publica as mensagens **antes** do `SaveChangesAsync` (Outbox) e é idempotente
- [ ] Endpoint novo tem `.WithSummary(...)`, `.WithDescription(...)` e `.RequireAuthorization()`
- [ ] Mudou regra ou decisão de arquitetura? README e ADR atualizados no mesmo PR
