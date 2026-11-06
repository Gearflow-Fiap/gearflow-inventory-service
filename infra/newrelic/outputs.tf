output "dashboard_url" {
  description = "Link do dashboard do Inventory no New Relic"
  value       = newrelic_one_dashboard.inventory.permalink
}

output "alert_policy_id" {
  description = "ID da política de alertas do Inventory"
  value       = newrelic_alert_policy.inventory.id
}
