# Dashboard "GearFlow — Fase 4" (New Relic, Terraform)

Dashboard único para os **quatro** serviços (OS, Billing, Operation, Inventory), pensado no que o enunciado da Fase 4 manda mostrar no
vídeo. É independente do `infra/newrelic` (alertas e dashboard só do Inventory) e do Terraform da Fase 3.

## Por que não reaproveitar o Terraform da Fase 3

`gearflow-infra-k8s/terraform` é uma raiz única que cria **EKS, VPC, Kong e o app** e, junto, o New Relic. Não dá para aplicá-lo como está:

- o estado fica no workspace `gearflow-infra-k8s` (Terraform Cloud, org `gearflowfiapmurilo`) e aponta para o cluster da Fase 3;
- `monitoring.tf` (nri-bundle) depende de `aws_eks_node_group.default`, que é desse cluster; o da Fase 4 é o EKS `gearflow`;
- o dashboard e os alertas filtram por `service.name = 'GearFlow.Api'` (o monólito): nenhum enxerga OS, Billing, Operation nem Inventory. O único
  widget que já mostra a Fase 4 é "Erros/falhas nas integrações" (`integration.errors` sem filtro de serviço), só com o Inventory.

O que **se reaproveita** é a abordagem: o mesmo provider (`newrelic ~> 3.0`, região US), a mesma conta, `permissions = "public_read_only"`,
as mesmas métricas (`http.server.request.duration`, `integration.errors`) e o mesmo `nri-bundle` para CPU e memória.

## Páginas

| Página | O que mostra | Item do vídeo |
|---|---|---|
| **Fluxo distribuído da OS** | Serviços enviando traces, requisições e latência p95 por serviço, spans com erro e a tabela de **traces que atravessam mais de um serviço** (`trace.id` em vários `service.name`) | Fluxo completo de uma OS; rastreamento dos fluxos distribuídos |
| **Saga e compensação** | Reservas por desfecho, liberações (compensação) e baixas (Inventory); eventos de negócio (Billing); comandos por ação e resultado (Operation); `integration.errors`; DLQ e outbox pendente | Execução da Saga e tratamento de falhas |
| **Plataforma e deploy** | CPU e memória por pod e a **imagem (`sha-<commit>`) que cada pod roda** | Deploy automatizado; uso do monitoramento da Fase 3 |

## Aplicar

Pré-requisitos: o ID da conta e uma **User API Key** (`NRAK-…`, a do NerdGraph; não é a license key). A chave **nunca** vai para arquivo.

```powershell
cd infra/newrelic-fase4
Copy-Item terraform.tfvars.example terraform.tfvars        # só na PRIMEIRA vez (sobrescreve o arquivo); preencha newrelic_account_id (ignorado pelo Git)
$env:TF_VAR_newrelic_api_key = '<User API Key NRAK-...>'
terraform init
terraform plan
terraform apply
terraform output dashboard_url
```

Assim o estado fica local (sem backend remoto), como o `infra/newrelic`. Para aplicar com as credenciais do grupo, veja a próxima seção.
Dashboard e alertas criados pelo Terraform não devem ser editados na UI (o próximo `apply` desfaz); para mudar, edite o `dashboard.tf`.

## Aplicar pelo Terraform Cloud (conta do grupo)

O Terraform da Fase 3 roda na organização `gearflowfiapmurilo` do Terraform Cloud, com a conta New Relic do grupo. Uma User API Key de quem **não**
pode criar dashboards nessa conta falha no `apply` (veja "Se o apply falhar"). Por isso este módulo pode rodar num **workspace próprio** da mesma
organização, que guarda o ID da conta e a chave do grupo. Não use o workspace `gearflow-infra-k8s`: ele guarda o estado do EKS/VPC/Kong da Fase 3 e
só entende o código dela.

**Quem administra a organização faz uma vez:**

