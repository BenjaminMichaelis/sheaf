output "app_identity" {
  value = azurerm_user_assigned_identity.app_identity
}

output "backend_container_app_name" {
  description = "The name of the backend container app"
  value       = module.backend_container_app.name
}

output "resource_group_name" {
  description = "The name of the resource group"
  value       = azurerm_resource_group.resource_group.name
}

output "database_connection_string" {
  description = "The connection string for the SQL database"
  value       = module.sql.connection_string
}

output "backend_url" {
  description = "The URL of the backend API"
  value       = "https://${module.backend_container_app.fqdn}"
}

output "applicationinsights_connection_string" {
  description = "The connection string for Application Insights"
  value       = module.application_insights.application_insights.connection_string
  sensitive   = true
}