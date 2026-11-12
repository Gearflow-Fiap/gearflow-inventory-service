#!/usr/bin/env bash
# Prepara, de forma idempotente, o que o Inventory exige no cluster antes do Deployment (chamado pelo delivery.yml; também serve à mão,
# com o kubectl já apontando para o cluster):
#
#   1. valida as credenciais do broker (Secret `rabbitmq-inventory-credentials` do namespace `messaging`) e o JWT;
#   2. cria o namespace `gearflow` (se faltar) com a label que a NetworkPolicy do broker exige;
#   3. sobe o PostgreSQL exclusivo do Inventory (k8s/local-postgres[-auto]) com uma senha gerada UMA vez e preservada;
#   4. grava o Secret `gearflow-inventory-secret` (connection string, JWT e só o usuário/senha do Inventory no broker);
#   5. grava o Secret `newrelic-apm-secret` (licença do New Relic) quando NEW_RELIC_LICENSE_KEY é informada.
#
# Variáveis:
#   POSTGRES_DIR  (obrigatória) k8s/local-postgres (cluster convencional) ou k8s/local-postgres-auto (EKS Auto Mode)
#   JWT_SECRET    segredo HMAC (>= 32 caracteres) do grupo; no delivery.yml vem do secret JWT_SIGNING_KEY da organização.
#                 Se vazio, mantém o Jwt__Secret do Secret que já existe no cluster.
#   NEW_RELIC_LICENSE_KEY  licença (INGEST - LICENSE) do New Relic do grupo; no delivery.yml vem do secret NEW_RELIC_LICENSE_KEY.
#                 Opcional: se vazia, mantém o Secret `newrelic-apm-secret` que já existe (ou avisa que o agente não vai reportar).
#                 O Secret é do namespace, não só do Inventory: o OS usa o mesmo nome e a mesma chave.
#   NAMESPACE (gearflow), BROKER_NAMESPACE (messaging), BROKER_SECRET (rabbitmq-inventory-credentials), BROKER_USER (gearflow_inventory)
#
# Nenhum valor secreto vai para argumento de processo nem para o log: eles passam por arquivos 0600 (apagados ao sair) e por `printf`
# embutido do bash; no GitHub Actions os valores lidos do cluster também são mascarados (::add-mask::).
set -euo pipefail
umask 077

NAMESPACE="${NAMESPACE:-gearflow}"
BROKER_NAMESPACE="${BROKER_NAMESPACE:-messaging}"
BROKER_SECRET="${BROKER_SECRET:-rabbitmq-inventory-credentials}"
BROKER_USER="${BROKER_USER:-gearflow_inventory}"
POSTGRES_DIR="${POSTGRES_DIR:?Defina POSTGRES_DIR (k8s/local-postgres ou k8s/local-postgres-auto).}"

APP_SECRET=gearflow-inventory-secret
NR_SECRET=newrelic-apm-secret         # mesmo nome e chave que o Deployment (secretKeyRef, optional) e o OS esperam
NR_KEY=NEW_RELIC_LICENSE_KEY
PG_SECRET=gearflow-inventory-postgres
PG_PVC=data-inventory-postgres-0
PG_STATEFULSET=inventory-postgres

log() { printf '%s\n' "$*"; }
fail() { printf '::error::%s\n' "$*" >&2; exit 1; }
mask() { if [ -n "${GITHUB_ACTIONS:-}" ] && [ -n "$1" ]; then printf '::add-mask::%s\n' "$1"; fi; }

work=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/inventory-bootstrap.XXXXXX")
trap 'rm -rf "$work"' EXIT

# Valor (decodificado) de uma chave de um Secret; vazio se o Secret ou a chave não existirem.
secret_field() {
  local encoded
  encoded=$(kubectl get secret "$2" -n "$1" --ignore-not-found -o "jsonpath={.data.$3}")
  if [ -n "$encoded" ]; then printf '%s' "$encoded" | base64 -d; fi
}

[ -f "$POSTGRES_DIR/kustomization.yaml" ] || fail "POSTGRES_DIR=$POSTGRES_DIR não tem kustomization.yaml (rode a partir da raiz do repositório)."

