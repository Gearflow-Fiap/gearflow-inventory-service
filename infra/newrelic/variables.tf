variable "newrelic_account_id" {
  description = "ID da conta New Relic (a mesma da Fase 3). UI: menu da conta > Administration > ID da conta"
  type        = string
}

variable "newrelic_api_key" {
  description = "User API Key do New Relic (NerdGraph) para gerenciar alertas e dashboards via Terraform. Não é a license key de ingestão"
  type        = string
  sensitive   = true
}

variable "newrelic_alert_email" {
  description = "E-mail que recebe os alertas do Inventory"
  type        = string
}

variable "service_name" {
  description = "service.name que a API exporta por OTLP (AddObservability em Program.cs). Todo NRQL filtra por ele"
  type        = string
  default     = "GearFlow.Inventory.Api"
}

variable "cluster_name" {
  description = "Nome do cluster como o nri-bundle o registra (global.cluster do Helm). Usado nos widgets e alertas de CPU/memória"
  type        = string
  default     = "gearflow-fase4"
}

variable "kubernetes_namespace" {
  description = "Namespace onde o Deployment gearflow-inventory roda"
  type        = string
  default     = "gearflow"
}

variable "healthcheck_url" {
  description = "URL pública do /health/ready para o monitor sintético (ex.: http://<ELB do Kong>/health/ready). Vazio = não cria o monitor nem o alerta de uptime"
  type        = string
  default     = ""
}
