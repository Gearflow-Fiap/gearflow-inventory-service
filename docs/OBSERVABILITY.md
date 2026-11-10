# Observabilidade e New Relic

O PDF da Fase 4 pede para **reaproveitar o monitoramento da Fase 3**. Este serviço usa o mesmo New Relic, nas mesmas três camadas
do monólito (`gearflow-app` + `gearflow-infra-k8s`):

| Camada | Fase 3 (monólito) | Inventory | Onde está |
|---|---|---|---|
| **1. Agente APM .NET** | Dockerfile instala `newrelic-dotnet-agent`; variáveis `NEW_RELIC_*` | Igual | [`Dockerfile`](../src/GearFlow.Inventory.Api/Dockerfile) |
| **2. OpenTelemetry por OTLP** | Traces e métricas para `otlp.nr-data.net:4317` com `api-key` = license key; métricas de negócio no Meter `GearFlow.Business` | Igual, mais os spans e métricas do MassTransit | [`ObservabilityExtensions.cs`](../src/GearFlow.Inventory.Api/Configurations/ObservabilityExtensions.cs), [`BusinessMetrics.cs`](../src/GearFlow.Inventory.Application/Behaviors/BusinessMetrics.cs), [`k8s/base`](../k8s/base) |
| **3. Alertas, dashboard e cluster** | `newrelic-alerts.tf`, `newrelic-dashboard.tf`, `monitoring.tf` (nri-bundle) | Alertas e dashboard próprios em Terraform; o nri-bundle é do cluster | [`infra/newrelic`](../infra/newrelic) |

## Pilares

| Pilar | Como | Onde |
|---|---|---|
| **Traces** | OpenTelemetry: ASP.NET Core, HttpClient, spans por command (`GearFlow.Inventory`) e por mensagem do RabbitMQ (`MassTransit`) → OTLP | `AddObservability` |
| **Métricas** | OpenTelemetry → OTLP (New Relic) e `/metrics` (Prometheus). Instrumentação ASP.NET Core, runtime, MassTransit e **negócio** | `AddObservability`, `BusinessMetrics` |
| **Logs** | Serilog em **JSON** em produção, com `TraceId`/`SpanId` nas requisições HTTP (middleware); o agente encaminha os logs e os decora com `trace.id`/`span.id` (`NEW_RELIC_APPLICATION_LOGGING_*`), sem Fluent Bit | `AddSerilogLogging`, `ExceptionHandlingMiddleware` |
| **Health** | `/health/live`, `/health/ready` (banco; RabbitMQ fora = `Degraded`), `/health` | `HealthCheckExtensions` |

## Métricas de negócio

Meter **`GearFlow.Business`** (o mesmo nome do monólito: um dashboard único enxerga todos os serviços). Registradas por um
decorator no pipeline do Dispatcher (`BusinessMetricsDecorator`), então valem para a REST e para a Saga sem mudar os handlers.

| Instrumento | Tipo | Atributo | O que conta |
|---|---|---|---|
| `inventory.reservations` | contador | `outcome`: `reserved`, `already_reserved`, `insufficient`, `ignored_after_release` | Desfecho de cada `ReserveStock` |
| `inventory.releases` | contador | | `ReleaseStock` concluído (compensação) |
| `inventory.consumptions` | contador | | `ConsumeStock` concluído (baixa definitiva) |
| `inventory.replenishments` | contador | `item_type`: `Part`, `Consumable` | Reposição pela API (`PATCH .../stock`) |
| `inventory.low_stock_alerts` | contador | | Alerta de estoque baixo processado |
| `inventory.saga.step.duration` | histograma (ms) | `step`: `reserve`, `release`, `consume` | Duração do passo da Saga, inclusive quando falha |
| `integration.errors` | contador | `source`: `inventory.<Comando>` | Comando da Saga que falhou (vai para a DLQ) ou exceção inesperada |

Decisões que valem saber:
- **Faltar estoque não é erro.** Conta como `inventory.reservations{outcome=insufficient}` e não como `integration.errors`.
- **Erro de cliente da REST (400, 404, 409) não é `integration.errors`.** Só falha de comando da Saga e exceção inesperada.
- **Conflito de concorrência não conta**: é esperado quando duas OS disputam a última unidade e o consumer tenta de novo.
- `integration.errors` tem o **mesmo nome do monólito**, então o alerta e o widget da Fase 3 passam a enxergar o Inventory também.

Os nomes dos instrumentos são **contrato com os NRQLs** de `infra/newrelic`. Os testes `BusinessMetricsTests` travam os valores e
leem o Meter de verdade; se renomear um instrumento, atualize os dois lados.

## Ligar o New Relic

O agente e o OTLP só enviam dados se houver a license key. Sem ela, nada quebra: o agente só não reporta.

