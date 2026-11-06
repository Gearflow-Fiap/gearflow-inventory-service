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
| RabbitMQ | Host/vhost no `base/configmap.yaml` (hoje `rabbitmq.gearflow.svc.cluster.local`, vhost `gearflow`). **Ainda não há manifesto de RabbitMQ no gearflow-infra-broker**; o usuário precisa poder configurar o vhost (o MassTransit cria exchanges e filas). |
| Secret `gearflow-inventory-secret` | Chaves: `ConnectionStrings__Postgres`, `Jwt__Secret`, `RabbitMq__Username`, `RabbitMq__Password`. Nunca no repositório. |
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
  --from-literal=RabbitMq__Username='<usuário do broker>' \
  --from-literal=RabbitMq__Password='<senha do broker>'

# 3) Aplicação (use o digest/tag publicados pela ação gearflow-inventory-service.docker-publish)
kubectl kustomize k8s/overlays/kong \
  | sed "s#image: gearflow-inventory-service:unpublished#image: ghcr.io/gearflow-fiap/gearflow-inventory-service:sha-<commit>#" \
  | kubectl apply -f -
kubectl -n gearflow rollout status deployment/gearflow-inventory
```

Para o vídeo, o catálogo de demonstração (peças P1 a P6, IDs fixos) sai com `Seed__EnableDevData: "true"` no ConfigMap.
Preços para o Operation: `GET http://gearflow-inventory.gearflow.svc.cluster.local:8080/api/inventory/parts/{id}` (chamada interna, sem Kong).

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
