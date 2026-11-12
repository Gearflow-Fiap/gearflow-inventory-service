# Dashboard "GearFlow — Fase 4": o equivalente do newrelic-dashboard.tf da Fase 3, mas para os QUATRO serviços (OS, Billing, Operation e
# Inventory) e para o que o enunciado manda mostrar no vídeo:
#
#   Página 1  Fluxo distribuído da OS    -> "Fluxo completo de uma OS passando pelos microsserviços" e
#                                           "Monitoramento e rastreamento dos fluxos distribuídos" (traces que cruzam serviços)
#   Página 2  Saga e compensação         -> "Execução do Saga Pattern e tratamento de falhas" (reserva, liberação, estorno, DLQ)
#   Página 3  Plataforma e deploy        -> "Deploy automatizado": qual imagem (sha do commit) roda em cada pod; CPU/memória
#                                           ("Uso das ferramentas de monitoramento da Fase 3": nri-bundle + New Relic)
#
# Fontes dos dados (nada é gerado aqui, só consultado):
#   - Span:   traces OTLP de cada serviço (todos usam o mesmo trace.id quando o contexto é propagado pelo RabbitMQ).
#   - Metric: OTLP. http.server.request.duration (ASP.NET Core), inventory.* e integration.errors (Inventory), billing.* (Billing),
#             gearflow.operation.* (Operation). O OS só exporta métricas do ASP.NET Core.
#   - K8sContainerSample: vem do nri-bundle (instalado uma vez por cluster por quem o administra; ver docs/OBSERVABILITY.md).
#
# Os NRQLs foram escritos a partir do código dos serviços e NÃO foram executados contra uma conta real. Um widget vazio quer dizer
# "esse serviço ainda não exporta isso" (veja o README desta pasta), não que a consulta esteja errada: confirme no Query builder.
locals {
  svc = var.service_names

  # service.name dos quatro serviços da Fase 4; separa os dados deles do monólito da Fase 3 (GearFlow.Api), que usa a mesma conta.
  service_filter = "service.name IN ('${local.svc.os}', '${local.svc.billing}', '${local.svc.operation}', '${local.svc.inventory}')"

  pod_filter = "clusterName = '${var.cluster_name}' AND namespaceName = '${var.kubernetes_namespace}'"
}

