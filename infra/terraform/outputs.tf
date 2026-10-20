output "api_url" {
  description = "Base URL of the deployed API. Append /swagger in Development, /api/plans to smoke-test."
  value       = "https://${azurerm_linux_web_app.api.default_hostname}"
}

output "sql_server_fqdn" {
  description = "Azure SQL server FQDN."
  value       = azurerm_mssql_server.sql.fully_qualified_domain_name
}

output "key_vault_name" {
  description = "Key Vault holding the app secrets."
  value       = azurerm_key_vault.kv.name
}

output "resource_group" {
  description = "Resource group containing everything."
  value       = azurerm_resource_group.rg.name
}