| Variável | Valor | Onde |
|---|---|---|
| `NEW_RELIC_LICENSE_KEY` | License key da conta (a mesma da Fase 3) | Secret `newrelic-apm-secret`, chave `NEW_RELIC_LICENSE_KEY` (já existe no cluster da Fase 3) |
| `OTEL_EXPORTER_OTLP_HEADERS` | `api-key=<license key>` | Deployment: monta a partir do Secret acima |
| `Otlp__Endpoint` | `https://otlp.nr-data.net:4317` | ConfigMap (`k8s/base`) |
| `CORECLR_*`, `NEW_RELIC_APP_NAME`, `NEW_RELIC_DISTRIBUTED_TRACING_ENABLED`, `NEW_RELIC_APPLICATION_LOGGING_*`, `NEW_RELIC_LOG_LEVEL` | Já na imagem | Dockerfile |
| `NEW_RELIC_LOG_DIRECTORY` | `/tmp/newrelic` | ConfigMap (o sistema de arquivos do contêiner é somente leitura) |

> **Não copie os `CORECLR_*` do ConfigMap da Fase 3.** Eles apontam para `/app/newrelic`, mas o agente é instalado em
> `/usr/local/newrelic-dotnet-agent` (o que o Dockerfile faz) e as variáveis do ConfigMap sobrescrevem as da imagem. Também não defina
> `DOTNET_EnableDiagnostics=0`: ele desliga o profiler do CLR e o agente deixa de funcionar.

**Rodando local** (sem Kubernetes), com os dados indo para o New Relic:

```powershell
$env:Otlp__Endpoint = 'https://otlp.nr-data.net:4317'
$env:OTEL_EXPORTER_OTLP_HEADERS = "api-key=$env:NEW_RELIC_LICENSE_KEY"   # a license key vem do seu ambiente, nunca do repositório
dotnet run --project src/GearFlow.Inventory.Api
```

As métricas aparecem como `Metric` com `service.name = 'GearFlow.Inventory.Api'`. Sem `Otlp__Endpoint`, os traces ficam no processo e
`/metrics` (Prometheus) continua funcionando.

## Alertas e dashboard (Terraform)

[`infra/newrelic`](../infra/newrelic) cria, com o provider `newrelic ~> 3.0` (o mesmo da Fase 3):
- **Alertas** (e-mail): latência p95 da API, CPU e memória do pod, falhas de processamento, **OS sem estoque** (`StockInsufficient`),
  **estoque baixo** e, se houver URL pública, **healthcheck** (monitor sintético a cada 5 min).
- **Dashboard** "GearFlow Inventory — Fase 4": página *Estoque e Saga* (reservas por desfecho, passos da Saga, duração, falhas) e
  página *Infraestrutura e performance* (latência p50/p95/p99, CPU/memória por pod, uptime).

```powershell
cd infra/newrelic
Copy-Item terraform.tfvars.example terraform.tfvars            # preencha newrelic_account_id e newrelic_alert_email
$env:TF_VAR_newrelic_api_key = '<User API Key NRAK-...>'        # NerdGraph; NÃO é a license key
terraform init
terraform plan
terraform apply
```

Foi validado com `terraform fmt -check` e `terraform validate`. **O `plan` e o `apply` ainda não foram executados**: dependem do ID
da conta e da User API Key do grupo.

## Pré-requisito do cluster: nri-bundle

Os alertas e widgets de CPU e memória usam `K8sContainerSample`, que vem do **nri-bundle** (Helm), instalado **uma vez por cluster** por quem o
administra, como no `monitoring.tf` da Fase 3. Pelos docs do `gearflow-infra-broker` (05/11), o cluster novo (`gearflow-fase4`) só tem o RabbitMQ e
o monitoramento segue pendente: **o nri-bundle ainda não deve estar instalado** (não verifiquei no cluster).
Referência (valores da Fase 3: `gearflow-infra-k8s/k8s/monitoring/helm-values.yaml`):

```bash
helm repo add newrelic https://helm-charts.newrelic.com && helm repo update
helm install nri-bundle newrelic/nri-bundle -n newrelic --create-namespace \
  --set global.licenseKey="$NEW_RELIC_LICENSE_KEY" --set global.cluster=gearflow-fase4 \
  --set global.lowDataMode=true --set newrelic-logging.enabled=false \
  -f helm-values.yaml
```

O `global.cluster` precisa ser igual à variável `cluster_name` de `infra/newrelic`.

## O que ainda depende de fora do repositório

- Credenciais do New Relic do grupo (ID da conta e User API Key) para o `terraform apply`.
- O `nri-bundle` no cluster `gearflow-fase4`.
- Um Secret `newrelic-apm-secret` no namespace do Inventory (o do cluster da Fase 3 não existe no cluster novo).
- Uma URL pública do `/health/ready`, se quiserem o monitor sintético (o Ingress do Kong só publica `/api/inventory`).
- Os outros serviços exportarem para o mesmo New Relic para o dashboard compartilhado enxergar a Saga inteira.
