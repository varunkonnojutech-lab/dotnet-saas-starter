terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
  }

  # Configure a remote backend (Azure Storage) for real teams, e.g.:
  # backend "azurerm" {
  #   resource_group_name  = "tfstate-rg"
  #   storage_account_name = "tfstate<unique>"
  #   container_name       = "tfstate"
  #   key                  = "dotnet-saas-starter.tfstate"
  # }
}

provider "azurerm" {
  features {}
}

data "azurerm_client_config" "current" {}

locals {
  name = "${var.name_prefix}-${var.environment}"
  tags = {
    project     = "dotnet-saas-starter"
    environment = var.environment
    managed_by  = "terraform"
  }
}

resource "azurerm_resource_group" "rg" {
  name     = "${local.name}-rg"
  location = var.location
  tags     = local.tags
}

# ---------------------------------------------------------------- hosting ---

resource "azurerm_service_plan" "plan" {
  name                = "${local.name}-plan"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  os_type             = "Linux"
  sku_name            = var.app_service_sku
  tags                = local.tags
}

resource "azurerm_linux_web_app" "api" {
  name                = "${local.name}-api"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  service_plan_id     = azurerm_service_plan.plan.id
  tags                = local.tags

  identity {
    type = "SystemAssigned"
  }

  site_config {
    always_on = true

    application_stack {
      dotnet_version = "8.0"
    }
  }

  # Secrets are injected as app settings for the starter. Production
  # hardening: replace the values with Key Vault references, e.g.
  # "@Microsoft.KeyVault(SecretUri=https://<vault>.vault.azure.net/secrets/JwtKey/)"
  # — the web app's managed identity already has Get access below.
  app_settings = {
    "ASPNETCORE_ENVIRONMENT"               = "Production"
    "Database__Provider"                   = "SqlServer"
    "Database__SeedDemo"                   = "false"
    "Jwt__Key"                             = var.jwt_key
    "Stripe__SecretKey"                    = var.stripe_secret_key
    "Stripe__WebhookSecret"                = var.stripe_webhook_secret
    "Email__Provider"                      = "SendGrid"
    "APPINSIGHTS_CONNECTION_STRING"        = azurerm_application_insights.appi.connection_string
    "ApplicationInsightsAgent_EXTENSION"   = "~3"
    "SCM_DO_BUILD_DURING_DEPLOYMENT"       = "true"
    "ConnectionStrings__DefaultConnection" = "Server=tcp:${azurerm_mssql_server.sql.fully_qualified_domain_name},1433;Initial Catalog=${azurerm_mssql_database.db.name};Persist Security Info=False;User ID=${var.sql_admin_login};Password=${var.sql_admin_password};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
    "WEBSITES_ENABLE_APP_SERVICE_STORAGE"  = "false"
  }
}

# ------------------------------------------------------------------ data ---

resource "azurerm_mssql_server" "sql" {
  name                         = "${local.name}-sql"
  resource_group_name          = azurerm_resource_group.rg.name
  location                     = azurerm_resource_group.rg.location
  version                      = "12.0"
  administrator_login          = var.sql_admin_login
  administrator_login_password = var.sql_admin_password
  tags                         = local.tags
}

resource "azurerm_mssql_database" "db" {
  name      = "saasstarter"
  server_id = azurerm_mssql_server.sql.id
  sku_name  = var.sql_sku
  tags      = local.tags
}

# Lets App Service (and other Azure services) reach the server. For real
# production, prefer Private Endpoints / VNet integration over this rule.
resource "azurerm_mssql_firewall_rule" "allow_azure" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.sql.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# --------------------------------------------------------------- secrets ---

resource "azurerm_key_vault" "kv" {
  name                       = substr("${replace(local.name, "-", "")}kv", 0, 24)
  resource_group_name        = azurerm_resource_group.rg.name
  location                   = azurerm_resource_group.rg.location
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  soft_delete_retention_days = 7
  tags                       = local.tags

  # The identity running Terraform can manage secrets.
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = data.azurerm_client_config.current.object_id

    secret_permissions = ["Get", "List", "Set", "Delete"]
  }

  # The API's managed identity can read them.
  access_policy {
    tenant_id = data.azurerm_client_config.current.tenant_id
    object_id = azurerm_linux_web_app.api.identity[0].principal_id

    secret_permissions = ["Get", "List"]
  }
}

resource "azurerm_key_vault_secret" "jwt_key" {
  name         = "JwtKey"
  value        = var.jwt_key
  key_vault_id = azurerm_key_vault.kv.id
}

# -------------------------------------------------------------- telemetry ---

resource "azurerm_log_analytics_workspace" "law" {
  name                = "${local.name}-law"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

resource "azurerm_application_insights" "appi" {
  name                = "${local.name}-appi"
  resource_group_name = azurerm_resource_group.rg.name
  location            = azurerm_resource_group.rg.location
  workspace_id        = azurerm_log_analytics_workspace.law.id
  application_type    = "web"
  tags                = local.tags
}
