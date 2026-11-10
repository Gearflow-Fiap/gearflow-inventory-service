# Alertas do Inventory no New Relic. Mesma estrutura do newrelic-alerts.tf do gearflow-infra-k8s (Fase 3): política, canal
# de e-mail, workflow e condições NRQL. Os alertas de negócio usam as métricas de BusinessMetrics (Meter GearFlow.Business);
# os nomes dos instrumentos são contrato e estão travados por testes unitários (BusinessMetricsTests).

locals {
  monitor_name = "GearFlow Inventory — /health/ready"
  pod_filter   = "clusterName = '${var.cluster_name}' AND namespaceName = '${var.kubernetes_namespace}' AND podName LIKE 'gearflow-inventory-%'"
}

# ── Canal de notificação (e-mail) ──────────────────────────────────────────
resource "newrelic_notification_destination" "email" {
  name = "gearflow-inventory-alertas-email"
  type = "EMAIL"

  property {
    key   = "email"
    value = var.newrelic_alert_email
  }
}

resource "newrelic_notification_channel" "email" {
  name           = "gearflow-inventory-alertas-email-channel"
  type           = "EMAIL"
  destination_id = newrelic_notification_destination.email.id
  product        = "IINT"

  property {
    key   = "subject"
    value = "[GearFlow Inventory] {{issueTitle}}"
  }
}

resource "newrelic_alert_policy" "inventory" {
  name                = "GearFlow Inventory — Fase 4"
  incident_preference = "PER_CONDITION_AND_TARGET"
}

resource "newrelic_workflow" "inventory" {
  name                  = "GearFlow Inventory — notificação por e-mail"
  muting_rules_handling = "NOTIFY_ALL_ISSUES"

  issues_filter {
    name = "gearflow-inventory-policy-filter"
    type = "FILTER"

    predicate {
      attribute = "labels.policyIds"
      operator  = "EXACTLY_MATCHES"
      values    = [newrelic_alert_policy.inventory.id]
    }
  }

  destination {
    channel_id = newrelic_notification_channel.email.id
  }
}

# ── 1. Latência da API ──────────────────────────────────────────────────────
# http.server.request.duration (histograma OTel, em segundos): instrumentação ASP.NET Core do AddObservability.
resource "newrelic_nrql_alert_condition" "api_latency" {
  policy_id                    = newrelic_alert_policy.inventory.id
  name                         = "Inventory — latência da API acima do esperado (p95)"
  type                         = "static"
  enabled                      = true
  violation_time_limit_seconds = 3600
  fill_option                  = "none"

  nrql {
    query = "SELECT percentile(http.server.request.duration, 95) FROM Metric WHERE service.name = '${var.service_name}'"
  }

  critical {
    operator              = "above"
    threshold             = 1
    threshold_duration    = 300
    threshold_occurrences = "ALL"
  }

  warning {
    operator              = "above"
    threshold             = 0.5
    threshold_duration    = 300
    threshold_occurrences = "ALL"
  }
}

# ── 2. Recursos do Kubernetes ───────────────────────────────────────────────
# K8sContainerSample vem do nri-bundle (newrelic-infrastructure) instalado no cluster, como na Fase 3.
resource "newrelic_nrql_alert_condition" "k8s_cpu" {
  policy_id                    = newrelic_alert_policy.inventory.id
  name                         = "Inventory — CPU do container acima de 80%"
  type                         = "static"
  enabled                      = true
  violation_time_limit_seconds = 3600
  fill_option                  = "none"

  nrql {
    query = "SELECT average(cpuCoresUtilization) FROM K8sContainerSample WHERE ${local.pod_filter} FACET podName"
  }

  critical {
    operator              = "above"
    threshold             = 80
    threshold_duration    = 300
    threshold_occurrences = "ALL"
  }
}

resource "newrelic_nrql_alert_condition" "k8s_memory" {
  policy_id                    = newrelic_alert_policy.inventory.id
  name                         = "Inventory — memória do container acima de 80%"
  type                         = "static"
  enabled                      = true
  violation_time_limit_seconds = 3600
  fill_option                  = "none"

  nrql {
    query = "SELECT average(memoryWorkingSetUtilization) FROM K8sContainerSample WHERE ${local.pod_filter} FACET podName"
  }

  critical {
    operator              = "above"
    threshold             = 80
    threshold_duration    = 300
    threshold_occurrences = "ALL"
  }
}

