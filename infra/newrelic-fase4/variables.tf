variable "newrelic_account_id" {
  description = "ID da conta New Relic (a mesma da Fase 3). UI: menu da conta > Administration > ID da conta"
  type        = string

  # O ID de exemplo do terraform.tfvars.example faz o New Relic responder "Access restricted" (a chave não tem acesso a essa conta),
  # que parece um erro de permissão. Recusar aqui evita perder tempo, e o Copy-Item do README sobrescreve o terraform.tfvars.
  validation {
    condition     = can(regex("^[0-9]+$", var.newrelic_account_id)) && var.newrelic_account_id != "0000000"
    error_message = "newrelic_account_id deve ser o ID numérico real da conta (não o 0000000 do exemplo). Edite o terraform.tfvars; não rode Copy-Item de novo, que sobrescreve o arquivo."
  }
}

variable "newrelic_api_key" {
  description = "User API Key do New Relic (NerdGraph) para gerenciar o dashboard via Terraform. Não é a license key de ingestão"
  type        = string
  sensitive   = true
}

variable "dashboard_permissions" {
  description = <<-EOT
    Quem vê/edita o dashboard: public_read_only (o mesmo da Fase 3), public_read_write ou private. Se o apply falhar com
    "Access restricted", teste private: usuários sem permissão para dashboards públicos só conseguem criar privados.
  EOT
  type        = string
  default     = "public_read_only"

  validation {
    condition     = contains(["private", "public_read_only", "public_read_write"], var.dashboard_permissions)
    error_message = "dashboard_permissions deve ser private, public_read_only ou public_read_write."
  }
}

variable "service_names" {
  description = <<-EOT
    service.name que cada serviço exporta por OTLP (o nome no New Relic). Os quatro não seguem o mesmo padrão: confira em
    Observability.cs de cada repositório (OS e Billing: AddService("gearflow-…-service"); Operation: OperationTelemetry.Name;
    Inventory: ObservabilityExtensions). Se um serviço mudar o nome, ajuste aqui, senão os widgets dele ficam vazios.
  EOT
  type = object({
    os        = string
    billing   = string
    operation = string
    inventory = string
  })
  default = {
    os        = "gearflow-os-service"
    billing   = "gearflow-budget-service"
    operation = "GearFlow.Operation"
    inventory = "GearFlow.Inventory.Api"
  }
}

variable "cluster_name" {
  description = "Nome do cluster como o nri-bundle o registra (global.cluster do Helm). Usado só na página Plataforma e deploy"
  type        = string
  default     = "gearflow"
}

variable "kubernetes_namespace" {
  description = "Namespace onde os serviços rodam (o do Inventory e do OS; o Billing também usa gearflow no perfil Academy)"
  type        = string
  default     = "gearflow"
}