# --- 1. validações (antes de criar qualquer coisa, para uma falha não deixar o cluster pela metade) ---------------------------------
log "1/5 Validando credenciais do broker, o JWT e a licença do New Relic"
broker_user=$(secret_field "$BROKER_NAMESPACE" "$BROKER_SECRET" username)
broker_pass=$(secret_field "$BROKER_NAMESPACE" "$BROKER_SECRET" password)
[ "$broker_user" = "$BROKER_USER" ] \
  || fail "O Secret $BROKER_SECRET (namespace $BROKER_NAMESPACE) não existe ou o usuário não é $BROKER_USER. O GearFlow CD do infra-broker já rodou neste cluster?"
[ -n "$broker_pass" ] || fail "O Secret $BROKER_SECRET (namespace $BROKER_NAMESPACE) não tem senha."
mask "$broker_pass"

jwt="${JWT_SECRET:-}"
if [ -z "$jwt" ]; then
  jwt=$(secret_field "$NAMESPACE" "$APP_SECRET" Jwt__Secret)
  [ -z "$jwt" ] || log "    JWT_SECRET não informado: mantendo o Jwt__Secret do Secret $APP_SECRET que já existe."
fi
[ -n "$jwt" ] || fail "Informe o JWT na variável de ambiente JWT_SECRET (no deploy vem do secret JWT_SIGNING_KEY da organização, liberado a este repositório; o segredo HMAC do grupo, >= 32 caracteres): o Secret $APP_SECRET ainda não existe."
mask "$jwt"
[ "${#jwt}" -ge 32 ] || fail "O JWT precisa ter pelo menos 32 caracteres (HMAC-SHA256)."
case "$jwt" in
  *$'\n'* | *$'\r'*) fail "O JWT não pode ter quebra de linha." ;;
esac
[[ ! "$jwt" =~ ^[[:space:]] && ! "$jwt" =~ [[:space:]]$ ]] || fail "O JWT não pode começar nem terminar com espaço."

# A licença é opcional: o Deployment declara o Secret como `optional`, então a API sobe sem ele (só não reporta ao New Relic).
nr_key="${NEW_RELIC_LICENSE_KEY:-}"
if [ -n "$nr_key" ]; then
  mask "$nr_key"
  # Um espaço ou quebra de linha colado junto com a chave é o erro mais comum e o New Relic só responde 403, sem dizer o motivo.
  [[ ! "$nr_key" =~ [[:space:]] ]] || fail "NEW_RELIC_LICENSE_KEY não pode ter espaço nem quebra de linha (cole só a chave, sem o que vem antes ou depois)."
  # A User API Key (NRAK-, a do Terraform de infra/newrelic) NÃO é a licença: o agente e o OTLP a rejeitam.
  case "$nr_key" in
    NRAK-*) fail "NEW_RELIC_LICENSE_KEY começa com NRAK-: essa é a User API Key (usada só no Terraform). Use a chave do tipo INGEST - LICENSE." ;;
    eu01xx*) printf '::warning::%s\n' "A licença é de uma conta da região EU, mas o ConfigMap envia o OTLP para otlp.nr-data.net (US). Troque Otlp__Endpoint por https://otlp.eu01.nr-data.net:4317 em k8s/base/configmap.yaml." ;;
  esac
else
  log "    NEW_RELIC_LICENSE_KEY não informada: o Secret $NR_SECRET não será alterado."
fi

# --- 2. namespace e label ------------------------------------------------------------------------------------------------------------
log "2/5 Namespace $NAMESPACE e a label gearflow.io/messaging-access=true"
kubectl get namespace "$NAMESPACE" >/dev/null 2>&1 \
  || kubectl create namespace "$NAMESPACE" >/dev/null \
  || kubectl get namespace "$NAMESPACE" >/dev/null   # outro serviço pode ter criado no mesmo instante
# `label` (e não `apply`) para não disputar o last-applied do namespace com o Billing, que também o aplica.
kubectl label namespace "$NAMESPACE" gearflow.io/messaging-access=true --overwrite >/dev/null