# ── 3. Healthcheck e uptime ─────────────────────────────────────────────────
# Monitor sintético externo no /health/ready (checa o banco; o RabbitMQ fora vira Degraded, HTTP 200). Só existe se
# healthcheck_url for informada: o Ingress do Kong só publica /api/inventory, então a URL depende de quem expõe o health.
resource "newrelic_synthetics_monitor" "healthcheck" {
  count = var.healthcheck_url == "" ? 0 : 1

  name              = local.monitor_name
  type              = "SIMPLE"
  status            = "ENABLED"
  locations_public  = ["US_EAST_1"]
  period            = "EVERY_5_MINUTES"
  uri               = var.healthcheck_url
  verify_ssl        = false
  validation_string = "\"status\":\"Healthy\""

  custom_header {
    name  = "Accept"
    value = "application/json"
  }
}

resource "newrelic_nrql_alert_condition" "healthcheck_down" {
  count = var.healthcheck_url == "" ? 0 : 1

  policy_id                    = newrelic_alert_policy.inventory.id
  name                         = "Inventory — healthcheck indisponível (/health/ready)"
  type                         = "static"
  enabled                      = true
  violation_time_limit_seconds = 3600
  fill_option                  = "none"

  nrql {
    query = "SELECT filter(count(*), WHERE result != 'SUCCESS') FROM SyntheticCheck WHERE monitorName = '${local.monitor_name}'"
  }

  critical {
    operator              = "above"
    threshold             = 0
    threshold_duration    = 300
    threshold_occurrences = "AT_LEAST_ONCE"
  }
}

# ── 4. Falhas de processamento ──────────────────────────────────────────────
# integration.errors{source}: comando da Saga que falhou (vai para a DLQ) ou exceção inesperada. É o mesmo nome do
# monólito da Fase 3, então o dashboard e o alerta de lá enxergam o Inventory também.
resource "newrelic_nrql_alert_condition" "integration_errors" {
  policy_id                    = newrelic_alert_policy.inventory.id
  name                         = "Inventory — falhas no processamento (comandos da Saga)"
  type                         = "static"
  enabled                      = true
  violation_time_limit_seconds = 3600
  fill_option                  = "static"
  fill_value                   = 0

  nrql {
    query = "SELECT sum(integration.errors) FROM Metric WHERE service.name = '${var.service_name}' FACET source"
  }

  critical {
    operator              = "above"
    threshold             = 0
    threshold_duration    = 300
    threshold_occurrences = "AT_LEAST_ONCE"
  }
}

# ── 5. Negócio: estoque ─────────────────────────────────────────────────────
# Estoque insuficiente: a OS não pôde ser atendida e a Saga fica aguardando reposição (StockInsufficient).
resource "newrelic_nrql_alert_condition" "insufficient_stock" {
  policy_id                    = newrelic_alert_policy.inventory.id
  name                         = "Inventory — OS sem estoque suficiente (StockInsufficient)"
  type                         = "static"
  enabled                      = true
  violation_time_limit_seconds = 3600
  fill_option                  = "static"
  fill_value                   = 0

  nrql {
    query = "SELECT sum(inventory.reservations) FROM Metric WHERE service.name = '${var.service_name}' AND outcome = 'insufficient'"
  }

  critical {
    operator              = "above"
    threshold             = 0
    threshold_duration    = 300
    threshold_occurrences = "AT_LEAST_ONCE"
  }
}

# Estoque baixo: o item ficou no mínimo ou abaixo (LowStockAlert, o mesmo aviso por e-mail ao estoquista).
resource "newrelic_nrql_alert_condition" "low_stock" {
  policy_id                    = newrelic_alert_policy.inventory.id
  name                         = "Inventory — estoque baixo (LowStockAlert)"
  type                         = "static"
  enabled                      = true
  violation_time_limit_seconds = 3600
  fill_option                  = "static"
  fill_value                   = 0

  nrql {
    query = "SELECT sum(inventory.low_stock_alerts) FROM Metric WHERE service.name = '${var.service_name}'"
  }

  critical {
    operator              = "above"
    threshold             = 0
    threshold_duration    = 300
    threshold_occurrences = "AT_LEAST_ONCE"
  }
}
