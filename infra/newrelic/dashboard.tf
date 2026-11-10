# Dashboard do Inventory (equivalente ao newrelic-dashboard.tf da Fase 3). As métricas de negócio chegam por OTLP (Meter
# "GearFlow.Business") e ficam disponíveis como `Metric`; latência e recursos do cluster vêm do APM/OTel e do nri-bundle.
resource "newrelic_one_dashboard" "inventory" {
  name        = "GearFlow Inventory — Fase 4"
  permissions = "public_read_only"

  page {
    name = "Estoque e Saga"

    widget_line {
      title  = "Reservas por desfecho (reserved / insufficient / already_reserved / ignored_after_release)"
      row    = 1
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(inventory.reservations) WHERE service.name = '${var.service_name}' FACET outcome TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_bar {
      title  = "Passos da Saga: volume"
      row    = 1
      column = 7
      width  = 3
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(inventory.releases) AS 'Liberações (compensação)', sum(inventory.consumptions) AS 'Baixas definitivas', sum(inventory.replenishments) AS 'Reposições' WHERE service.name = '${var.service_name}' SINCE 1 day ago"
      }
    }

    widget_billboard {
      title  = "Alertas de estoque baixo"
      row    = 1
      column = 10
      width  = 3
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(inventory.low_stock_alerts) AS 'Estoque baixo' WHERE service.name = '${var.service_name}' SINCE 1 day ago"
      }
    }

    widget_bar {
      title  = "Duração média de cada passo da Saga (ms)"
      row    = 4
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT average(inventory.saga.step.duration) WHERE service.name = '${var.service_name}' FACET step SINCE 1 day ago"
      }
    }

    widget_bar {
      title  = "Falhas de processamento por origem"
      row    = 4
      column = 7
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(integration.errors) WHERE service.name = '${var.service_name}' FACET source SINCE 7 days ago"
      }
    }
  }

  page {
    name = "Infraestrutura e performance"

    widget_line {
      title  = "Latência da API (p50/p95/p99)"
      row    = 1
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT percentile(http.server.request.duration, 50, 95, 99) WHERE service.name = '${var.service_name}' TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_line {
      title  = "Kubernetes: CPU por pod"
      row    = 4
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM K8sContainerSample SELECT average(cpuCoresUtilization) WHERE ${local.pod_filter} FACET podName TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_line {
      title  = "Kubernetes: memória por pod"
      row    = 4
      column = 7
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM K8sContainerSample SELECT average(memoryWorkingSetUtilization) WHERE ${local.pod_filter} FACET podName TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_billboard {
      title  = "Healthcheck: uptime (/health/ready)"
      row    = 1
      column = 7
      width  = 3
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM SyntheticCheck SELECT percentage(count(*), WHERE result = 'SUCCESS') AS 'Uptime %' WHERE monitorName = '${local.monitor_name}' SINCE 1 day ago"
      }
    }
  }
}
