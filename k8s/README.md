# Inventory no Kubernetes

Mesmo padrão do `gearflow-operation-service`: a **base** (`k8s/base`) não cria banco, broker nem credenciais. Quem prepara o ambiente é o
`scripts/bootstrap-cluster.sh` (chamado pelo `delivery.yml`, ou à mão): namespace, PostgreSQL de `local-postgres*/` e o Secret da aplicação.
O Postgres é uma instância de demonstração fora da base, como o `local-mongo` do Billing; para um banco que importa, use um PostgreSQL
gerenciado e dispense o `local-postgres*` (o bootstrap precisa então ser ajustado para não subi-lo).

```
k8s/
├── base/                  Deployment (2 réplicas), Service :8080, ConfigMap
├── overlays/kong/         base + Ingress /api/inventory (só se o cluster tiver o Kong)
├── local-postgres/        StorageClass gearflow-inventory-gp3 + StatefulSet postgres:17 (cluster convencional, driver ebs.csi.aws.com)
└── local-postgres-auto/   o mesmo para EKS Auto Mode (driver ebs.csi.eks.amazonaws.com, nós Auto Mode)
scripts/bootstrap-cluster.sh   namespace + label + Postgres + Secret gearflow-inventory-secret (idempotente)
```

Por que a StorageClass é própria: o EKS Auto Mode **não cria StorageClass padrão**, então um PVC sem `storageClassName` ficaria `Pending`. A
classe é do Inventory (`gearflow-inventory-gp3[-auto]`) para não depender da do Billing (`gearflow-gp3`) nem da do broker. O `reclaimPolicy` é
`Delete`: apagar o PVC apaga o volume EBS, sem disco órfão gastando crédito do laboratório.