resource "newrelic_one_dashboard" "fase4" {
  name        = "GearFlow — Fase 4"
  permissions = var.dashboard_permissions

  # ── Página 1: o fluxo de uma OS atravessando os serviços ────────────────────────────────────────────────────────────────────────
  page {
    name = "Fluxo distribuído da OS"

    widget_billboard {
      title  = "Serviços enviando traces (últimos 30 min)"
      row    = 1
      column = 1
      width  = 3
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Span SELECT uniqueCount(service.name) AS 'de 4 serviços' WHERE ${local.service_filter} SINCE 30 minutes ago"
      }
    }

    widget_billboard {
      title  = "Traces na última hora"
      row    = 1
      column = 4
      width  = 3
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Span SELECT uniqueCount(trace.id) AS 'traces' WHERE ${local.service_filter} SINCE 1 hour ago"
      }
    }

    widget_line {
      title  = "Requisições recebidas por serviço"
      row    = 1
      column = 7
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Span SELECT count(*) WHERE ${local.service_filter} AND span.kind = 'server' FACET service.name TIMESERIES SINCE 3 hours ago"
      }
    }

    # O coração da demonstração: um trace.id que aparece em vários serviços é a OS atravessando a Saga. Os de mais serviços vêm primeiro.
    widget_table {
      title  = "Traces que atravessam mais de um serviço (abra um para ver o fluxo)"
      row    = 4
      column = 1
      width  = 12
      height = 4

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Span SELECT uniqueCount(service.name) AS 'Serviços', uniques(service.name, 10) AS 'Passou por', count(*) AS 'Spans', max(duration.ms) AS 'Maior span (ms)' WHERE ${local.service_filter} FACET trace.id ORDER BY uniqueCount(service.name) LIMIT 20 SINCE 1 hour ago"
      }
    }

    widget_line {
      title  = "Latência p95 por serviço (ms)"
      row    = 8
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Span SELECT percentile(duration.ms, 95) WHERE ${local.service_filter} AND span.kind = 'server' FACET service.name TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_line {
      title  = "Spans com erro por serviço"
      row    = 8
      column = 7
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Span SELECT count(*) WHERE ${local.service_filter} AND otel.status_code = 'ERROR' FACET service.name TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_table {
      title  = "Últimos spans com erro (o trace.id leva ao fluxo da falha)"
      row    = 11
      column = 1
      width  = 12
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Span SELECT service.name, name, trace.id WHERE ${local.service_filter} AND otel.status_code = 'ERROR' LIMIT 20 SINCE 3 hours ago"
      }
    }
  }

  # ── Página 2: Saga e compensação ────────────────────────────────────────────────────────────────────────────────────────────────
  page {
    name = "Saga e compensação"

    widget_line {
      title  = "Inventory — reservas de estoque por desfecho (reserved / insufficient / already_reserved / ignored_after_release)"
      row    = 1
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(inventory.reservations) WHERE service.name = '${local.svc.inventory}' FACET outcome TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_bar {
      title  = "Inventory — compensação e baixa definitiva"
      row    = 1
      column = 7
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(inventory.releases) AS 'Liberações (compensação)', sum(inventory.consumptions) AS 'Baixas definitivas', sum(inventory.replenishments) AS 'Reposições' WHERE service.name = '${local.svc.inventory}' SINCE 1 day ago"
      }
    }

    widget_bar {
      title  = "Billing — eventos de negócio (orçamento, pagamento, estorno)"
      row    = 4
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(billing.business.events) WHERE service.name = '${local.svc.billing}' FACET `event.name` SINCE 1 day ago"
      }
    }

    widget_bar {
      title  = "Operation — comandos da Saga por ação e resultado"
      row    = 4
      column = 7
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(gearflow.operation.commands) WHERE service.name = '${local.svc.operation}' FACET action, outcome SINCE 1 day ago"
      }
    }

    widget_bar {
      title  = "Falhas de integração por serviço e origem (integration.errors)"
      row    = 7
      column = 1
      width  = 4
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT sum(integration.errors) WHERE ${local.service_filter} FACET service.name, source SINCE 7 days ago"
      }
    }

    widget_billboard {
      title  = "Mensagens em fila de erro (DLQ)"
      row    = 7
      column = 5
      width  = 4
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT latest(billing.dlq.depth) AS 'Billing (profundidade)', sum(billing.messages.deadlettered) AS 'Billing (enviadas à DLQ, 1 dia)' WHERE service.name = '${local.svc.billing}' SINCE 1 day ago"
      }
    }

    widget_billboard {
      title  = "Outbox pendente (mensagens ainda não publicadas)"
      row    = 7
      column = 9
      width  = 4
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT latest(gearflow.operation.outbox.pending) AS 'Operation', latest(billing.outbox.pending) AS 'Billing' WHERE ${local.service_filter} SINCE 1 hour ago"
      }
    }

    widget_line {
      title  = "Inventory — duração dos passos da Saga (ms)"
      row    = 10
      column = 1
      width  = 12
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM Metric SELECT average(inventory.saga.step.duration) WHERE service.name = '${local.svc.inventory}' FACET step TIMESERIES SINCE 3 hours ago"
      }
    }
  }

  # ── Página 3: plataforma e deploy (precisa do nri-bundle no cluster) ────────────────────────────────────────────────────────────
  page {
    name = "Plataforma e deploy"

    widget_line {
      title  = "Kubernetes — CPU por pod"
      row    = 1
      column = 1
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM K8sContainerSample SELECT average(cpuCoresUtilization) WHERE ${local.pod_filter} FACET podName TIMESERIES SINCE 3 hours ago"
      }
    }

    widget_line {
      title  = "Kubernetes — memória por pod"
      row    = 1
      column = 7
      width  = 6
      height = 3

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM K8sContainerSample SELECT average(memoryWorkingSetUtilization) WHERE ${local.pod_filter} FACET podName TIMESERIES SINCE 3 hours ago"
      }
    }

    # Evidência do deploy automatizado: depois de cada delivery o pod passa a rodar a imagem :sha-<commit> do GHCR.
    widget_table {
      title  = "Imagem em execução por pod (evidência do deploy automatizado)"
      row    = 4
      column = 1
      width  = 12
      height = 4

      nrql_query {
        account_id = var.newrelic_account_id
        query      = "FROM K8sContainerSample SELECT latest(imageName) AS 'Imagem', latest(restartCount) AS 'Reinícios', latest(status) AS 'Status' WHERE ${local.pod_filter} FACET podName LIMIT 50 SINCE 30 minutes ago"
      }
    }
  }
}
