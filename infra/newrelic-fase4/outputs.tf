output "dashboard_url" {
  description = "Link do dashboard da Fase 4 (todos os serviços) no New Relic"
  value       = newrelic_one_dashboard.fase4.permalink
}
