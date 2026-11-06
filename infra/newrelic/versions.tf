terraform {
  required_version = ">= 1.6"

  required_providers {
    newrelic = {
      source  = "newrelic/newrelic"
      version = "~> 3.0"
    }
  }
}

# Mesmo provider e região do gearflow-infra-k8s da Fase 3. A api_key é a User API Key (NRAK-...) do NerdGraph,
# diferente da license key de ingestão que o agente e o OTLP usam.
provider "newrelic" {
  account_id = var.newrelic_account_id
  api_key    = var.newrelic_api_key
  region     = "US"
}
