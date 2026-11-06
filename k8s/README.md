# Inventory no Kubernetes

Mesmo padrão do `gearflow-operation-service`: a base **não cria banco, broker nem credenciais**; eles são pré-requisitos do namespace
`gearflow` (o EKS do `gearflow-infra-k8s`). O Postgres de `local-postgres/` é só um exemplo para desenvolvimento/demonstração, fora da
base, como o `local-mongo` do Billing.

```
k8s/
├── base/              Deployment (2 réplicas), Service :8080, ConfigMap
├── overlays/kong/     base + Ingress /api/inventory (plugins gearflow-jwt e gearflow-rate-limit)
└── local-postgres/    StatefulSet postgres:17 de exemplo (instância exclusiva do Inventory)
```

## Pré-requisitos

| O quê | Observação |
|---|---|
| Namespace `gearflow` | Já existe (gearflow-infra-k8s). A base não o cria. |
| PostgreSQL 17 **exclusivo** | Nenhum outro serviço acessa este banco. Para demo: `local-postgres/` (precisa de StorageClass padrão no cluster). |
| RabbitMQ do `gearflow-infra-broker` | Já implantado no EKS `gearflow-fase4` (05/11): `rabbitmq.messaging.svc.cluster.local:5671`, vhost `gearflow`, **AMQPS obrigatório** (não existe 5672). O `base/configmap.yaml` já aponta para ele e liga `RabbitMq__Tls`. **Pendência com o dono do broker: as permissões** (veja [Permissões no broker](#permissões-no-broker-pendência-com-o-dono-do-broker)). |
| ConfigMap `rabbitmq-ca` | O `ca.crt` **público** do broker, no namespace do Inventory. Quem tem o arquivo cria (comando abaixo). O Deployment o monta em `/etc/gearflow/rabbitmq-ca`; sem ele o Pod fica em `ContainerCreating`. |
| Label do namespace | `gearflow.io/messaging-access=true`, aplicada por quem administra o cluster. A label de Pod `gearflow.io/rabbitmq-client: "true"` já está no Deployment; **as duas são necessárias**, senão a NetworkPolicy do broker bloqueia a 5671. |
| Secret `gearflow-inventory-secret` | Chaves: `ConnectionStrings__Postgres`, `Jwt__Secret`, `RabbitMq__Username` (o usuário `gearflow_inventory` do broker), `RabbitMq__Password` (a senha dele, em `.local/service-credentials.json` do infra-broker, que não é versionado). Nunca no repositório. |
| Secret `newrelic-apm-secret` | Já criado pelo gearflow-infra-k8s (chave `NEW_RELIC_LICENSE_KEY`). Opcional: sem ele o agente só não reporta **e o export OTLP (abaixo) é rejeitado pelo New Relic**, sem derrubar a API. |
| SMTP | O Mailpit `smtp-service:1025` que já roda no namespace. |

A imagem `ghcr.io/gearflow-fiap/gearflow-inventory-service` é **pública**: não precisa de `imagePullSecret`.

## Subir (bash / Git Bash)

```bash
# 1) Postgres de exemplo (pule se houver um Postgres gerenciado). A senha nunca é impressa nem commitada.
PGPW=$(openssl rand -hex 16)
kubectl -n gearflow create secret generic gearflow-inventory-postgres --from-literal=POSTGRES_PASSWORD="$PGPW"
kubectl apply -f k8s/local-postgres/statefulset.yaml

# 2) Secret da aplicação. O Jwt__Secret é o mesmo do monólito (chave Jwt__SigningKey do Secret gearflow-secret).
kubectl -n gearflow create secret generic gearflow-inventory-secret \
  --from-literal=ConnectionStrings__Postgres="Host=inventory-postgres;Port=5432;Database=gearflow_inventory;Username=gearflow;Password=$PGPW" \
  --from-literal=Jwt__Secret="$(kubectl -n gearflow get secret gearflow-secret -o jsonpath='{.data.Jwt__SigningKey}' | base64 -d)" \
  --from-literal=RabbitMq__Username='gearflow_inventory' \
  --from-literal=RabbitMq__Password='<senha do usuário gearflow_inventory>'

# 2b) Acesso ao broker: a CA pública (só o ca.crt, nunca a chave) e a label que a NetworkPolicy do broker exige no namespace.
kubectl -n gearflow create configmap rabbitmq-ca --from-file=ca.crt=<caminho do ca.crt do infra-broker>
kubectl label namespace gearflow gearflow.io/messaging-access=true --overwrite   # quem administra o cluster

# 3) Aplicação (use o digest/tag publicados pela ação gearflow-inventory-service.docker-publish)
kubectl kustomize k8s/overlays/kong \
  | sed "s#image: gearflow-inventory-service:unpublished#image: ghcr.io/gearflow-fiap/gearflow-inventory-service:sha-<commit>#" \
  | kubectl apply -f -
kubectl -n gearflow rollout status deployment/gearflow-inventory
```

Para o vídeo, o catálogo de demonstração (peças P1 a P6, IDs fixos) sai com `Seed__EnableDevData: "true"` no ConfigMap.
Preços para o Operation: `GET http://gearflow-inventory.gearflow.svc.cluster.local:8080/api/inventory/parts/{id}` (chamada interna, sem Kong).

## Permissões no broker (pendência com o dono do broker)

O TLS, o host, a porta e as labels estão corretos. **O que ainda impede o Inventory de subir no broker compartilhado são as permissões.**
Hoje `rabbitmq/service-permissions.json` do infra-broker dá ao usuário `gearflow_inventory` `configure: "^$"`, `write` só em
`gearflow.events` e `read` só em `gearflow.inventory.*`, e o README do broker diz que as bibliotecas devem usar a topologia
existente. O MassTransit **declara** exchanges e filas ao iniciar (`ACCESS_REFUSED` com `configure` vazio), e publica nas exchanges
`inventory.<fato>`, que não são `gearflow.events`.

O que o Inventory declara, publica e consome (de `QueueNames` e `InventoryEventNames`):

| Tipo | Nomes |
|---|---|
| Filas (e uma exchange de mesmo nome cada) | `inventory-reserve-stock`, `inventory-release-stock`, `inventory-consume-stock`, `inventory-low-stock-alert-email`, `inventory-stock-insufficient-email`, mais `<fila>_error` e `<fila>_skipped` de cada uma |
| Exchanges de eventos que ele publica | `inventory.stock-reserved`, `.stock-insufficient`, `.stock-released`, `.stock-consumed`, `.stock-replenished`, `.low-stock-alert` |
| Exchanges por tipo de mensagem (consumo) | `GearFlow.Contracts:ReserveStock`, `:ReleaseStock`, `:ConsumeStock`, `:LowStockAlert`, `:StockInsufficient` |

**Proposta de ACL** para `gearflow_inventory` (a validar com o dono do broker; não foi testada contra um RabbitMQ real):
`configure`, `write` e `read` = `^(inventory[-.].*|GearFlow\.Contracts:.*)$`. O Billing, o Operation e o OS têm necessidades parecidas
com os próprios prefixos; além disso o OS e o Operation precisam de `read` nas exchanges `inventory.stock-*` para ligar as filas deles, e
o OS hoje publica os comandos na **exchange padrão**, que o broker proíbe. Isso é uma decisão do grupo (D-02): ou o broker libera os
prefixos dos serviços, ou os serviços passam a usar só a topologia pré-criada, o que o MassTransit não suporta.

## Deploy automático (`.github/workflows/delivery.yml`, ação `gearflow-inventory-service.delivery`)

Desligado até existir a variável `DEPLOY_ENABLED=true`. Roda depois da ação `gearflow-inventory-service.ci` na `main`, espera a imagem `sha-<commit>` no GHCR, fixa o
deploy no digest, valida no servidor (`--dry-run=server`), aplica, espera o rollout e testa `/health/ready`.

| Onde | Nome | Quem fornece |
|---|---|---|
| Variável do repositório | `DEPLOY_ENABLED` (`true`), `AWS_ROLE_ARN`, `AWS_REGION`, `EKS_CLUSTER_NAME` | Quem administra a AWS/EKS |
| Environment `inventory-homologation` | (aprovação opcional) | Settings → Environments |

A role precisa confiar no OIDC do GitHub para este repositório e estar mapeada no cluster com permissão no namespace `gearflow`.

## SonarCloud (job `sonar` da ação `gearflow-inventory-service.ci`)

Desligado até existir a variável `SONAR_PROJECT_KEY`. Também precisa de `SONAR_ORG` (variável) e `SONAR_TOKEN` (secret). O Billing usa a
organização `gearflow-fiap`; falta alguém com permissão importar este repositório lá.

## Observabilidade (reaproveita a Fase 3)

O ConfigMap traz `Otlp__Endpoint=https://otlp.nr-data.net:4317` e o Deployment monta `OTEL_EXPORTER_OTLP_HEADERS=api-key=<licença>` a partir
do Secret `newrelic-apm-secret`: o mesmo caminho do monólito (`gearflow-infra-k8s/terraform/app.tf`). A API exporta traces e métricas por
OTLP e inclui os spans do MassTransit, então um trace que começa numa requisição REST ou numa mensagem do RabbitMQ deve aparecer inteiro no
New Relic (o serviço que **envia** a mensagem também precisa propagar o contexto). **Ainda não foi visto no New Relic**: depende de rodar no
cluster. O agente APM do Dockerfile continua ativo. Para desligar o OTLP, remova `Otlp__Endpoint` do ConfigMap.
Métricas de negócio, alertas, dashboard e o pré-requisito do `nri-bundle` no cluster: [docs/OBSERVABILITY.md](../docs/OBSERVABILITY.md).

## O que foi verificado (2026-11-04)

- `kubectl kustomize` + kubeconform `-strict` nas três saídas (CI: job `manifests`).
- Smoke test no Docker com a imagem real do GHCR reproduzindo o `securityContext` (uid 1654, raiz somente leitura, sem capabilities),
  Postgres 17 (uid 999) e RabbitMQ com vhost `gearflow`: sobe, `/health/ready` Healthy, API responde com JWT, a topologia aparece no vhost
  e o agente do New Relic escreve em `/tmp/newrelic`.
- Em 2026-11-05, depois de acrescentar o OTLP: `kubectl kustomize` renderiza `k8s/base` e `k8s/overlays/kong` sem erro e o `$(NEW_RELIC_LICENSE_KEY)`
  do header sai intacto (o Kubernetes o expande). O kubeconform e o smoke test **não foram reexecutados** (Docker parado nesta máquina).
- Não foi aplicado em nenhum cluster.

## Atenção: migration concorrente no primeiro deploy

A migration roda no startup (`Program.cs`). Com 2 réplicas subindo ao mesmo tempo num banco **vazio**, uma pode cair uma vez com
`42P07: relation "InboxState" already exists` (reproduzido no smoke test). O Kubernetes reinicia o pod e na segunda tentativa tudo já está
aplicado. Para evitar o ruído no vídeo: `kubectl -n gearflow scale deployment/gearflow-inventory --replicas=1` logo após o primeiro apply
(ou antes dele, via `--replicas` no apply) e voltar para 2 depois; a correção definitiva é um lock de migration no código.