# --- 3. PostgreSQL --------------------------------------------------------------------------------------------------------------------
log "3/5 PostgreSQL exclusivo do Inventory ($POSTGRES_DIR)"
pg_pass=$(secret_field "$NAMESPACE" "$PG_SECRET" POSTGRES_PASSWORD)
if [ -z "$pg_pass" ]; then
  # A senha é gravada no volume pelo initdb, na primeira subida. Um volume antigo com Secret novo significaria senha diferente.
  if [ -n "$(kubectl get pvc "$PG_PVC" -n "$NAMESPACE" --ignore-not-found -o name)" ]; then
    fail "O PVC $PG_PVC já existe mas o Secret $PG_SECRET não: a senha do banco no volume é desconhecida. Recupere o Secret ou apague o PVC (perde os dados) e rode de novo."
  fi
  pg_pass=$(openssl rand -hex 16)
  printf 'POSTGRES_PASSWORD=%s\n' "$pg_pass" > "$work/postgres.env"
  kubectl create secret generic "$PG_SECRET" -n "$NAMESPACE" --from-env-file="$work/postgres.env" >/dev/null
  log "    Secret $PG_SECRET criado (senha gerada; não é impressa)."
else
  log "    Secret $PG_SECRET já existe: a senha foi mantida."
fi
mask "$pg_pass"
kubectl apply --dry-run=server -k "$POSTGRES_DIR" >/dev/null
kubectl apply -k "$POSTGRES_DIR"
kubectl rollout status "statefulset/$PG_STATEFULSET" -n "$NAMESPACE" --timeout=600s

# --- 4. Secret da aplicação -----------------------------------------------------------------------------------------------------------
log "4/5 Secret $APP_SECRET"
version_before=$(kubectl get secret "$APP_SECRET" -n "$NAMESPACE" --ignore-not-found -o jsonpath='{.metadata.resourceVersion}')
{
  printf 'ConnectionStrings__Postgres=Host=%s;Port=5432;Database=gearflow_inventory;Username=gearflow;Password=%s\n' "$PG_STATEFULSET" "$pg_pass"
  printf 'Jwt__Secret=%s\n' "$jwt"
  printf 'RabbitMq__Username=%s\n' "$broker_user"
  printf 'RabbitMq__Password=%s\n' "$broker_pass"
} > "$work/app.env"
kubectl create secret generic "$APP_SECRET" -n "$NAMESPACE" --from-env-file="$work/app.env" --dry-run=client -o yaml \
  | kubectl apply -f - >/dev/null
version_after=$(kubectl get secret "$APP_SECRET" -n "$NAMESPACE" -o jsonpath='{.metadata.resourceVersion}')

# Pods só leem Secret na subida: o delivery reinicia o Deployment se o conteúdo mudou (e ele já existia).
changed=false
[ "$version_before" = "$version_after" ] || changed=true
log "    Secret $APP_SECRET aplicado (alterado nesta execução: $changed)."

# --- 5. Secret do New Relic -----------------------------------------------------------------------------------------------------------
# Mesmo nome e chave que o Deployment lê (secretKeyRef optional) e que o OS usa: quem rodar depois aplica o mesmo valor, sem conflito.
log "5/5 Secret $NR_SECRET"
nr_changed=false
if [ -n "$nr_key" ]; then
  nr_before=$(kubectl get secret "$NR_SECRET" -n "$NAMESPACE" --ignore-not-found -o jsonpath='{.metadata.resourceVersion}')
  printf '%s=%s\n' "$NR_KEY" "$nr_key" > "$work/newrelic.env"
  kubectl create secret generic "$NR_SECRET" -n "$NAMESPACE" --from-env-file="$work/newrelic.env" --dry-run=client -o yaml \
    | kubectl apply -f - >/dev/null
  nr_after=$(kubectl get secret "$NR_SECRET" -n "$NAMESPACE" -o jsonpath='{.metadata.resourceVersion}')
  [ "$nr_before" = "$nr_after" ] || nr_changed=true
  log "    Secret $NR_SECRET aplicado (alterado nesta execução: $nr_changed)."
elif [ -n "$(kubectl get secret "$NR_SECRET" -n "$NAMESPACE" --ignore-not-found -o name)" ]; then
  log "    Secret $NR_SECRET já existe: mantido como está."
else
  printf '::warning::%s\n' "O Secret $NR_SECRET não existe e NEW_RELIC_LICENSE_KEY não foi informada: a API sobe, mas o agente e o export OTLP não reportam ao New Relic."
fi

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  printf 'app_secret_changed=%s\n' "$changed" >> "$GITHUB_OUTPUT"
  printf 'newrelic_secret_changed=%s\n' "$nr_changed" >> "$GITHUB_OUTPUT"
fi
log "Bootstrap concluído."
