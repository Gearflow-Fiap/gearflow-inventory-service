# Evidência de cobertura de testes

**Meta do desafio:** cobertura de linhas **≥ 80% por serviço**.

## Quem mede (e por que não é o computador de quem desenvolve)

O árbitro é o CI do GitHub: `.github/workflows/ci.yml` (ação `gearflow-inventory-service.ci`), em Linux, com o **coverlet** e a
configuração de [`coverlet.runsettings`](../../coverlet.runsettings). Ele aplica **dois gates de 80%** e reprova o build abaixo disso:

| Job | O que mede | Gate |
|---|---|---|
| `unit` — *Build + testes unitários + cobertura* | Só os testes unitários e de arquitetura | linhas ≥ 80% |
| `coverage` — *Cobertura consolidada (gate 80%)* | Unitários **+** integração (Postgres, RabbitMQ e Mailpit reais), unidos com o ReportGenerator | linhas ≥ 80% |

Fora da medição (`coverlet.runsettings`): migrations, `Program.cs`, o projeto de contratos e código gerado.

Na máquina de desenvolvimento (Windows) o coverlet é barrado pelo Smart App Control, então a medição local usa outra ferramenta
(`dotnet-coverage`) e **não bate** com a do CI. Por isso o número que vale é o do CI.

## Prova de que os gates passam

Run [`41918414705`](https://github.com/Gearflow-Fiap/gearflow-inventory-service/actions/runs/41918414705), commit `db3f41e`
(2026-11-04): os jobs `unit`, `integration`, `security` e `coverage` terminaram com sucesso, portanto os dois gates de 80% foram atendidos.
Conferido pela API pública do GitHub (`/actions/runs/41918414705/jobs`).

## Onde ver o número exato

1. **Anotações do run** (a partir do primeiro push depois de `2026-11-05`): os jobs `unit` e `coverage` publicam
   *Cobertura (unitários)* e *Cobertura consolidada (unit + integração)* com o percentual. Ficam no resumo do run e na API pública:
   `GET /repos/Gearflow-Fiap/gearflow-inventory-service/check-runs/<id do job>/annotations`.
2. **Artefato `coverage-merged`** (ZIP, em *Actions → run → Artifacts*): `Summary.txt`, `Cobertura.xml` e `SummaryGithub.md`, com o
   detalhe por classe. Os artefatos `coverage-unit` e `coverage-integration` trazem os relatórios de cada suíte. Retenção de 90 dias.
3. **Comentário fixo no PR** com o resumo (job `coverage`, só em pull request).

## Como reproduzir

```bash
# Linux/macOS/CI (coverlet)
dotnet test tests/GearFlow.Inventory.UnitTests -c Release --settings coverlet.runsettings \
  --collect:"XPlat Code Coverage" --results-directory ./coverage/unit
dotnet test tests/GearFlow.Inventory.IntegrationTests -c Release --settings coverlet.runsettings \
  --collect:"XPlat Code Coverage" --results-directory ./coverage/integration   # precisa do Docker
dotnet tool restore
dotnet tool run reportgenerator -reports:"./coverage/**/coverage.cobertura.xml" -targetdir:"./coverage-report" \
  -reporttypes:"Cobertura;TextSummary;MarkdownSummaryGithub"
cat ./coverage-report/Summary.txt
```
