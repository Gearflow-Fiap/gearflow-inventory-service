# Inventory no Kubernetes

Mesmo padrão do `gearflow-operation-service`: a base **não cria banco, broker nem credenciais**; eles são pré-requisitos do namespace
`gearflow`. O Postgres de `local-postgres/` é só um exemplo para desenvolvimento/demonstração, fora da base, como o `local-mongo` do Billing.

```
k8s/
├── base/              Deployment (2 réplicas), Service :8080, ConfigMap
├── overlays/kong/     base + Ingress /api/inventory (só se o cluster tiver o Kong; o gearflow-fase4 não tem)
└── local-postgres/    StatefulSet postgres:17 de exemplo (instância exclusiva do Inventory)
```

> **Ordem importante.** Estes manifestos assumem o broker **sem TLS** (AMQP 5672), que está na `homolog` do `gearflow-infra-broker`. O broker
> da `main` dele (até 05/11) é AMQPS 5671 com CA privada. Só faça o deploy do Inventory depois que a `homolog` entrar na `main` do broker
> **e o CD dele rodar**; o `delivery.yml` confere isso no preflight (o Service `rabbitmq` precisa expor a 5672). Se precisar apontar para um
> broker com TLS, o código ainda suporta: `RabbitMq__Port=5671`, `RabbitMq__Tls=true` e `RabbitMq__CaCertificatePath` (veja o comentário
> do `base/configmap.yaml`).

## Pré-requisitos

| O quê | Observação |
|---|---|
| Namespace `gearflow` | O Terraform/CD do `gearflow-infra-broker` só cria `messaging`. Quem administra o cluster cria o `gearflow` (comando abaixo). A base não o cria. |
| Label do namespace | `gearflow.io/messaging-access=true`, aplicada por quem administra o cluster. A label de Pod `gearflow.io/rabbitmq-client: "true"` já está no Deployment; **as duas são necessárias**, senão a NetworkPolicy do broker bloqueia a 5672. |
| PostgreSQL 17 **exclusivo** | Nenhum outro serviço acessa este banco. Para demo: `local-postgres/` (precisa de uma StorageClass padrão; o cluster tem o driver EBS CSI, mas confirme a classe padrão). |
| RabbitMQ do `gearflow-infra-broker` | EKS `gearflow-fase4`: `rabbitmq.messaging.svc.cluster.local:5672`, vhost `gearflow`, **AMQP sem TLS** (decisão do grupo em 08/11/2026). Usuário/senha por serviço, ACL e NetworkPolicy continuam valendo. O `base/configmap.yaml` já aponta para ele. |
| Secret `gearflow-inventory-secret` | Chaves: `ConnectionStrings__Postgres`, `Jwt__Secret`, `RabbitMq__Username` e `RabbitMq__Password` (do Secret `rabbitmq-inventory-credentials`, chaves `username`/`password`, namespace `messaging`, criado pelo bootstrap do broker; copie **só** essas duas, nunca a conta administrativa nem as senhas dos outros serviços). Nunca no repositório. |
| Secret `newrelic-apm-secret` | Era do cluster da Fase 3 e **não existe no `gearflow-fase4`**. Opcional: sem ele o agente só não reporta **e o export OTLP é rejeitado pelo New Relic**, sem derrubar a API. |
| SMTP | O Mailpit `smtp-service:1025` era do cluster da Fase 3 e **não existe no `gearflow-fase4`**. Sem SMTP os avisos de estoque (baixo/insuficiente) esgotam o retry e vão para `*_error`, e contam em `integration.errors`; a Saga **não** é afetada (visto no teste de 2026-11-09). |

A imagem `ghcr.io/gearflow-fiap/gearflow-inventory-service` é **pública**: não precisa de `imagePullSecret`.

## Subir (bash / Git Bash)

