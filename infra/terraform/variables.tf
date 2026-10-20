variable "name_prefix" {
  description = "Prefix for all resource names (lowercase letters, digits, hyphens)."
  type        = string
  default     = "saasstarter"
}

variable "environment" {
  description = "Environment name, used in resource names and tags."
  type        = string
  default     = "dev"
}

variable "location" {
  description = "Azure region."
  type        = string
  default     = "eastus"
}

variable "app_service_sku" {
  description = "App Service plan SKU. B1 is the cheapest always-on-capable tier; P1v3 for production."
  type        = string
  default     = "B1"
}

variable "sql_sku" {
  description = "Azure SQL database SKU."
  type        = string
  default     = "Basic"
}

variable "sql_admin_login" {
  description = "Azure SQL administrator login."
  type        = string
  default     = "saasadmin"
}

variable "sql_admin_password" {
  description = "Azure SQL administrator password. Pass via TF_VAR_sql_admin_password or a .tfvars file — never commit it."
  type        = string
  sensitive   = true
}

variable "jwt_key" {
  description = "JWT signing key for the API (32+ characters). Stored in Key Vault and injected as an app setting."
  type        = string
  sensitive   = true
}

variable "stripe_secret_key" {
  description = "Stripe TEST MODE secret key (sk_test_...). Leave empty to run without billing."
  type        = string
  sensitive   = true
  default     = ""
}

variable "stripe_webhook_secret" {
  description = "Stripe webhook signing secret (whsec_...)."
  type        = string
  sensitive   = true
  default     = ""
}