> **Ordem importante.** Estes manifestos assumem o broker **sem TLS** (AMQP 5672) do `gearflow-infra-broker`, no EKS **`gearflow`** (o broker
> migrou do `gearflow-fase4` para este cluster em 10/11, PR #5 dele). Só faça o deploy depois que o **GearFlow CD** do broker rodar na `main`
> dele; o `delivery.yml` confere (o Service `rabbitmq` precisa expor a 5672 e o Secret `rabbitmq-inventory-credentials` precisa existir). Se
> precisar apontar para um broker com TLS, o código ainda suporta: `RabbitMq__Port=5671`, `RabbitMq__Tls=true` e
> `RabbitMq__CaCertificatePath` (veja o comentário do `base/configmap.yaml`).

## Pré-requisitos

| O quê | Observação |
|---|---|
| Namespace `gearflow` e a label `gearflow.io/messaging-access=true` | **O bootstrap cria/ajusta** (a NetworkPolicy do broker exige a label no namespace **e** `gearflow.io/rabbitmq-client: "true"` no Pod, que já está no Deployment). Usa `kubectl label`, sem `apply`, para não disputar o namespace com o Billing, que também o aplica. A base não o cria. |
| PostgreSQL 17 **exclusivo** | Nenhum outro serviço acessa este banco. **O bootstrap sobe** o `local-postgres` (ou `-auto`, escolhido pelo `delivery.yml` lendo `computeConfig.enabled`). A senha é gerada **uma vez** e preservada no Secret `gearflow-inventory-postgres`: o `initdb` a grava no volume, então trocar o Secret depois não troca a senha do banco. |
| RabbitMQ do `gearflow-infra-broker` | Pré-requisito (não é do Inventory): EKS `gearflow`, `rabbitmq.messaging.svc.cluster.local:5672`, vhost `gearflow`, **AMQP sem TLS** (decisão do grupo em 08/11/2026). Usuário/senha por serviço, ACL e NetworkPolicy continuam valendo. O `base/configmap.yaml` já aponta para ele. |
| Secret `gearflow-inventory-secret` | **O bootstrap grava.** Chaves: `ConnectionStrings__Postgres`, `Jwt__Secret`, `RabbitMq__Username` e `RabbitMq__Password` (copiados do Secret `rabbitmq-inventory-credentials`, namespace `messaging`: **só** o usuário/senha do Inventory, nunca a conta administrativa nem as senhas dos outros serviços). O `Jwt__Secret` vem do secret `JWT_SECRET` do repositório, ou fica o que já está no cluster. Nunca no repositório. |
| Secret `newrelic-apm-secret` | Era do cluster da Fase 3; **verifique se existe no `gearflow`**. Opcional: sem ele o agente só não reporta **e o export OTLP é rejeitado pelo New Relic**, sem derrubar a API. |
| SMTP (Service `mailpit`) | O ConfigMap aponta para `mailpit:1025`, o Mailpit que o **perfil Academy do Billing** sobe no namespace `gearflow` (`gearflow-budget-service/k8s/academy/infrastructure.yaml`; SMTP sem TLS nem autenticação). O Inventory **não cria** um Mailpit próprio, para não disputar o mesmo nome com o do Billing. Então os e-mails de estoque só funcionam com o Billing já deployado; o `delivery.yml` avisa (sem falhar) se o Service não existir. Sem SMTP os avisos (baixo/insuficiente) esgotam o retry e vão para `*_error`, e contam em `integration.errors`; a Saga **não** é afetada (visto no teste de 2026-11-09). O `smtp-service` anterior era do cluster da Fase 3. |

A imagem `ghcr.io/gearflow-fiap/gearflow-inventory-service` é **pública**: não precisa de `imagePullSecret`.

## Subir à mão (bash / Git Bash)

Com o `kubectl` já apontando para o cluster `gearflow` (`aws eks update-kubeconfig --name gearflow --region us-east-1`) e a partir da raiz do repo:

```bash
# 0-2) Namespace + label, Postgres (senha gerada e preservada) e Secret da aplicação. Nenhum segredo é impresso.
#      POSTGRES_DIR: k8s/local-postgres-auto se o cluster é EKS Auto Mode (aws eks describe-cluster ... computeConfig.enabled), senão k8s/local-postgres.
#      JWT_SECRET: o segredo HMAC do grupo (>= 32 caracteres); se omitido, mantém o Jwt__Secret do Secret que já existe.
POSTGRES_DIR=k8s/local-postgres-auto JWT_SECRET='<segredo HMAC do grupo, >= 32 caracteres>' bash scripts/bootstrap-cluster.sh

# 3) Aplicação (use o digest/tag publicados pela ação gearflow-inventory-service.docker-publish)
kubectl kustomize k8s/base \
  | sed "s#image: gearflow-inventory-service:unpublished#image: ghcr.io/gearflow-fiap/gearflow-inventory-service:sha-<commit>#" \
  | kubectl apply -f -
kubectl -n gearflow rollout status deployment/gearflow-inventory
```

## Ver os e-mails de estoque no cluster

Os avisos "Estoque baixo: …" (`LowStockAlert`) e "Estoque insuficiente para a OS …" (`StockInsufficient`) vão para `Stock__AlertEmail`
(`estoque@gearflow.com`) e ficam retidos no Mailpit, que não entrega nada para fora:

```bash
kubectl -n gearflow port-forward service/mailpit 8025:8025     # depois, http://localhost:8025
kubectl -n gearflow logs deployment/gearflow-inventory | grep -i -E "smtp|email|e-mail"   # se o e-mail não chegar
```

Se a caixa ficar vazia: confirme que o Service existe (`kubectl -n gearflow get service mailpit`) e olhe, no broker, as filas
`inventory-low-stock-alert-email_error` e `inventory-stock-insufficient-email_error` (o envio esgotou o retry). O Mailpit do Billing não tem
volume, então um restart do Pod dele apaga as mensagens.

Para o vídeo, o catálogo de demonstração (peças P1 a P6, IDs fixos) sai com `Seed__EnableDevData: "true"` no ConfigMap.
Preços para o Operation: `GET http://gearflow-inventory.gearflow.svc.cluster.local:8080/api/inventory/parts/{id}` (chamada interna).

## Permissões no broker (resolvido e validado em 2026-11-09)

O broker novo dá ao usuário `gearflow_inventory` `configure`/`write`/`read` sobre `inventory[-.]…` (+ `_error`/`_skipped`), `GearFlow.Contracts:<Tipo>`, as
exchanges `MassTransit:Fault…` e os nomes temporários de bus do MassTransit, e pré-cria a topologia (`rabbitmq/definitions.json`). Isso cobre
tudo o que o Inventory declara, publica e consome (`QueueNames` e `InventoryEventNames`). Foi testado com o Inventory real (imagem `develop`)
contra um RabbitMQ 4.2.9 com o `rabbitmq.conf`, o `definitions.json` e as ACLs exatas do repositório do broker:

- o serviço fica `Healthy` sem `ACCESS_REFUSED` nem `PRECONDITION_FAILED`;
- o OS (usuário `gearflow_os`, publicando cru na exchange padrão, como o código dele faz) → Inventory → fila `gearflow.os-service`: reserva,
  baixa, estoque insuficiente, `ReleaseStock` sem reserva e compensação, todos recebem a resposta esperada;
- `inventory-reserve-stock_error`, `_skipped` e a exchange `MassTransit:Fault--…` são criados sob a ACL;
- as ACLs negativas funcionam (`403`): o OS não lê as filas de comando do Inventory e o Inventory não lê a fila do OS.

**Não foi testado:** a NetworkPolicy (as duas labels), que só existe no EKS, e o broker deployado no cluster.

## Deploy automático (`.github/workflows/delivery.yml`, ação `gearflow-inventory-service.delivery`)

Desligado até existir a variável `DEPLOY_ENABLED=true`. Roda depois da ação `gearflow-inventory-service.ci` na `main` (e por disparo manual, também
só na `main`, exigindo o CI verde para o mesmo commit). Na ordem: valida região/cluster/conta, espera a imagem `sha-<commit>` no GHCR e fixa o
deploy no digest, autentica na sessão Academy, lê a versão e o modo (Auto Mode ou não) do cluster, confere o acesso e o broker, roda o
`scripts/bootstrap-cluster.sh` (namespace, label, Postgres, Secret), valida no servidor (`--dry-run=server`), aplica, espera o rollout e testa
`/health/ready`. Se o Secret mudou e o Deployment já existia, reinicia os Pods (Pod só lê Secret na subida). O `workflow_run` **só vale a partir
da `main`** (branch padrão do repositório), então o workflow precisa estar nela.

**Como ele alcança o cluster.** O AWS Academy não tem OIDC nem permite roles novas, então o workflow usa as credenciais temporárias da sessão
do laboratório, como o CD do infra-broker e o Billing. Ele **não altera o endpoint do EKS** (nada de CIDR): o do `gearflow` precisa ser
alcançável pelo runner. Se não for, o passo "Confere o acesso ao cluster" falha com essa explicação e mostra `publicAccessCidrs`. (Antes o
workflow abria um /32 com o `cluster_access.py` do broker; o script recusa endpoint `/0` e o CD novo do broker já não o usa.)

| Onde | Nome | Observação |
|---|---|---|
| Secrets (organização, **liberados a este repositório**, ou do repositório) | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN` | Da mesma sessão Academy e da conta do cluster `gearflow`; expiram juntos, renove os três antes de cada teste. |
| Secret do repositório | `JWT_SECRET` | O segredo HMAC do grupo (>= 32 caracteres). Sem ele o bootstrap só funciona se `gearflow-inventory-secret` já existir no cluster. |
| Variáveis | `AWS_REGION` (`us-east-1`), `EKS_CLUSTER_NAME` (`gearflow`), `AWS_ACCOUNT_ID` (**opcional**: 12 dígitos, a conta do `gearflow`; Variable ou Secret, a variável vence) | `AWS_REGION` e `EKS_CLUSTER_NAME` fazem o workflow falhar cedo se estiverem erradas. O `AWS_ACCOUNT_ID`, **se informado**, faz o login recusar credenciais de outra conta (o nome `gearflow` não é único entre contas Academy); **vazio**, não há essa conferência e o workflow só registra um aviso. Variável **do repositório** vence a da organização (que pode ainda dizer `gearflow-fase4`). |
| Variável do repositório | `DEPLOY_ENABLED` (`true`); opcional `KUSTOMIZE_DIR` (padrão `k8s/base`; `k8s/overlays/kong` só se houver Kong) | |
| Environment `inventory-homologation` | (aprovação opcional) | Settings → Environments |

No cluster, o preflight exige só o broker (Service `rabbitmq` na porta 5672 e Secret `rabbitmq-inventory-credentials`, ambos do namespace
`messaging`); o resto o bootstrap cria. Não há role a mapear: segundo o `docs/cd.md` do infra-broker, o acesso Kubernetes é o da identidade
Academy que criou o cluster (não verificado por mim no cluster).

**Limites.** O bootstrap é idempotente, mas não migra dados nem troca a senha de um Postgres que já existe (ele recusa e explica se encontrar o
PVC sem o Secret). A imagem do Postgres sai do Docker Hub (os nós precisam alcançá-lo, como já precisam para o RabbitMQ). O Postgres é uma
réplica única, sem backup. **Este workflow ainda não rodou no GitHub.**

## SonarCloud (job `sonar` da ação `gearflow-inventory-service.ci`)

Desligado até existir a variável `SONAR_PROJECT_KEY`. Também precisa de `SONAR_ORG` (variável) e `SONAR_TOKEN` (secret). O Billing usa a
organização `gearflow-fiap`; falta alguém com permissão importar este repositório lá.

## Observabilidade (reaproveita a Fase 3)

O ConfigMap traz `Otlp__Endpoint=https://otlp.nr-data.net:4317` e o Deployment monta `OTEL_EXPORTER_OTLP_HEADERS=api-key=<licença>` a partir
do Secret `newrelic-apm-secret`: o mesmo caminho do monólito (`gearflow-infra-k8s/terraform/app.tf`). A API exporta traces e métricas por
OTLP e inclui os spans do MassTransit, então um trace que começa numa requisição REST ou numa mensagem do RabbitMQ deve aparecer inteiro no
New Relic (o serviço que **envia** a mensagem também precisa propagar o contexto). **Ainda não foi visto no New Relic**: depende de rodar no
cluster, que hoje não tem o Secret da licença nem o `nri-bundle`. O agente APM do Dockerfile continua ativo. Para desligar o OTLP, remova
`Otlp__Endpoint` do ConfigMap. Métricas de negócio, alertas, dashboard e o pré-requisito do `nri-bundle` no cluster:
[docs/OBSERVABILITY.md](../docs/OBSERVABILITY.md).

## O que foi verificado

- `kubectl kustomize` + kubeconform `-strict` nas saídas de `k8s/base`, `overlays/kong` e dos dois perfis do Postgres (CI: job `manifests`).
  Os dois perfis novos do Postgres foram só **renderizados** localmente (Docker parado: o kubeconform deles roda no CI).
- `scripts/bootstrap-cluster.sh` contra um `kubectl` **falso** (estado em disco), 37 verificações em 12 cenários: primeira execução, segunda
  execução idempotente (senha do Postgres preservada, `app_secret_changed=false`), JWT novo muda o Secret, sem `JWT_SECRET` mantém o do
  cluster, falha antes de criar qualquer coisa (JWT ausente/curto/com espaço, Secret do broker ausente ou de outro usuário), recusa de PVC sem
  Secret e `POSTGRES_DIR` inválido; nenhum segredo em argumento de `kubectl` nem no log (e `::add-mask::` dos três no Actions). **Não foi
  rodado contra um cluster de verdade**, então a semântica real do `kubectl` (ex.: `create secret --from-env-file`) segue por confirmar.
  O teste não está no repositório (o `kubectl` falso mora fora dele).
- Smoke test no Docker (2026-11-04) com a imagem real do GHCR reproduzindo o `securityContext` (uid 1654, raiz somente leitura, sem
  capabilities), Postgres 17 (uid 999) e RabbitMQ com vhost `gearflow`: sobe, `/health/ready` Healthy, API responde com JWT, a topologia aparece
  no vhost e o agente do New Relic escreve em `/tmp/newrelic`.
- Teste contra a topologia e as ACLs reais do infra-broker (2026-11-09): seção "Permissões no broker" acima.
- Não foi aplicado em nenhum cluster.

## Migration concorrente no primeiro deploy (resolvido)

A migration roda no startup (`Program.cs`). Antes, com 2 réplicas subindo ao mesmo tempo num banco **vazio**, uma caía com
`42P07: relation "InboxState" already exists` (reproduzido no smoke test), porque o `MigrateAsync` do provider Npgsql não serializa as
réplicas. Agora o `InventoryDatabaseMigrator` toma um *advisory lock* do PostgreSQL (`pg_advisory_lock`, de sessão) antes de migrar: a
réplica que chega depois espera, encontra tudo aplicado e segue. Não é preciso reduzir as réplicas no primeiro apply.

- Se a outra réplica demorar mais de 90 s, o Pod falha com `TimeoutException` ("Não consegui o lock das migrations") e o Kubernetes o reinicia.
- Se o Pod que migra for morto, o PostgreSQL derruba a sessão e libera o lock sozinho.
- O lock exige que o usuário do banco consiga chamar `pg_advisory_lock` (qualquer usuário comum consegue). O banco precisa aceitar uma
  conexão a mais por Pod durante a migration.
- Testes de integração (Postgres real): quatro réplicas migrando um banco vazio ao mesmo tempo, o timeout com lock preso por outra sessão
  e a liberação do lock ao terminar. Sem o lock, o primeiro deles reproduz o `42P07`.