```bash
# 0) Namespace e a label que a NetworkPolicy do broker exige (quem administra o cluster)
kubectl create namespace gearflow
kubectl label namespace gearflow gearflow.io/messaging-access=true --overwrite

# 1) Postgres de exemplo (pule se houver um Postgres gerenciado). A senha nunca é impressa nem commitada.
PGPW=$(openssl rand -hex 16)
kubectl -n gearflow create secret generic gearflow-inventory-postgres --from-literal=POSTGRES_PASSWORD="$PGPW"
kubectl apply -f k8s/local-postgres/statefulset.yaml

# 2) Secret da aplicação. O Jwt__Secret é o mesmo do monólito/Lambda (no cluster da Fase 3 era a chave Jwt__SigningKey do Secret
#    gearflow-secret; neste cluster use o valor combinado pelo grupo). As credenciais do broker vêm do Secret do próprio broker.
kubectl -n gearflow create secret generic gearflow-inventory-secret \
  --from-literal=ConnectionStrings__Postgres="Host=inventory-postgres;Port=5432;Database=gearflow_inventory;Username=gearflow;Password=$PGPW" \
  --from-literal=Jwt__Secret='<segredo HMAC do grupo, >= 32 caracteres>' \
  --from-literal=RabbitMq__Username="$(kubectl -n messaging get secret rabbitmq-inventory-credentials -o jsonpath='{.data.username}' | base64 -d)" \
  --from-literal=RabbitMq__Password="$(kubectl -n messaging get secret rabbitmq-inventory-credentials -o jsonpath='{.data.password}' | base64 -d)"

# 3) Aplicação (use o digest/tag publicados pela ação gearflow-inventory-service.docker-publish)
kubectl kustomize k8s/base \
  | sed "s#image: gearflow-inventory-service:unpublished#image: ghcr.io/gearflow-fiap/gearflow-inventory-service:sha-<commit>#" \
  | kubectl apply -f -
kubectl -n gearflow rollout status deployment/gearflow-inventory
```

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
só na `main`, exigindo o CI verde para o mesmo commit), espera a imagem `sha-<commit>` no GHCR, fixa o deploy no digest, confere o cluster
(namespace, label, Secret e a porta 5672 do broker), valida no servidor (`--dry-run=server`), aplica, espera o rollout e testa `/health/ready`.
O `workflow_run` **só vale a partir da `main`** (branch padrão do repositório), então o workflow precisa estar nela.

**Como ele alcança o cluster (padrão do infra-broker).** O AWS Academy não tem OIDC nem permite roles novas, e o endpoint do EKS só aceita IPs
liberados. Por isso o workflow usa as credenciais temporárias da sessão do laboratório, abre **um único /32 (o IP do runner)** com o
`scripts/cd/cluster_access.py` do infra-broker (fixado no commit `5a7e1d1` e conferido por hash; o hash continua igual na `homolog`), faz o
deploy e **fecha a liberação sempre** (`always()`), sem remover nenhuma regra existente. Um arquivo de recuperação fica como artefato por 1 dia,
caso o runner caia antes de fechar (como recuperar: `docs/cd.md` do infra-broker).

| Onde | Nome | Quem fornece |
|---|---|---|
| Secrets da organização, **liberados a este repositório** | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_SESSION_TOKEN` (da mesma sessão Academy, atualizados juntos quando expira) | Dono do infra-broker / administrador da organização |
| Variáveis da organização, **liberadas a este repositório** | `AWS_REGION` (`us-east-1`), `EKS_CLUSTER_NAME` (`gearflow-fase4`) | Idem |
| Variável do repositório | `DEPLOY_ENABLED` (`true`); opcional `KUSTOMIZE_DIR` (padrão `k8s/base`; `k8s/overlays/kong` só se houver Kong) | Este repositório |
| Environment `inventory-homologation` | (aprovação opcional) | Settings → Environments |

No cluster (checado no preflight, que falha com mensagem clara): namespace `gearflow` com a label `gearflow.io/messaging-access=true`,
Secret `gearflow-inventory-secret` e Service `rabbitmq` com a porta 5672. Não há role a mapear: segundo o `docs/cd.md` do infra-broker, o acesso
Kubernetes é o da identidade Academy que criou o cluster (não verificado por mim no cluster).

**Limites.** O GitHub só serializa os runs **deste** repositório: se outro repositório abrir o endpoint ao mesmo tempo, o EKS recusa a segunda
atualização (a execução falha) e, no pior caso, uma liberação é sobrescrita. Combine os deploys ou centralize o acesso num workflow reutilizável do
infra-broker. As credenciais da sessão expiram: se o deploy falhar em `Autentica`, renove os 3 secrets. **Este workflow ainda não rodou no GitHub.**

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

- `kubectl kustomize` + kubeconform `-strict` nas três saídas (CI: job `manifests`).
- Smoke test no Docker (2026-11-04) com a imagem real do GHCR reproduzindo o `securityContext` (uid 1654, raiz somente leitura, sem
  capabilities), Postgres 17 (uid 999) e RabbitMQ com vhost `gearflow`: sobe, `/health/ready` Healthy, API responde com JWT, a topologia aparece
  no vhost e o agente do New Relic escreve em `/tmp/newrelic`.
- Teste contra a topologia e as ACLs reais do infra-broker (2026-11-09): seção "Permissões no broker" acima.
- Não foi aplicado em nenhum cluster.

## Atenção: migration concorrente no primeiro deploy

A migration roda no startup (`Program.cs`). Com 2 réplicas subindo ao mesmo tempo num banco **vazio**, uma pode cair uma vez com
`42P07: relation "InboxState" already exists` (reproduzido no smoke test). O Kubernetes reinicia o pod e na segunda tentativa tudo já está
aplicado. Para evitar o ruído no vídeo: `kubectl -n gearflow scale deployment/gearflow-inventory --replicas=1` logo após o primeiro apply
(ou antes dele, via `--replicas` no apply) e voltar para 2 depois; a correção definitiva é um lock de migration no código.