1. Criar o workspace `gearflow-fase4-newrelic` (fluxo *CLI-driven*, execução *Remote*). Se usar outro nome, ajuste o `backend.tf.example`.
2. Nas variáveis do workspace (categoria *Terraform variable*): `newrelic_account_id` (ID da conta do grupo) e `newrelic_api_key` (User API Key
   `NRAK-…` de um usuário que pode criar dashboards, marcada como **Sensitive**). Opcional: `dashboard_permissions`. As variáveis do workspace da
   Fase 3 **não** são herdadas e valores sensíveis não podem ser lidos de volta: preencha de novo, ou anexe um *variable set* se já existir um.
3. Convidar para a organização e dar ao time de quem vai aplicar a permissão de rodar *plans* e *applies* nesse workspace.

**Quem aplica:**

```powershell
cd infra/newrelic-fase4
Copy-Item backend.tf.example backend.tf     # liga o módulo ao workspace
terraform login                             # token pessoal do Terraform Cloud
terraform init
terraform apply                             # roda no Terraform Cloud, com as variáveis do workspace
terraform output dashboard_url
```

O `apply` é remoto e usa as variáveis do workspace: não precisa de `TF_VAR_newrelic_api_key` na sua máquina, e o ID e a chave não devem vir de um
`terraform.tfvars` local. Se já houver um `terraform.tfstate` local **com o dashboard**, o `init` pergunta se migra o estado (responda `yes`); um estado
local vazio (de um `apply` que falhou) pode ser ignorado. Sem o `backend.tf`, o módulo segue com estado local.

## Se o apply falhar

| Mensagem | Causa | O que fazer |
|---|---|---|
| `Invalid value for variable … newrelic_account_id` | O `terraform.tfvars` ficou com o ID de exemplo `0000000` (o `Copy-Item` sobrescreve o arquivo) | Preencha o ID real; não rode `Copy-Item` de novo |
| `Access restricted` | Mesmo sintoma do ID errado antes de a validação existir: a chave não tem acesso à conta informada | Confira o ID. Liste as contas que a chave enxerga com a consulta NerdGraph `{ actor { accounts { id name } } }` |
| `User is not allowed to perform operation on dashboard entity … does not have the necessary capabilities` | O ID está certo, mas o usuário dono da chave não pode criar dashboards na conta (tipo *Basic* ou papel só de leitura) | Aplicar pelo workspace do grupo (acima), ou pedir a quem administra a conta o tipo/papel e gerar uma chave nova |

## O que precisa estar de pé para os widgets encherem

O dashboard só **consulta**. Cada serviço precisa exportar telemetria ao New Relic; o que falta em cada um está em
[`docs/OBSERVABILITY.md`](../../docs/OBSERVABILITY.md). Resumo (visto no código, não no New Relic):

| Serviço | `service.name` | Exporta no código | Fica vazio enquanto… |
|---|---|---|---|
| Inventory | `GearFlow.Inventory.Api` | traces + métricas `inventory.*`, `integration.errors` | o Secret `newrelic-apm-secret` não existir no cluster |
| OS | `gearflow-os-service` | traces + métricas do ASP.NET Core | não houver `OTEL_EXPORTER_OTLP_ENDPOINT`/header (o k8s do OS já os traz) |
| Billing | `gearflow-budget-service` | traces + `billing.*`, só com `Telemetry:Enabled=true` | o perfil Academy mantiver a telemetria desligada ou o endpoint apontar para um collector que não existe |
| Operation | `GearFlow.Operation` | traces + `gearflow.operation.*`, só com `Telemetry:ExportEnabled` | não houver endpoint OTLP no ConfigMap |

- A tabela de traces entre serviços só mostra mais de um serviço se o **contexto de trace for propagado pelas mensagens**. O Billing e o
  Operation propagam `traceparent` e o Inventory usa o span do MassTransit; o OS, até onde li, **não** grava nem lê o `traceparent` no outbox/consumer
  (`OutboxDispatcher.cs`). Sem isso, a OS aparece em traces separados por serviço.
- A página **Plataforma e deploy** depende do `nri-bundle` no cluster `gearflow` (não confirmado). Sem ele, CPU, memória e imagem ficam vazios.
- Os NRQLs foram escritos a partir do código e **não foram executados** numa conta real: confirme no Query builder se algum widget vier vazio com
  dados chegando (os suspeitos são `span.kind`, `otel.status_code` e os nomes dos atributos).
